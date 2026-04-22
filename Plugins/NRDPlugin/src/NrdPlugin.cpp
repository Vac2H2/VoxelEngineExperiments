#include "../include/NrdPluginAPI.h"

#include <Windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>

#include <algorithm>
#include <array>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <wrl/client.h>

#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D12.h"

#include "NRI.h"
#include "Extensions/NRIHelper.h"
#include "Extensions/NRIWrapperD3D12.h"
#include "NRD.h"
#include "NRDDescs.h"
#include "NRDSettings.h"
#include "NRDIntegration.hpp"

namespace
{
    using Microsoft::WRL::ComPtr;

    constexpr nrd::Identifier kReblurDiffuseOcclusionIdentifier = 1;
    constexpr D3D12_RESOURCE_STATES kUnityShaderReadState =
        D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE |
        D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
    constexpr UINT32 kCommandContextCount = 3;

    struct CameraContext
    {
        nrd::Integration integration = {};
        uint16_t width = 0;
        uint16_t height = 0;
        uint32_t lastFrameIndex = 0;
        bool hasHistory = false;
        bool needsRecreate = true;
    };

    struct CommandContext
    {
        ComPtr<ID3D12CommandAllocator> allocator;
        ComPtr<ID3D12GraphicsCommandList> commandList;
        UINT64 lastFenceValue = 0;
    };

    struct PluginState
    {
        std::mutex mutex;
        IUnityInterfaces* unityInterfaces = nullptr;
        IUnityGraphics* unityGraphics = nullptr;
        IUnityGraphicsD3D12v8* d3d12 = nullptr;
        ID3D12Device* device = nullptr;
        ID3D12CommandQueue* graphicsQueue = nullptr;
        UnityGfxRenderer renderer = kUnityGfxRendererNull;
        int denoiseEventId = -1;
        bool backendReady = false;
        bool backendActive = false;
        bool hasPendingSettings = false;
        bool hasPendingFrame = false;
        NrdSettingsNative pendingSettings = {};
        NrdFrameDataNative pendingFrame = {};
        std::unordered_map<int, std::unique_ptr<CameraContext>> cameraContexts;
        std::array<CommandContext, kCommandContextCount> commandContexts = {};
        HANDLE commandFenceEvent = nullptr;
        std::string lastError = "NRD native backend has not been initialized.";
    };

    PluginState g_State = {};

    void DebugLog(const char* message)
    {
        if (message == nullptr)
        {
            return;
        }

        OutputDebugStringA("[NRDPlugin] ");
        OutputDebugStringA(message);
        OutputDebugStringA("\n");
    }

    void SetLastErrorLocked(const char* message)
    {
        g_State.lastError = message != nullptr ? message : "Unknown NRD plugin error.";
        g_State.backendActive = false;
        DebugLog(g_State.lastError.c_str());
    }

    void ClearLastErrorLocked()
    {
        g_State.lastError.clear();
    }

    void DestroyContextsLocked()
    {
        g_State.cameraContexts.clear();
    }

    void DestroyCommandContextsLocked()
    {
        for (CommandContext& context : g_State.commandContexts)
        {
            context.commandList.Reset();
            context.allocator.Reset();
            context.lastFenceValue = 0;
        }

        if (g_State.commandFenceEvent != nullptr)
        {
            CloseHandle(g_State.commandFenceEvent);
            g_State.commandFenceEvent = nullptr;
        }
    }

    bool ConfigureDenoiseEventLocked()
    {
        if (g_State.unityGraphics == nullptr || g_State.d3d12 == nullptr)
        {
            return false;
        }

        if (g_State.denoiseEventId < 0)
        {
            g_State.denoiseEventId = g_State.unityGraphics->ReserveEventIDRange(1);
        }

        UnityD3D12PluginEventConfig config = {};
        config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_Allow;
        config.flags =
            kUnityD3D12EventConfigFlag_FlushCommandBuffers |
            kUnityD3D12EventConfigFlag_SyncWorkerThreads;
        config.ensureActiveRenderTextureIsBound = false;
        g_State.d3d12->ConfigureEvent(g_State.denoiseEventId, &config);
        return true;
    }

    bool InitializeBackendLocked()
    {
        if (g_State.unityInterfaces == nullptr || g_State.unityGraphics == nullptr)
        {
            SetLastErrorLocked("Unity graphics interfaces are not available.");
            return false;
        }

        g_State.renderer = g_State.unityGraphics->GetRenderer();
        if (g_State.renderer != kUnityGfxRendererD3D12)
        {
            SetLastErrorLocked("NRDPlugin requires Unity running on Direct3D 12.");
            return false;
        }

        g_State.d3d12 = g_State.unityInterfaces->Get<IUnityGraphicsD3D12v8>();
        if (g_State.d3d12 == nullptr)
        {
            SetLastErrorLocked("IUnityGraphicsD3D12v8 is unavailable.");
            return false;
        }

        g_State.device = g_State.d3d12->GetDevice();
        g_State.graphicsQueue = g_State.d3d12->GetCommandQueue();
        if (g_State.device == nullptr || g_State.graphicsQueue == nullptr)
        {
            SetLastErrorLocked("Unity D3D12 device or graphics queue is unavailable.");
            return false;
        }

        ConfigureDenoiseEventLocked();
        g_State.backendReady = true;
        ClearLastErrorLocked();
        return true;
    }

    bool EnsureCommandContextLocked(uint32_t frameIndex, CommandContext*& outContext)
    {
        outContext = nullptr;

        if (g_State.device == nullptr || g_State.d3d12 == nullptr)
        {
            SetLastErrorLocked("NRDPlugin cannot create a dedicated command list before D3D12 initialization.");
            return false;
        }

        if (g_State.commandFenceEvent == nullptr)
        {
            g_State.commandFenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
            if (g_State.commandFenceEvent == nullptr)
            {
                SetLastErrorLocked("NRDPlugin failed to create a D3D12 fence event.");
                return false;
            }
        }

        CommandContext& context = g_State.commandContexts[frameIndex % kCommandContextCount];
        ID3D12Fence* frameFence = g_State.d3d12->GetFrameFence();
        if (frameFence != nullptr &&
            context.lastFenceValue != 0 &&
            frameFence->GetCompletedValue() < context.lastFenceValue)
        {
            HRESULT waitResult = frameFence->SetEventOnCompletion(context.lastFenceValue, g_State.commandFenceEvent);
            if (FAILED(waitResult))
            {
                SetLastErrorLocked("NRDPlugin failed to arm the Unity frame fence for command allocator reuse.");
                return false;
            }

            DWORD waitStatus = WaitForSingleObject(g_State.commandFenceEvent, 10000);
            if (waitStatus != WAIT_OBJECT_0)
            {
                SetLastErrorLocked("NRDPlugin timed out waiting for its previous D3D12 command list to finish.");
                return false;
            }
        }

        if (context.allocator == nullptr)
        {
            HRESULT allocatorResult = g_State.device->CreateCommandAllocator(
                D3D12_COMMAND_LIST_TYPE_DIRECT,
                IID_PPV_ARGS(context.allocator.ReleaseAndGetAddressOf()));
            if (FAILED(allocatorResult))
            {
                SetLastErrorLocked("NRDPlugin failed to create a D3D12 command allocator.");
                return false;
            }
        }

        if (context.commandList == nullptr)
        {
            HRESULT commandListResult = g_State.device->CreateCommandList(
                0,
                D3D12_COMMAND_LIST_TYPE_DIRECT,
                context.allocator.Get(),
                nullptr,
                IID_PPV_ARGS(context.commandList.ReleaseAndGetAddressOf()));
            if (FAILED(commandListResult))
            {
                SetLastErrorLocked("NRDPlugin failed to create a D3D12 graphics command list.");
                return false;
            }

            context.commandList->Close();
        }

        if (FAILED(context.allocator->Reset()) || FAILED(context.commandList->Reset(context.allocator.Get(), nullptr)))
        {
            SetLastErrorLocked("NRDPlugin failed to reset its dedicated D3D12 command list.");
            return false;
        }

        outContext = &context;
        return true;
    }

    CameraContext& GetOrCreateCameraContextLocked(int cameraId)
    {
        auto iterator = g_State.cameraContexts.find(cameraId);
        if (iterator != g_State.cameraContexts.end())
        {
            return *iterator->second;
        }

        std::unique_ptr<CameraContext> context = std::make_unique<CameraContext>();
        CameraContext& reference = *context;
        g_State.cameraContexts.emplace(cameraId, std::move(context));
        return reference;
    }

    bool RecreateCameraContextLocked(CameraContext& context, uint16_t width, uint16_t height)
    {
        if (g_State.device == nullptr || g_State.graphicsQueue == nullptr)
        {
            SetLastErrorLocked("NRDPlugin cannot recreate NRD context before D3D12 device initialization.");
            return false;
        }

        nri::QueueFamilyD3D12Desc queueFamilyDesc = {};
        queueFamilyDesc.d3d12Queues = &g_State.graphicsQueue;
        queueFamilyDesc.queueNum = 1;
        queueFamilyDesc.queueType = nri::QueueType::GRAPHICS;

        nri::DeviceCreationD3D12Desc deviceDesc = {};
        deviceDesc.d3d12Device = g_State.device;
        deviceDesc.queueFamilies = &queueFamilyDesc;
        deviceDesc.queueFamilyNum = 1;
        deviceDesc.enableNRIValidation = false;
        deviceDesc.enableMemoryZeroInitialization = false;
        deviceDesc.disableD3D12EnhancedBarriers = true;
        deviceDesc.disableNVAPIInitialization = true;

        const nrd::DenoiserDesc denoiserDescs[] =
        {
            {kReblurDiffuseOcclusionIdentifier, nrd::Denoiser::REBLUR_DIFFUSE_OCCLUSION}
        };

        nrd::InstanceCreationDesc instanceDesc = {};
        instanceDesc.denoisers = denoiserDescs;
        instanceDesc.denoisersNum = 1;

        nrd::IntegrationCreationDesc integrationDesc = {};
        strncpy_s(integrationDesc.name, "VoxelEngineNRD", _TRUNCATE);
        integrationDesc.resourceWidth = width;
        integrationDesc.resourceHeight = height;
        integrationDesc.queuedFrameNum = 3;
        integrationDesc.enableWholeLifetimeDescriptorCaching = false;
        integrationDesc.autoWaitForIdle = true;

        nrd::Result result = context.integration.RecreateD3D12(integrationDesc, instanceDesc, deviceDesc);
        if (result != nrd::Result::SUCCESS)
        {
            SetLastErrorLocked("NRDIntegration::RecreateD3D12 failed.");
            return false;
        }

        context.width = width;
        context.height = height;
        context.lastFrameIndex = 0;
        context.hasHistory = false;
        context.needsRecreate = false;
        return true;
    }

    void CopyMatrix(float destination[16], const float source[16])
    {
        std::memcpy(destination, source, sizeof(float) * 16);
    }

    nrd::CommonSettings BuildCommonSettings(const NrdSettingsNative& settings, const NrdFrameDataNative& frame, const CameraContext& context, bool restartHistory)
    {
        nrd::CommonSettings commonSettings = {};
        CopyMatrix(commonSettings.worldToViewMatrix, frame.currentWorldToView);
        CopyMatrix(commonSettings.worldToViewMatrixPrev, frame.previousWorldToView);
        CopyMatrix(commonSettings.viewToClipMatrix, frame.currentViewToClip);
        CopyMatrix(commonSettings.viewToClipMatrixPrev, frame.previousViewToClip);

        commonSettings.motionVectorScale[0] = 1.0f;
        commonSettings.motionVectorScale[1] = 1.0f;
        commonSettings.motionVectorScale[2] = 1.0f;
        commonSettings.resourceSize[0] = static_cast<uint16_t>(frame.width);
        commonSettings.resourceSize[1] = static_cast<uint16_t>(frame.height);
        commonSettings.resourceSizePrev[0] = context.hasHistory ? context.width : static_cast<uint16_t>(frame.width);
        commonSettings.resourceSizePrev[1] = context.hasHistory ? context.height : static_cast<uint16_t>(frame.height);
        commonSettings.rectSize[0] = static_cast<uint16_t>(frame.width);
        commonSettings.rectSize[1] = static_cast<uint16_t>(frame.height);
        commonSettings.rectSizePrev[0] = context.hasHistory ? context.width : static_cast<uint16_t>(frame.width);
        commonSettings.rectSizePrev[1] = context.hasHistory ? context.height : static_cast<uint16_t>(frame.height);
        commonSettings.viewZScale = 1.0f;
        commonSettings.denoisingRange = settings.denoisingRange;
        commonSettings.frameIndex = static_cast<uint32_t>(std::max(frame.frameIndex, 0));
        commonSettings.accumulationMode = restartHistory ? nrd::AccumulationMode::RESTART : nrd::AccumulationMode::CONTINUE;
        commonSettings.isMotionVectorInWorldSpace = false;
        commonSettings.enableValidation = settings.enableValidation != 0;
        return commonSettings;
    }

    nrd::ReblurSettings BuildReblurSettings(const NrdSettingsNative& settings)
    {
        nrd::ReblurSettings reblurSettings = {};
        reblurSettings.hitDistanceParameters.A = settings.hitDistanceA;
        reblurSettings.hitDistanceParameters.B = settings.hitDistanceB;
        reblurSettings.hitDistanceParameters.C = settings.hitDistanceC;
        reblurSettings.maxAccumulatedFrameNum = static_cast<uint32_t>(std::max(settings.maxAccumulatedFrameNum, 1));
        reblurSettings.maxFastAccumulatedFrameNum = static_cast<uint32_t>(std::max(settings.maxFastAccumulatedFrameNum, 1));
        reblurSettings.historyFixFrameNum = static_cast<uint32_t>(std::max(settings.historyFixFrameNum, 1));
        return reblurSettings;
    }

    DXGI_FORMAT ResolveDxgiFormat(ID3D12Resource* resource)
    {
        return resource != nullptr
            ? resource->GetDesc().Format
            : DXGI_FORMAT_UNKNOWN;
    }

    nrd::Resource MakeInitialShaderReadTexture(ID3D12Resource* resource)
    {
        nrd::Resource result = {};
        result.d3d12.resource = resource;
        result.d3d12.format = ResolveDxgiFormat(resource);
        result.state = {
            nri::AccessBits::SHADER_RESOURCE,
            nri::Layout::SHADER_RESOURCE,
            nri::StageBits::COMPUTE_SHADER
        };
        return result;
    }

    bool DenoisePendingFrameLocked()
    {
        if (!g_State.backendReady && !InitializeBackendLocked())
        {
            return false;
        }

        if (!g_State.hasPendingSettings || !g_State.hasPendingFrame)
        {
            SetLastErrorLocked("NRDPlugin render event was triggered without pending frame data.");
            return false;
        }

        const NrdSettingsNative settings = g_State.pendingSettings;
        const NrdFrameDataNative frame = g_State.pendingFrame;
        g_State.hasPendingFrame = false;

        if (frame.cameraId == 0)
        {
            SetLastErrorLocked("NRDPlugin received an invalid camera identifier.");
            return false;
        }

        if (frame.width <= 0 || frame.height <= 0)
        {
            SetLastErrorLocked("NRDPlugin received an invalid frame resolution.");
            return false;
        }

        ID3D12Resource* diffHitDist = static_cast<ID3D12Resource*>(frame.noisyAmbientNormalizedHitDistance);
        ID3D12Resource* motion = static_cast<ID3D12Resource*>(frame.motion);
        ID3D12Resource* normalRoughness = static_cast<ID3D12Resource*>(frame.normalRoughness);
        ID3D12Resource* viewZ = static_cast<ID3D12Resource*>(frame.viewZ);
        ID3D12Resource* denoisedOutput = static_cast<ID3D12Resource*>(frame.denoisedAmbientOutput);
        if (diffHitDist == nullptr || motion == nullptr || normalRoughness == nullptr || viewZ == nullptr || denoisedOutput == nullptr)
        {
            SetLastErrorLocked("NRDPlugin received null D3D12 resource pointers.");
            return false;
        }

        CommandContext* commandContext = nullptr;
        if (!EnsureCommandContextLocked(static_cast<uint32_t>(std::max(frame.frameIndex, 0)), commandContext) || commandContext == nullptr)
        {
            return false;
        }

        CameraContext& context = GetOrCreateCameraContextLocked(frame.cameraId);
        bool restartHistory = context.needsRecreate || !context.hasHistory || static_cast<uint32_t>(frame.frameIndex) <= context.lastFrameIndex;
        if (context.needsRecreate ||
            context.width != static_cast<uint16_t>(frame.width) ||
            context.height != static_cast<uint16_t>(frame.height))
        {
            if (!RecreateCameraContextLocked(context, static_cast<uint16_t>(frame.width), static_cast<uint16_t>(frame.height)))
            {
                return false;
            }

            restartHistory = true;
        }

        nrd::CommonSettings commonSettings = BuildCommonSettings(settings, frame, context, restartHistory);
        nrd::ReblurSettings reblurSettings = BuildReblurSettings(settings);

        context.integration.NewFrame();
        if (context.integration.SetCommonSettings(commonSettings) != nrd::Result::SUCCESS)
        {
            SetLastErrorLocked("NRDIntegration::SetCommonSettings failed.");
            return false;
        }

        if (context.integration.SetDenoiserSettings(kReblurDiffuseOcclusionIdentifier, &reblurSettings) != nrd::Result::SUCCESS)
        {
            SetLastErrorLocked("NRDIntegration::SetDenoiserSettings failed.");
            return false;
        }

        nrd::ResourceSnapshot resourceSnapshot = {};
        resourceSnapshot.restoreInitialState = true;
        resourceSnapshot.SetResource(nrd::ResourceType::IN_MV, MakeInitialShaderReadTexture(motion));
        resourceSnapshot.SetResource(nrd::ResourceType::IN_NORMAL_ROUGHNESS, MakeInitialShaderReadTexture(normalRoughness));
        resourceSnapshot.SetResource(nrd::ResourceType::IN_VIEWZ, MakeInitialShaderReadTexture(viewZ));
        resourceSnapshot.SetResource(nrd::ResourceType::IN_DIFF_HITDIST, MakeInitialShaderReadTexture(diffHitDist));
        resourceSnapshot.SetResource(nrd::ResourceType::OUT_DIFF_HITDIST, MakeInitialShaderReadTexture(denoisedOutput));

        nri::CommandBufferD3D12Desc commandBufferDesc = {};
        commandBufferDesc.d3d12CommandList = commandContext->commandList.Get();

        const nrd::Identifier denoisers[] = {kReblurDiffuseOcclusionIdentifier};
        context.integration.DenoiseD3D12(denoisers, 1, commandBufferDesc, resourceSnapshot);

        if (FAILED(commandContext->commandList->Close()))
        {
            SetLastErrorLocked("NRDPlugin failed to close its dedicated D3D12 command list.");
            return false;
        }

        UnityGraphicsD3D12ResourceState states[5] = {};
        states[0] = {diffHitDist, kUnityShaderReadState, kUnityShaderReadState};
        states[1] = {motion, kUnityShaderReadState, kUnityShaderReadState};
        states[2] = {normalRoughness, kUnityShaderReadState, kUnityShaderReadState};
        states[3] = {viewZ, kUnityShaderReadState, kUnityShaderReadState};
        states[4] = {denoisedOutput, kUnityShaderReadState, kUnityShaderReadState};
        commandContext->lastFenceValue = g_State.d3d12->ExecuteCommandList(
            commandContext->commandList.Get(),
            static_cast<int>(std::size(states)),
            states);
        if (commandContext->lastFenceValue == 0)
        {
            SetLastErrorLocked("NRDPlugin failed to submit its dedicated D3D12 command list.");
            return false;
        }

        context.width = static_cast<uint16_t>(frame.width);
        context.height = static_cast<uint16_t>(frame.height);
        context.lastFrameIndex = static_cast<uint32_t>(std::max(frame.frameIndex, 0));
        context.hasHistory = true;
        context.needsRecreate = false;
        g_State.backendActive = true;
        ClearLastErrorLocked();
        return true;
    }

    void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);

        switch (eventType)
        {
            case kUnityGfxDeviceEventInitialize:
                InitializeBackendLocked();
                break;
            case kUnityGfxDeviceEventBeforeReset:
            case kUnityGfxDeviceEventShutdown:
                DestroyContextsLocked();
                DestroyCommandContextsLocked();
                g_State.backendReady = false;
                g_State.backendActive = false;
                g_State.d3d12 = nullptr;
                g_State.device = nullptr;
                g_State.graphicsQueue = nullptr;
                break;
            case kUnityGfxDeviceEventAfterReset:
                InitializeBackendLocked();
                break;
            default:
                break;
        }
    }

    void UNITY_INTERFACE_API OnRenderEvent(int eventId)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        if (eventId != g_State.denoiseEventId)
        {
            return;
        }

        if (!DenoisePendingFrameLocked())
        {
            g_State.backendActive = false;
        }
    }
}

extern "C"
{
    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
    {
        {
            std::lock_guard<std::mutex> lock(g_State.mutex);
            g_State.unityInterfaces = unityInterfaces;
            g_State.unityGraphics = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityGraphics>() : nullptr;
            if (g_State.unityGraphics != nullptr)
            {
                g_State.unityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
            }
        }

        if (unityInterfaces != nullptr)
        {
            OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
        }
    }

    void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        if (g_State.unityGraphics != nullptr)
        {
            g_State.unityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
        }

        DestroyContextsLocked();
        DestroyCommandContextsLocked();
        g_State.unityInterfaces = nullptr;
        g_State.unityGraphics = nullptr;
        g_State.d3d12 = nullptr;
        g_State.device = nullptr;
        g_State.graphicsQueue = nullptr;
        g_State.renderer = kUnityGfxRendererNull;
        g_State.denoiseEventId = -1;
        g_State.backendReady = false;
        g_State.backendActive = false;
        g_State.hasPendingSettings = false;
        g_State.hasPendingFrame = false;
        g_State.pendingSettings = {};
        g_State.pendingFrame = {};
        g_State.lastError = "NRD native backend has not been initialized.";
    }
}

extern "C"
{
    NRD_PLUGIN_API int NrdPlugin_Initialize(void* unityGraphicsDevice, int graphicsApi)
    {
        (void)unityGraphicsDevice;
        std::lock_guard<std::mutex> lock(g_State.mutex);

        if (graphicsApi != static_cast<int>(kUnityGfxRendererD3D12))
        {
            SetLastErrorLocked("NRDPlugin requires Unity running on Direct3D 12.");
            return 0;
        }

        return InitializeBackendLocked() ? 1 : 0;
    }

    NRD_PLUGIN_API void NrdPlugin_Shutdown(void)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        DestroyContextsLocked();
        DestroyCommandContextsLocked();
        g_State.backendReady = false;
        g_State.backendActive = false;
        g_State.hasPendingSettings = false;
        g_State.hasPendingFrame = false;
    }

    NRD_PLUGIN_API void NrdPlugin_OnResize(int width, int height)
    {
        (void)width;
        (void)height;

        std::lock_guard<std::mutex> lock(g_State.mutex);
        for (auto& entry : g_State.cameraContexts)
        {
            entry.second->needsRecreate = true;
        }
    }

    NRD_PLUGIN_API void NrdPlugin_SetSettings(const NrdSettingsNative* settings)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        if (settings == nullptr)
        {
            SetLastErrorLocked("NrdPlugin_SetSettings received a null settings pointer.");
            return;
        }

        g_State.pendingSettings = *settings;
        g_State.hasPendingSettings = true;
    }

    NRD_PLUGIN_API void NrdPlugin_QueueFrame(const NrdFrameDataNative* frameData)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        if (frameData == nullptr)
        {
            SetLastErrorLocked("NrdPlugin_QueueFrame received a null frame pointer.");
            return;
        }

        g_State.pendingFrame = *frameData;
        g_State.hasPendingFrame = true;
    }

    NRD_PLUGIN_API void* NrdPlugin_GetRenderEventFunc(void)
    {
        return reinterpret_cast<void*>(OnRenderEvent);
    }

    NRD_PLUGIN_API int NrdPlugin_GetDenoiseEventId(void)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        return g_State.denoiseEventId;
    }

    NRD_PLUGIN_API int NrdPlugin_IsBackendActive(void)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        return g_State.backendActive ? 1 : 0;
    }

    NRD_PLUGIN_API const char* NrdPlugin_GetLastError(void)
    {
        std::lock_guard<std::mutex> lock(g_State.mutex);
        return g_State.lastError.empty() ? nullptr : g_State.lastError.c_str();
    }
}
