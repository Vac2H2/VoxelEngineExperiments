using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelEngine.LifeCycle.Manager;
using VoxelEngine.Render.RenderBackend;

namespace VoxelEngine.Render.Cores
{
    public static class VoxelGbufferIds
    {
        public static readonly int AlbedoTextureId = Shader.PropertyToID("_VoxelEngineGbufferAlbedo");
        public static readonly int NormalTextureId = Shader.PropertyToID("_VoxelEngineGbufferNormal");
        public static readonly int DepthTextureId = Shader.PropertyToID("_VoxelEngineGbufferDepth");
        public static readonly int ViewZTextureId = Shader.PropertyToID("_VoxelEngineGbufferViewZ");
        public static readonly int MotionTextureId = Shader.PropertyToID("_VoxelEngineGbufferMotion");
        public static readonly int CameraFarClipId = Shader.PropertyToID("_VoxelEngineCameraFarClip");
        public static readonly int GlobalRoughnessId = Shader.PropertyToID("_VoxelEngineGlobalRoughness");
        public static readonly int PuddleEnabledId = Shader.PropertyToID("_VoxelEnginePuddleEnabled");
        public static readonly int PuddleCoverageId = Shader.PropertyToID("_VoxelEnginePuddleCoverage");
        public static readonly int PuddleScaleId = Shader.PropertyToID("_VoxelEnginePuddleScale");
        public static readonly int PuddleRoughnessId = Shader.PropertyToID("_VoxelEnginePuddleRoughness");
        public static readonly int PuddleDarkeningId = Shader.PropertyToID("_VoxelEnginePuddleDarkening");
        public static readonly int PuddleMinNormalYId = Shader.PropertyToID("_VoxelEnginePuddleMinNormalY");

        public static RenderTargetIdentifier AlbedoTarget => new RenderTargetIdentifier(AlbedoTextureId);
        public static RenderTargetIdentifier NormalTarget => new RenderTargetIdentifier(NormalTextureId);
        public static RenderTargetIdentifier DepthTarget => new RenderTargetIdentifier(DepthTextureId);
        public static RenderTargetIdentifier ViewZTarget => new RenderTargetIdentifier(ViewZTextureId);
        public static RenderTargetIdentifier MotionTarget => new RenderTargetIdentifier(MotionTextureId);
    }

    [Serializable]
    public sealed class GbufferCore
    {
        private const string DefaultShaderPassName = "VoxelProceduralDXR";
        private const string RayGenerationShaderName = "RayGenMain";
        private const float MinimumRayT = 0.001f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int CameraForwardWsId = Shader.PropertyToID("_CameraForwardWS");
        private static readonly int PreviousCameraPositionWsId = Shader.PropertyToID("_PreviousCameraPositionWS");
        private static readonly int PreviousCameraForwardWsId = Shader.PropertyToID("_PreviousCameraForwardWS");
        private static readonly int ProjectionJitterId = Shader.PropertyToID("_VoxelEngineProjectionJitter");
        private static readonly int PreviousProjectionJitterId = Shader.PropertyToID("_VoxelEnginePreviousProjectionJitter");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_VoxelEngineGbufferScreenSize");
        private static readonly int BackgroundColorId = Shader.PropertyToID("_BackgroundColor");
        private static readonly int CurrentWorldToClipId = Shader.PropertyToID("_CurrentWorldToClip");
        private static readonly int PreviousWorldToClipId = Shader.PropertyToID("_PreviousWorldToClip");
        private static readonly int RayTMinId = Shader.PropertyToID("_RayTMin");
        private static readonly int RayTMaxId = Shader.PropertyToID("_RayTMax");
        private static readonly int AllInstanceMaskId = Shader.PropertyToID("_AllInstanceMask");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int DebugAabbOverlayInstanceMaskId = Shader.PropertyToID("_DebugAabbOverlayInstanceMask");
        [NonSerialized] private readonly Dictionary<int, CameraHistory> _historyByCameraId = new Dictionary<int, CameraHistory>();
        [NonSerialized] private RenderTexture _normalTexture;
        [NonSerialized] private RenderTexture _viewZTexture;
        [NonSerialized] private RenderTexture _motionTexture;
        [NonSerialized] private bool _loggedUnavailableReason;

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField] private GraphicsFormat _albedoFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _normalFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _depthFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _viewZFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _motionFormat = GraphicsFormat.None;
        [SerializeField, Range(0.0f, 1.0f)] private float _globalRoughness = 0.55f;
        [SerializeField] private bool _puddlesEnabled = true;
        [SerializeField, Range(0.0f, 1.0f)] private float _puddleCoverage = 0.35f;
        [SerializeField, Min(0.001f)] private float _puddleScale = 0.055f;
        [SerializeField, Range(0.0f, 1.0f)] private float _puddleRoughness = 0.06f;
        [SerializeField, Range(0.0f, 1.0f)] private float _puddleDarkening = 0.35f;
        [SerializeField, Range(0.0f, 1.0f)] private float _puddleMinNormalY = 0.78f;

        public RenderTexture NormalTexture => _normalTexture;
        public RenderTexture ViewZTexture => _viewZTexture;
        public RenderTexture MotionTexture => _motionTexture;

        public float GlobalRoughness
        {
            get => _globalRoughness;
            set => _globalRoughness = Mathf.Clamp01(value);
        }

        public bool PuddlesEnabled
        {
            get => _puddlesEnabled;
            set => _puddlesEnabled = value;
        }

        public float PuddleCoverage
        {
            get => _puddleCoverage;
            set => _puddleCoverage = Mathf.Clamp01(value);
        }

        public float PuddleScale
        {
            get => _puddleScale;
            set => _puddleScale = Mathf.Max(value, 0.001f);
        }

        public float PuddleRoughness
        {
            get => _puddleRoughness;
            set => _puddleRoughness = Mathf.Clamp01(value);
        }

        public float PuddleDarkening
        {
            get => _puddleDarkening;
            set => _puddleDarkening = Mathf.Clamp01(value);
        }

        public float PuddleMinNormalY
        {
            get => _puddleMinNormalY;
            set => _puddleMinNormalY = Mathf.Clamp01(value);
        }

        public bool Record(
            CommandBuffer commandBuffer,
            Camera camera,
            VoxelEngineRenderBackend renderBackend,
            Vector2 projectionJitter = default)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (renderBackend == null)
            {
                throw new ArgumentNullException(nameof(renderBackend));
            }

            if (_rayTracingShader == null || !SystemInfo.supportsRayTracing || !renderBackend.HasInstances)
            {
                LogUnavailableReason(renderBackend);
                return false;
            }

            int width = Mathf.Max(camera.pixelWidth, 1);
            int height = Mathf.Max(camera.pixelHeight, 1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            Matrix4x4 currentWorldToClip = ComputeWorldToClipMatrix(camera);
            CameraHistory currentHistory = CreateCameraHistory(camera, currentWorldToClip, projectionJitter);
            CameraHistory previousHistory = TryGetPreviousHistory(camera, out CameraHistory storedHistory)
                ? storedHistory
                : currentHistory;

            AllocateFrameTargets(commandBuffer, width, height);

            try
            {
                if (renderBackend.RtasManager.HasPendingBuild)
                {
                    renderBackend.RtasManager.Build(commandBuffer);
                }

                commandBuffer.SetRayTracingShaderPass(_rayTracingShader, ResolveShaderPassName());
                commandBuffer.SetRayTracingAccelerationStructure(
                    _rayTracingShader,
                    RayTracingAccelerationStructureId,
                    renderBackend.RtasManager.AccelerationStructure);
                commandBuffer.SetRayTracingMatrixParam(
                    _rayTracingShader,
                    PixelCoordToViewDirWsId,
                    ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, width, height));

                VoxelCameraState cameraState = VoxelCameraState.FromCamera(camera);
                Vector3 cameraPosition = cameraState.Position;
                Vector3 cameraForward = cameraState.Forward;
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    CameraPositionWsId,
                    new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0.0f));
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    CameraForwardWsId,
                    new Vector4(cameraForward.x, cameraForward.y, cameraForward.z, 0.0f));
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    PreviousCameraPositionWsId,
                    new Vector4(
                        previousHistory.Position.x,
                        previousHistory.Position.y,
                        previousHistory.Position.z,
                        0.0f));
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    PreviousCameraForwardWsId,
                    new Vector4(
                        previousHistory.Forward.x,
                        previousHistory.Forward.y,
                        previousHistory.Forward.z,
                        0.0f));
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    ProjectionJitterId,
                    new Vector4(projectionJitter.x, projectionJitter.y, 0.0f, 0.0f));
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    PreviousProjectionJitterId,
                    new Vector4(
                        previousHistory.ProjectionJitter.x,
                        previousHistory.ProjectionJitter.y,
                        0.0f,
                        0.0f));
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    ScreenSizeId,
                    new Vector4(width, height, 1.0f / width, 1.0f / height));
                commandBuffer.SetRayTracingMatrixParam(
                    _rayTracingShader,
                    CurrentWorldToClipId,
                    currentHistory.WorldToClip);
                commandBuffer.SetRayTracingMatrixParam(
                    _rayTracingShader,
                    PreviousWorldToClipId,
                    previousHistory.WorldToClip);

                Color backgroundColor = camera.backgroundColor.linear;
                commandBuffer.SetRayTracingVectorParam(
                    _rayTracingShader,
                    BackgroundColorId,
                    new Vector4(backgroundColor.r, backgroundColor.g, backgroundColor.b, backgroundColor.a));

                float rayTMin = MinimumRayT;
                float rayTMax = Mathf.Max(camera.farClipPlane, rayTMin);
                commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMinId, rayTMin);
                commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMaxId, rayTMax);
                commandBuffer.SetRayTracingIntParam(
                    _rayTracingShader,
                    AllInstanceMaskId,
                    unchecked((int)VoxelRtasManager.AllInstanceMask));
                commandBuffer.SetRayTracingIntParam(
                    _rayTracingShader,
                    OpaqueInstanceMaskId,
                    unchecked((int)VoxelRtasManager.OpaqueInstanceMask));
                commandBuffer.SetRayTracingIntParam(
                    _rayTracingShader,
                    DebugAabbOverlayInstanceMaskId,
                    unchecked((int)VoxelRtasManager.DebugAabbOverlayInstanceMask));
                commandBuffer.SetRayTracingFloatParam(
                    _rayTracingShader,
                    VoxelGbufferIds.GlobalRoughnessId,
                    Mathf.Clamp01(_globalRoughness));
                commandBuffer.SetRayTracingIntParam(
                    _rayTracingShader,
                    VoxelGbufferIds.PuddleEnabledId,
                    _puddlesEnabled ? 1 : 0);
                commandBuffer.SetRayTracingFloatParam(
                    _rayTracingShader,
                    VoxelGbufferIds.PuddleCoverageId,
                    Mathf.Clamp01(_puddleCoverage));
                commandBuffer.SetRayTracingFloatParam(
                    _rayTracingShader,
                    VoxelGbufferIds.PuddleScaleId,
                    Mathf.Max(_puddleScale, 0.001f));
                commandBuffer.SetRayTracingFloatParam(
                    _rayTracingShader,
                    VoxelGbufferIds.PuddleRoughnessId,
                    Mathf.Clamp01(_puddleRoughness));
                commandBuffer.SetRayTracingFloatParam(
                    _rayTracingShader,
                    VoxelGbufferIds.PuddleDarkeningId,
                    Mathf.Clamp01(_puddleDarkening));
                commandBuffer.SetRayTracingFloatParam(
                    _rayTracingShader,
                    VoxelGbufferIds.PuddleMinNormalYId,
                    Mathf.Clamp01(_puddleMinNormalY));
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.AlbedoTextureId,
                    VoxelGbufferIds.AlbedoTarget);
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.NormalTextureId,
                    _normalTexture);
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.DepthTextureId,
                    VoxelGbufferIds.DepthTarget);
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.ViewZTextureId,
                    _viewZTexture);
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.MotionTextureId,
                    _motionTexture);
                commandBuffer.DispatchRays(
                    _rayTracingShader,
                    RayGenerationShaderName,
                    (uint)width,
                    (uint)height,
                    1u,
                    camera);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.AlbedoTextureId, VoxelGbufferIds.AlbedoTarget);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.NormalTextureId, _normalTexture);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.DepthTextureId, VoxelGbufferIds.DepthTarget);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.ViewZTextureId, _viewZTexture);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.MotionTextureId, _motionTexture);
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.CameraFarClipId, rayTMax);
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.GlobalRoughnessId, Mathf.Clamp01(_globalRoughness));
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.PuddleEnabledId, _puddlesEnabled ? 1.0f : 0.0f);
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.PuddleCoverageId, Mathf.Clamp01(_puddleCoverage));
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.PuddleScaleId, Mathf.Max(_puddleScale, 0.001f));
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.PuddleRoughnessId, Mathf.Clamp01(_puddleRoughness));
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.PuddleDarkeningId, Mathf.Clamp01(_puddleDarkening));
                commandBuffer.SetGlobalFloat(VoxelGbufferIds.PuddleMinNormalYId, Mathf.Clamp01(_puddleMinNormalY));
                RememberHistory(camera, currentHistory);
                return true;
            }
            catch
            {
                ReleaseTemporaryTargets(commandBuffer);
                throw;
            }
        }

        public void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            commandBuffer.ReleaseTemporaryRT(VoxelGbufferIds.AlbedoTextureId);
            commandBuffer.ReleaseTemporaryRT(VoxelGbufferIds.DepthTextureId);
        }

        public void Dispose()
        {
            ReleasePersistentTexture(ref _normalTexture);
            ReleasePersistentTexture(ref _viewZTexture);
            ReleasePersistentTexture(ref _motionTexture);
        }

        private void AllocateFrameTargets(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                VoxelGbufferIds.AlbedoTextureId,
                CreateTextureDescriptor(width, height, ResolveAlbedoFormat()),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                VoxelGbufferIds.DepthTextureId,
                CreateTextureDescriptor(width, height, ResolveDepthFormat()),
                FilterMode.Point);
            EnsurePersistentTexture(
                ref _normalTexture,
                width,
                height,
                ResolveNormalFormat(),
                "_VoxelEngineGbufferNormal");
            EnsurePersistentTexture(
                ref _viewZTexture,
                width,
                height,
                ResolveViewZFormat(),
                "_VoxelEngineGbufferViewZ");
            EnsurePersistentTexture(
                ref _motionTexture,
                width,
                height,
                ResolveMotionFormat(),
                "_VoxelEngineGbufferMotion");
        }

        private string ResolveShaderPassName()
        {
            return string.IsNullOrWhiteSpace(_shaderPassName)
                ? DefaultShaderPassName
                : _shaderPassName;
        }

        private void LogUnavailableReason(VoxelEngineRenderBackend renderBackend)
        {
            if (_loggedUnavailableReason)
            {
                return;
            }

            _loggedUnavailableReason = true;
            string reason = _rayTracingShader == null
                ? "G-buffer ray tracing shader is not assigned."
                : !SystemInfo.supportsRayTracing
                    ? $"SystemInfo.supportsRayTracing is false. GraphicsDeviceType={SystemInfo.graphicsDeviceType}."
                    : renderBackend != null && !renderBackend.HasInstances
                        ? "RTAS has no registered voxel instances."
                        : "Unknown unavailable condition.";

            Debug.LogWarning($"[{nameof(GbufferCore)}] Skipping voxel gbuffer. {reason}");
        }

        private static RenderTextureDescriptor CreateTextureDescriptor(
            int width,
            int height,
            GraphicsFormat graphicsFormat)
        {
            return new RenderTextureDescriptor(width, height)
            {
                dimension = TextureDimension.Tex2D,
                depthBufferBits = 0,
                msaaSamples = 1,
                graphicsFormat = graphicsFormat,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
        }

        private GraphicsFormat ResolveAlbedoFormat()
        {
            return _albedoFormat == GraphicsFormat.None
                ? GraphicsFormat.R16G16B16A16_SFloat
                : _albedoFormat;
        }

        private GraphicsFormat ResolveNormalFormat()
        {
            return _normalFormat == GraphicsFormat.None
                ? GraphicsFormat.R8G8B8A8_UNorm
                : _normalFormat;
        }

        private GraphicsFormat ResolveDepthFormat()
        {
            return _depthFormat == GraphicsFormat.None
                ? GraphicsFormat.R16_SFloat
                : _depthFormat;
        }

        private GraphicsFormat ResolveViewZFormat()
        {
            return _viewZFormat == GraphicsFormat.None
                ? GraphicsFormat.R16_SFloat
                : _viewZFormat;
        }

        private GraphicsFormat ResolveMotionFormat()
        {
            return _motionFormat == GraphicsFormat.None
                ? GraphicsFormat.R16G16B16A16_SFloat
                : _motionFormat;
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
            renderTexture = new RenderTexture(CreateTextureDescriptor(width, height, graphicsFormat))
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

        private bool TryGetPreviousHistory(Camera camera, out CameraHistory history)
        {
            if (camera != null && _historyByCameraId.TryGetValue(camera.GetInstanceID(), out history))
            {
                return true;
            }

            history = default;
            return false;
        }

        private void RememberHistory(Camera camera, CameraHistory history)
        {
            if (camera == null)
            {
                return;
            }

            _historyByCameraId[camera.GetInstanceID()] = history;
        }

        private static CameraHistory CreateCameraHistory(
            Camera camera,
            Matrix4x4 worldToClip,
            Vector2 projectionJitter)
        {
            VoxelCameraState cameraState = VoxelCameraState.FromCamera(camera);
            return new CameraHistory(cameraState.Position, cameraState.Forward, worldToClip, projectionJitter);
        }

        private static Matrix4x4 ComputeWorldToClipMatrix(Camera camera)
        {
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            return projectionMatrix * camera.worldToCameraMatrix;
        }

        private static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera camera, int width, int height)
        {
            Vector4 screenSize = new Vector4(width, height, 1.0f / width, 1.0f / height);
            Matrix4x4 viewSpaceRasterTransform;

            if (camera.orthographic)
            {
                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(-2.0f * screenSize.z, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, -2.0f * screenSize.w, 0.0f, 0.0f),
                    new Vector4(1.0f, 1.0f, -1.0f, 0.0f),
                    Vector4.zero);
            }
            else
            {
                float aspectRatio = width / (float)height;
                float tanHalfVerticalFov = Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad);
                Vector2 lensShift = camera.usePhysicalProperties ? camera.lensShift : Vector2.zero;

                float m21 = (1.0f - 2.0f * lensShift.y) * tanHalfVerticalFov;
                float m11 = -2.0f * screenSize.w * tanHalfVerticalFov;
                float m20 = (1.0f - 2.0f * lensShift.x) * tanHalfVerticalFov * aspectRatio;
                float m00 = -2.0f * screenSize.z * tanHalfVerticalFov * aspectRatio;

                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(m00, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, m11, 0.0f, 0.0f),
                    new Vector4(m20, m21, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            }

            Matrix4x4 worldToViewMatrix = camera.worldToCameraMatrix;
            worldToViewMatrix.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            worldToViewMatrix.SetRow(2, -worldToViewMatrix.GetRow(2));
            return Matrix4x4.Transpose(worldToViewMatrix.transpose * viewSpaceRasterTransform);
        }

        private readonly struct CameraHistory
        {
            public CameraHistory(
                Vector3 position,
                Vector3 forward,
                Matrix4x4 worldToClip,
                Vector2 projectionJitter)
            {
                Position = position;
                Forward = forward;
                WorldToClip = worldToClip;
                ProjectionJitter = projectionJitter;
            }

            public Vector3 Position { get; }

            public Vector3 Forward { get; }

            public Matrix4x4 WorldToClip { get; }

            public Vector2 ProjectionJitter { get; }
        }
    }
}
