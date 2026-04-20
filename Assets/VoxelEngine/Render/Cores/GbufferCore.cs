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
        public static readonly int MotionTextureId = Shader.PropertyToID("_VoxelEngineGbufferMotion");

        public static RenderTargetIdentifier AlbedoTarget => new RenderTargetIdentifier(AlbedoTextureId);
        public static RenderTargetIdentifier NormalTarget => new RenderTargetIdentifier(NormalTextureId);
        public static RenderTargetIdentifier DepthTarget => new RenderTargetIdentifier(DepthTextureId);
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
        private static readonly int BackgroundColorId = Shader.PropertyToID("_BackgroundColor");
        private static readonly int CurrentWorldToClipId = Shader.PropertyToID("_CurrentWorldToClip");
        private static readonly int PreviousWorldToClipId = Shader.PropertyToID("_PreviousWorldToClip");
        private static readonly int RayTMinId = Shader.PropertyToID("_RayTMin");
        private static readonly int RayTMaxId = Shader.PropertyToID("_RayTMax");
        private static readonly int AllInstanceMaskId = Shader.PropertyToID("_AllInstanceMask");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int DebugAabbOverlayInstanceMaskId = Shader.PropertyToID("_DebugAabbOverlayInstanceMask");

        [NonSerialized] private readonly Dictionary<int, CameraHistory> _historyByCameraId = new Dictionary<int, CameraHistory>();

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField] private GraphicsFormat _albedoFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _normalFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _depthFormat = GraphicsFormat.None;
        [SerializeField] private GraphicsFormat _motionFormat = GraphicsFormat.None;

        public bool Record(
            CommandBuffer commandBuffer,
            Camera camera,
            VoxelEngineRenderBackend renderBackend)
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
                return false;
            }

            int width = Mathf.Max(camera.pixelWidth, 1);
            int height = Mathf.Max(camera.pixelHeight, 1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            Matrix4x4 currentWorldToClip = ComputeWorldToClipMatrix(camera);
            CameraHistory currentHistory = CreateCameraHistory(camera, currentWorldToClip);
            CameraHistory previousHistory = TryGetPreviousHistory(camera, out CameraHistory storedHistory)
                ? storedHistory
                : currentHistory;

            AllocateTemporaryTargets(commandBuffer, width, height);

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

                Vector3 cameraPosition = camera.transform.position;
                Vector3 cameraForward = camera.transform.forward;
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
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.AlbedoTextureId,
                    VoxelGbufferIds.AlbedoTarget);
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.NormalTextureId,
                    VoxelGbufferIds.NormalTarget);
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.DepthTextureId,
                    VoxelGbufferIds.DepthTarget);
                commandBuffer.SetRayTracingTextureParam(
                    _rayTracingShader,
                    VoxelGbufferIds.MotionTextureId,
                    VoxelGbufferIds.MotionTarget);
                commandBuffer.DispatchRays(
                    _rayTracingShader,
                    RayGenerationShaderName,
                    (uint)width,
                    (uint)height,
                    1u,
                    camera);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.AlbedoTextureId, VoxelGbufferIds.AlbedoTarget);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.NormalTextureId, VoxelGbufferIds.NormalTarget);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.DepthTextureId, VoxelGbufferIds.DepthTarget);
                commandBuffer.SetGlobalTexture(VoxelGbufferIds.MotionTextureId, VoxelGbufferIds.MotionTarget);
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
            commandBuffer.ReleaseTemporaryRT(VoxelGbufferIds.NormalTextureId);
            commandBuffer.ReleaseTemporaryRT(VoxelGbufferIds.DepthTextureId);
            commandBuffer.ReleaseTemporaryRT(VoxelGbufferIds.MotionTextureId);
        }

        private void AllocateTemporaryTargets(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                VoxelGbufferIds.AlbedoTextureId,
                CreateTextureDescriptor(width, height, ResolveAlbedoFormat()),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                VoxelGbufferIds.NormalTextureId,
                CreateTextureDescriptor(width, height, ResolveNormalFormat()),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                VoxelGbufferIds.DepthTextureId,
                CreateTextureDescriptor(width, height, ResolveDepthFormat()),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                VoxelGbufferIds.MotionTextureId,
                CreateTextureDescriptor(width, height, ResolveMotionFormat()),
                FilterMode.Point);
        }

        private string ResolveShaderPassName()
        {
            return string.IsNullOrWhiteSpace(_shaderPassName)
                ? DefaultShaderPassName
                : _shaderPassName;
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
                ? GraphicsFormat.R16_UNorm
                : _depthFormat;
        }

        private GraphicsFormat ResolveMotionFormat()
        {
            return _motionFormat == GraphicsFormat.None
                ? GraphicsFormat.R16G16B16A16_SFloat
                : _motionFormat;
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

        private static CameraHistory CreateCameraHistory(Camera camera, Matrix4x4 worldToClip)
        {
            Transform transform = camera.transform;
            return new CameraHistory(transform.position, transform.forward, worldToClip);
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
            public CameraHistory(Vector3 position, Vector3 forward, Matrix4x4 worldToClip)
            {
                Position = position;
                Forward = forward;
                WorldToClip = worldToClip;
            }

            public Vector3 Position { get; }

            public Vector3 Forward { get; }

            public Matrix4x4 WorldToClip { get; }
        }
    }
}
