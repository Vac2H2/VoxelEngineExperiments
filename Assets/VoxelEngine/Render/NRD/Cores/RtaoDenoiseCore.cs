using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelEngine.Render.Cores;
using VoxelEngine.Render.NRD.Bridge;
using VoxelEngine.Render.NRD.Data;

namespace VoxelEngine.Render.NRD.Cores
{
    public static class VoxelNrdIds
    {
        public static readonly int PackedDiffuseHitDistanceTextureId = Shader.PropertyToID("_VoxelEngineNrdPackedDiffuseHitDistance");
        public static readonly int DenoisedAoTextureId = Shader.PropertyToID("_VoxelEngineNrdDenoisedAo");
        public static readonly int NativeBackendActiveId = Shader.PropertyToID("_VoxelEngineNrdNativeBackendActive");
        public static readonly int PreviewAvailableId = Shader.PropertyToID("_VoxelEngineNrdPreviewAvailable");

        public static RenderTargetIdentifier PackedDiffuseHitDistanceTarget => new RenderTargetIdentifier(PackedDiffuseHitDistanceTextureId);
        public static RenderTargetIdentifier DenoisedAoTarget => new RenderTargetIdentifier(DenoisedAoTextureId);
    }

    [Serializable]
    public sealed class RtaoDenoiseCore
    {
        private const string GuidePackShaderName = "Hidden/VoxelEngine/Rendering/NrdGuidePack";
        private static readonly Vector3 DefaultReblurHitDistanceParameters = new Vector3(3.0f, 0.1f, 20.0f);

        private static readonly int NrdPixelStepId = Shader.PropertyToID("_VoxelEngineNrdPixelStep");
        private static readonly int NrdSecondaryPixelStepId = Shader.PropertyToID("_VoxelEngineNrdSecondaryPixelStep");
        private static readonly int NrdGuideModeId = Shader.PropertyToID("_VoxelEngineNrdGuideMode");
        private static readonly int NrdSecondarySourceId = Shader.PropertyToID("_VoxelEngineNrdSecondarySource");
        private static readonly int NrdSecondarySourceTexelSizeId = Shader.PropertyToID("_VoxelEngineNrdSecondarySourceTexelSize");
        private static readonly int NrdHitDistanceParametersId = Shader.PropertyToID("_VoxelEngineNrdHitDistanceParameters");

        [Serializable]
        private readonly struct CameraHistory
        {
            public CameraHistory(Matrix4x4 worldToView, Matrix4x4 viewToClip)
            {
                WorldToView = worldToView;
                ViewToClip = viewToClip;
            }

            public Matrix4x4 WorldToView { get; }
            public Matrix4x4 ViewToClip { get; }
        }

        [Serializable]
        private struct CachedNativeTexture
        {
            public Texture Texture;
            public IntPtr NativePtr;
        }

        [NonSerialized] private readonly Dictionary<int, CameraHistory> _historyByCameraId = new Dictionary<int, CameraHistory>();
        [NonSerialized] private RenderTexture _packedNormalRoughnessTexture;
        [NonSerialized] private RenderTexture _packedViewZTexture;
        [NonSerialized] private RenderTexture _packedMotionTexture;
        [NonSerialized] private RenderTexture _packedDiffuseHitDistanceTexture;
        [NonSerialized] private RenderTexture _denoisedAoTexture;
        [NonSerialized] private RenderTexture _outputTexture;
        [NonSerialized] private Material _guidePackMaterial;
        [NonSerialized] private int _lastWidth;
        [NonSerialized] private int _lastHeight;
        [NonSerialized] private bool _nativeBackendActive;
        [NonSerialized] private string _lastBridgeError = string.Empty;
        [NonSerialized] private CachedNativeTexture _cachedMotionTexture;
        [NonSerialized] private CachedNativeTexture _cachedNormalRoughnessTexture;
        [NonSerialized] private CachedNativeTexture _cachedViewZTexture;
        [NonSerialized] private CachedNativeTexture _cachedPackedDiffuseHitDistanceTexture;
        [NonSerialized] private CachedNativeTexture _cachedDenoisedAoTexture;

        [SerializeField] private bool _enabled = true;
        [SerializeField] private bool _strictNativeBackend;
        [SerializeField] private GraphicsFormat _guideNormalRoughnessFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _guideViewZFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _guideMotionFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _packedDiffuseHitDistanceFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _denoisedAoFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.None;
        [SerializeField, Min(1)] private int _maxAccumulatedFrameNum = 30;
        [SerializeField, Min(1)] private int _maxFastAccumulatedFrameNum = 6;
        [SerializeField, Min(1)] private int _historyFixFrameNum = 3;

        public RenderTexture OutputTexture => _outputTexture;
        public RenderTexture PackedDiffuseHitDistanceTexture => _packedDiffuseHitDistanceTexture;
        public RenderTexture DenoisedAoTexture => _denoisedAoTexture;
        public bool NativeBackendActive => _nativeBackendActive;
        public bool StrictNativeBackend => _strictNativeBackend;

        public bool RecordNormHitDistancePreview(
            CommandBuffer commandBuffer,
            GbufferCore gbufferCore,
            RtaoCore rtaoCore)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (gbufferCore == null || rtaoCore == null)
            {
                return false;
            }

            if (gbufferCore.ViewZTexture == null || rtaoCore.HitDistanceTexture == null)
            {
                return false;
            }

            int width = Mathf.Max(rtaoCore.HitDistanceTexture.width, 1);
            int height = Mathf.Max(rtaoCore.HitDistanceTexture.height, 1);
            EnsureNormHitDistanceTexture(width, height);
            if (!EnsureMaterials())
            {
                return false;
            }

            RecordNormHitDistancePacking(commandBuffer, gbufferCore, rtaoCore);
            commandBuffer.SetGlobalTexture(VoxelNrdIds.PackedDiffuseHitDistanceTextureId, _packedDiffuseHitDistanceTexture);
            commandBuffer.SetGlobalFloat(VoxelNrdIds.NativeBackendActiveId, 0.0f);
            return true;
        }

        public bool BindLatestDenoisedAoPreview(
            CommandBuffer commandBuffer,
            RtaoCore rtaoCore)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (rtaoCore == null || _denoisedAoTexture == null)
            {
                return false;
            }

            commandBuffer.SetGlobalTexture(VoxelRtaoIds.HitDistanceTextureId, rtaoCore.HitDistanceTexture);
            if (_packedDiffuseHitDistanceTexture != null)
            {
                commandBuffer.SetGlobalTexture(VoxelNrdIds.PackedDiffuseHitDistanceTextureId, _packedDiffuseHitDistanceTexture);
            }

            commandBuffer.SetGlobalTexture(VoxelNrdIds.DenoisedAoTextureId, _denoisedAoTexture);
            commandBuffer.SetGlobalTexture(VoxelRtaoIds.OutputTextureId, _denoisedAoTexture);
            commandBuffer.SetGlobalFloat(VoxelNrdIds.NativeBackendActiveId, _nativeBackendActive ? 1.0f : 0.0f);
            return true;
        }

        public bool QueueDeferredDenoise(
            CommandBuffer commandBuffer,
            Camera camera,
            GbufferCore gbufferCore,
            RtaoCore rtaoCore)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (gbufferCore == null || rtaoCore == null)
            {
                return false;
            }

            if (gbufferCore.NormalTexture == null ||
                gbufferCore.ViewZTexture == null ||
                gbufferCore.MotionTexture == null ||
                rtaoCore.HitDistanceTexture == null)
            {
                return false;
            }

            int width = Mathf.Max(rtaoCore.HitDistanceTexture.width, 1);
            int height = Mathf.Max(rtaoCore.HitDistanceTexture.height, 1);
            EnsureTextures(width, height);
            if (!EnsureMaterials())
            {
                return false;
            }

            RecordGuidePacking(commandBuffer, gbufferCore, rtaoCore);
            RefreshNativeBackendState();

            if (_enabled && TryRecordNrd(commandBuffer, camera, rtaoCore, width, height))
            {
                _nativeBackendActive = true;
                RememberHistory(camera);
                return true;
            }

            _nativeBackendActive = false;
            if (_enabled && _strictNativeBackend)
            {
                RecordStrictFailure(commandBuffer, rtaoCore);
                RememberHistory(camera);
                return true;
            }

            if (_packedDiffuseHitDistanceTexture != null && _denoisedAoTexture != null)
            {
                commandBuffer.Blit(_packedDiffuseHitDistanceTexture, _denoisedAoTexture);
            }

            if (_denoisedAoTexture != null && _outputTexture != null)
            {
                commandBuffer.Blit(_denoisedAoTexture, _outputTexture);
            }

            RememberHistory(camera);
            return true;
        }

        public bool Record(
            CommandBuffer commandBuffer,
            Camera camera,
            GbufferCore gbufferCore,
            RtaoCore rtaoCore)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (gbufferCore == null || rtaoCore == null)
            {
                return false;
            }

            if (gbufferCore.NormalTexture == null ||
                gbufferCore.ViewZTexture == null ||
                gbufferCore.MotionTexture == null ||
                rtaoCore.HitDistanceTexture == null)
            {
                return false;
            }

            int width = Mathf.Max(rtaoCore.HitDistanceTexture.width, 1);
            int height = Mathf.Max(rtaoCore.HitDistanceTexture.height, 1);
            EnsureTextures(width, height);
            if (!EnsureMaterials())
            {
                return false;
            }

            RecordGuidePacking(commandBuffer, gbufferCore, rtaoCore);

            RefreshNativeBackendState();
            Texture aoSource = _packedDiffuseHitDistanceTexture;
            bool queuedNativeDenoise = false;

            if (_enabled && TryRecordNrd(commandBuffer, camera, rtaoCore, width, height))
            {
                queuedNativeDenoise = true;
                aoSource = _denoisedAoTexture;
                _nativeBackendActive = true;
            }
            else if (_enabled && _strictNativeBackend)
            {
                _nativeBackendActive = false;
                RecordStrictFailure(commandBuffer, rtaoCore);
                commandBuffer.SetGlobalTexture(VoxelRtaoIds.HitDistanceTextureId, rtaoCore.HitDistanceTexture);
                commandBuffer.SetGlobalTexture(VoxelNrdIds.PackedDiffuseHitDistanceTextureId, _packedDiffuseHitDistanceTexture);
                commandBuffer.SetGlobalTexture(VoxelNrdIds.DenoisedAoTextureId, _denoisedAoTexture);
                commandBuffer.SetGlobalFloat(VoxelNrdIds.NativeBackendActiveId, 0.0f);
                RememberHistory(camera);
                return true;
            }

            commandBuffer.SetGlobalTexture(VoxelRtaoIds.HitDistanceTextureId, rtaoCore.HitDistanceTexture);
            commandBuffer.SetGlobalTexture(VoxelNrdIds.PackedDiffuseHitDistanceTextureId, _packedDiffuseHitDistanceTexture);
            commandBuffer.SetGlobalTexture(VoxelNrdIds.DenoisedAoTextureId, _denoisedAoTexture);
            commandBuffer.SetGlobalFloat(VoxelNrdIds.NativeBackendActiveId, (_nativeBackendActive || queuedNativeDenoise) ? 1.0f : 0.0f);

            RecordComposite(commandBuffer, aoSource, rtaoCore);
            commandBuffer.SetGlobalTexture(VoxelRtaoIds.OutputTextureId, _outputTexture);

            RememberHistory(camera);
            return true;
        }

        public void Dispose()
        {
            ReleasePersistentTexture(ref _packedNormalRoughnessTexture);
            ReleasePersistentTexture(ref _packedViewZTexture);
            ReleasePersistentTexture(ref _packedMotionTexture);
            ReleasePersistentTexture(ref _packedDiffuseHitDistanceTexture);
            ReleasePersistentTexture(ref _denoisedAoTexture);
            ReleasePersistentTexture(ref _outputTexture);
            DestroyMaterial(ref _guidePackMaterial);
            _historyByCameraId.Clear();
            _cachedMotionTexture = default;
            _cachedNormalRoughnessTexture = default;
            _cachedViewZTexture = default;
            _cachedPackedDiffuseHitDistanceTexture = default;
            _cachedDenoisedAoTexture = default;
            _nativeBackendActive = false;
            NrdBridge.Shutdown();
        }

        private void RecordGuidePacking(CommandBuffer commandBuffer, GbufferCore gbufferCore, RtaoCore rtaoCore)
        {
            float pixelStep = rtaoCore.ResolutionMode == RtaoResolutionMode.Half ? 2.0f : 1.0f;
            Vector3 hitDistanceParameters = ResolveDefaultHitDistanceParameters();

            commandBuffer.SetGlobalFloat(NrdPixelStepId, pixelStep);
            commandBuffer.SetGlobalFloat(NrdSecondaryPixelStepId, pixelStep);
            commandBuffer.SetGlobalFloat(NrdGuideModeId, 0.0f);
            commandBuffer.Blit(gbufferCore.NormalTexture, _packedNormalRoughnessTexture, _guidePackMaterial);
            commandBuffer.Blit(gbufferCore.ViewZTexture, _packedViewZTexture, _guidePackMaterial);
            commandBuffer.Blit(gbufferCore.MotionTexture, _packedMotionTexture, _guidePackMaterial);

            RecordNormHitDistancePacking(commandBuffer, gbufferCore, rtaoCore, hitDistanceParameters);
        }

        private bool TryRecordNrd(CommandBuffer commandBuffer, Camera camera, RtaoCore rtaoCore, int width, int height)
        {
            if (_packedNormalRoughnessTexture == null ||
                _packedViewZTexture == null ||
                _packedMotionTexture == null ||
                _packedDiffuseHitDistanceTexture == null ||
                _denoisedAoTexture == null)
            {
                return false;
            }

            if (!NrdBridge.TryEnsureInitialized(out string initializeError))
            {
                LogBridgeErrorOnce(initializeError, _strictNativeBackend);
                return false;
            }

            if (_lastWidth != width || _lastHeight != height)
            {
                NrdBridge.NotifyResize(width, height);
                _lastWidth = width;
                _lastHeight = height;
            }

            CameraHistory currentHistory = CreateCameraHistory(camera);
            CameraHistory previousHistory = TryGetPreviousHistory(camera, out CameraHistory storedHistory)
                ? storedHistory
                : currentHistory;
            Vector3 ambientHitDistanceParameters = ResolveDefaultHitDistanceParameters();

            NrdSettings settings = new NrdSettings
            {
                Width = width,
                Height = height,
                DenoisingRange = Mathf.Max(camera.farClipPlane, 1.0f),
                MaxAccumulatedFrameNum = Mathf.Max(_maxAccumulatedFrameNum, 1),
                MaxFastAccumulatedFrameNum = Mathf.Clamp(_maxFastAccumulatedFrameNum, 1, Mathf.Max(_maxAccumulatedFrameNum, 1)),
                HistoryFixFrameNum = Mathf.Clamp(_historyFixFrameNum, 1, Mathf.Max(_maxFastAccumulatedFrameNum, 1)),
                HitDistanceA = ambientHitDistanceParameters.x,
                HitDistanceB = ambientHitDistanceParameters.y,
                HitDistanceC = ambientHitDistanceParameters.z,
                EnableValidation = 0
            };

            NrdFrameData frameData = new NrdFrameData
            {
                NoisyAmbientNormalizedHitDistance = ResolveNativeTexturePtr(_packedDiffuseHitDistanceTexture, ref _cachedPackedDiffuseHitDistanceTexture),
                Motion = ResolveNativeTexturePtr(_packedMotionTexture, ref _cachedMotionTexture),
                NormalRoughness = ResolveNativeTexturePtr(_packedNormalRoughnessTexture, ref _cachedNormalRoughnessTexture),
                ViewZ = ResolveNativeTexturePtr(_packedViewZTexture, ref _cachedViewZTexture),
                DenoisedAmbientOutput = ResolveNativeTexturePtr(_denoisedAoTexture, ref _cachedDenoisedAoTexture),
                CameraId = camera.GetInstanceID(),
                Width = width,
                Height = height,
                FrameIndex = Time.frameCount,
                CurrentWorldToView = NrdMatrix4x4.FromUnityMatrix(currentHistory.WorldToView),
                PreviousWorldToView = NrdMatrix4x4.FromUnityMatrix(previousHistory.WorldToView),
                CurrentViewToClip = NrdMatrix4x4.FromUnityMatrix(currentHistory.ViewToClip),
                PreviousViewToClip = NrdMatrix4x4.FromUnityMatrix(previousHistory.ViewToClip)
            };

            if (frameData.NoisyAmbientNormalizedHitDistance == IntPtr.Zero ||
                frameData.Motion == IntPtr.Zero ||
                frameData.NormalRoughness == IntPtr.Zero ||
                frameData.ViewZ == IntPtr.Zero ||
                frameData.DenoisedAmbientOutput == IntPtr.Zero)
            {
                LogBridgeErrorOnce("NRD native texture pointers are not available yet.", _strictNativeBackend);
                return false;
            }

            if (!NrdBridge.TryQueueFrame(in settings, in frameData, out string queueError))
            {
                LogBridgeErrorOnce(queueError, _strictNativeBackend);
                return false;
            }

            if (!NrdBridge.TryGetRenderEventFunc(out IntPtr renderEventFunc, out string eventError))
            {
                LogBridgeErrorOnce(eventError, _strictNativeBackend);
                return false;
            }

            commandBuffer.IssuePluginEvent(renderEventFunc, NrdBridge.DenoiseEventId);
            return true;
        }

        private void RecordComposite(CommandBuffer commandBuffer, Texture aoSource, RtaoCore rtaoCore)
        {
            if (_denoisedAoTexture != null && !ReferenceEquals(aoSource, _denoisedAoTexture))
            {
                commandBuffer.Blit(aoSource, _denoisedAoTexture);
            }

            commandBuffer.Blit(aoSource, _outputTexture);
        }

        private void RecordStrictFailure(CommandBuffer commandBuffer, RtaoCore rtaoCore)
        {
            ClearTexture(commandBuffer, _denoisedAoTexture, Color.clear);
            ClearTexture(commandBuffer, _outputTexture, Color.clear);
            commandBuffer.SetGlobalTexture(VoxelRtaoIds.OutputTextureId, _outputTexture);
        }

        private bool EnsureMaterials()
        {
            if (_guidePackMaterial == null)
            {
                Shader guidePackShader = Shader.Find(GuidePackShaderName);
                if (guidePackShader == null)
                {
                    return false;
                }

                _guidePackMaterial = new Material(guidePackShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            return true;
        }

        private void EnsureTextures(int width, int height)
        {
            EnsurePersistentTexture(
                ref _packedNormalRoughnessTexture,
                width,
                height,
                ResolveGuideNormalRoughnessFormat(),
                "_VoxelEngineNrdPackedNormalRoughness");
            EnsurePersistentTexture(
                ref _packedViewZTexture,
                width,
                height,
                ResolveGuideViewZFormat(),
                "_VoxelEngineNrdPackedViewZ");
            EnsurePersistentTexture(
                ref _packedMotionTexture,
                width,
                height,
                ResolveGuideMotionFormat(),
                "_VoxelEngineNrdPackedMotion");
            EnsurePersistentTexture(
                ref _packedDiffuseHitDistanceTexture,
                width,
                height,
                ResolvePackedDiffuseHitDistanceFormat(),
                "_VoxelEngineNrdPackedDiffuseHitDistance");
            EnsurePersistentTexture(
                ref _denoisedAoTexture,
                width,
                height,
                ResolveDenoisedAoFormat(),
                "_VoxelEngineNrdDenoisedAo");
            EnsurePersistentTexture(
                ref _outputTexture,
                width,
                height,
                ResolveOutputFormat(),
                "_VoxelEngineRtao");
        }

        private void EnsureNormHitDistanceTexture(int width, int height)
        {
            EnsurePersistentTexture(
                ref _packedDiffuseHitDistanceTexture,
                width,
                height,
                ResolvePackedDiffuseHitDistanceFormat(),
                "_VoxelEngineNrdPackedDiffuseHitDistance");
        }

        private void RecordNormHitDistancePacking(CommandBuffer commandBuffer, GbufferCore gbufferCore, RtaoCore rtaoCore)
        {
            RecordNormHitDistancePacking(commandBuffer, gbufferCore, rtaoCore, ResolveDefaultHitDistanceParameters());
        }

        private void RecordNormHitDistancePacking(
            CommandBuffer commandBuffer,
            GbufferCore gbufferCore,
            RtaoCore rtaoCore,
            Vector3 hitDistanceParameters)
        {
            float pixelStep = rtaoCore.ResolutionMode == RtaoResolutionMode.Half ? 2.0f : 1.0f;
            commandBuffer.SetGlobalFloat(NrdPixelStepId, 1.0f);
            commandBuffer.SetGlobalFloat(NrdSecondaryPixelStepId, pixelStep);
            commandBuffer.SetGlobalFloat(NrdGuideModeId, 1.0f);
            commandBuffer.SetGlobalTexture(NrdSecondarySourceId, gbufferCore.ViewZTexture);
            commandBuffer.SetGlobalVector(NrdSecondarySourceTexelSizeId, ComputeTexelSize(gbufferCore.ViewZTexture));
            commandBuffer.SetGlobalVector(
                NrdHitDistanceParametersId,
                new Vector4(hitDistanceParameters.x, hitDistanceParameters.y, hitDistanceParameters.z, 0.0f));
            commandBuffer.Blit(rtaoCore.HitDistanceTexture, _packedDiffuseHitDistanceTexture, _guidePackMaterial);
            commandBuffer.SetGlobalFloat(NrdGuideModeId, 0.0f);
            commandBuffer.SetGlobalFloat(NrdSecondaryPixelStepId, 1.0f);
        }

        private static Vector3 ResolveDefaultHitDistanceParameters()
        {
            // Official NRD defaults from ThirdParty/NRD/Include/NRDSettings.h
            return DefaultReblurHitDistanceParameters;
        }

        private static Vector4 ComputeTexelSize(Texture texture)
        {
            if (texture == null)
            {
                return Vector4.zero;
            }

            float width = Mathf.Max(texture.width, 1);
            float height = Mathf.Max(texture.height, 1);
            return new Vector4(1.0f / width, 1.0f / height, width, height);
        }

        private GraphicsFormat ResolveGuideNormalRoughnessFormat()
        {
            return _guideNormalRoughnessFormat == GraphicsFormat.None
                ? GraphicsFormat.R8G8B8A8_UNorm
                : _guideNormalRoughnessFormat;
        }

        private GraphicsFormat ResolveGuideViewZFormat()
        {
            return _guideViewZFormat == GraphicsFormat.None
                ? GraphicsFormat.R16_SFloat
                : _guideViewZFormat;
        }

        private GraphicsFormat ResolveGuideMotionFormat()
        {
            return _guideMotionFormat == GraphicsFormat.None
                ? GraphicsFormat.R16G16B16A16_SFloat
                : _guideMotionFormat;
        }

        private GraphicsFormat ResolvePackedDiffuseHitDistanceFormat()
        {
            return _packedDiffuseHitDistanceFormat == GraphicsFormat.None
                ? GraphicsFormat.R8_UNorm
                : _packedDiffuseHitDistanceFormat;
        }

        private GraphicsFormat ResolveDenoisedAoFormat()
        {
            return _denoisedAoFormat == GraphicsFormat.None
                ? GraphicsFormat.R16_SFloat
                : _denoisedAoFormat;
        }

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.R16_SFloat
                : _outputFormat;
        }

        private static void EnsurePersistentTexture(
            ref RenderTexture renderTexture,
            int width,
            int height,
            GraphicsFormat graphicsFormat,
            string name)
        {
            bool needsRecreate =
                renderTexture == null ||
                renderTexture.width != width ||
                renderTexture.height != height ||
                renderTexture.graphicsFormat != graphicsFormat;

            if (!needsRecreate)
            {
                return;
            }

            ReleasePersistentTexture(ref renderTexture);
            renderTexture = new RenderTexture(new RenderTextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex2D,
                depthBufferBits = 0,
                msaaSamples = 1,
                graphicsFormat = graphicsFormat,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            })
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            renderTexture.Create();
        }

        private static void ReleasePersistentTexture(ref RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                return;
            }

            renderTexture.Release();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(renderTexture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            renderTexture = null;
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            material = null;
        }

        private bool TryGetPreviousHistory(Camera camera, out CameraHistory history)
        {
            if (camera != null && _historyByCameraId.TryGetValue(camera.GetInstanceID(), out history))
            {
                return true;
            }

            history = default;
            return false;
        }

        private void RememberHistory(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            _historyByCameraId[camera.GetInstanceID()] = CreateCameraHistory(camera);
        }

        private static CameraHistory CreateCameraHistory(Camera camera)
        {
            return new CameraHistory(
                camera.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
        }

        private static IntPtr ResolveNativeTexturePtr(Texture texture, ref CachedNativeTexture cache)
        {
            if (texture == null)
            {
                cache = default;
                return IntPtr.Zero;
            }

            if (!ReferenceEquals(cache.Texture, texture))
            {
                cache.Texture = texture;
                cache.NativePtr = texture.GetNativeTexturePtr();
            }

            return cache.NativePtr;
        }

        private void RefreshNativeBackendState()
        {
            if (!_enabled)
            {
                _nativeBackendActive = false;
                return;
            }

            if (NrdBridge.TryGetBackendActive(out bool isActive))
            {
                _nativeBackendActive = isActive;
                return;
            }

            _nativeBackendActive = false;
        }

        private static void ClearTexture(CommandBuffer commandBuffer, RenderTexture renderTexture, Color clearColor)
        {
            if (commandBuffer == null || renderTexture == null)
            {
                return;
            }

            commandBuffer.SetRenderTarget(renderTexture);
            commandBuffer.ClearRenderTarget(false, true, clearColor);
        }

        private void LogBridgeErrorOnce(string error, bool strict)
        {
            if (string.IsNullOrWhiteSpace(error) || string.Equals(_lastBridgeError, error, StringComparison.Ordinal))
            {
                return;
            }

            _lastBridgeError = error;
            if (strict)
            {
                Debug.LogError($"NRD strict mode failure: {error}");
                return;
            }

            Debug.LogWarning($"NRD fallback active: {error}");
        }
    }
}
