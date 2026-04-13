using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RenderPipeline;
using VoxelRT.Runtime.Rendering.VoxelRuntime;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    [Serializable]
    public sealed class GenGbufferCore
    {
        private const string DefaultShaderPassName = "VoxelOccupancyDXR";
        private const string RayGenerationShaderName = "RayGenMain";
        private const float MinimumRayT = 0.001f;
        private const int AllInstanceMask = 0x3;
        private const int OpaqueInstanceMask = 0x1;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int CameraForwardWsId = Shader.PropertyToID("_CameraForwardWS");
        private static readonly int RayTMinId = Shader.PropertyToID("_RayTMin");
        private static readonly int RayTMaxId = Shader.PropertyToID("_RayTMax");
        private static readonly int AllInstanceMaskId = Shader.PropertyToID("_AllInstanceMask");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;

        public readonly struct RenderData
        {
            public RenderData(
                VoxelRuntime.VoxelRuntime runtime,
                Camera camera,
                int width,
                int height)
            {
                Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
                Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
                Width = width;
                Height = height;
            }

            public VoxelRuntime.VoxelRuntime Runtime { get; }

            public Camera Camera { get; }

            public int Width { get; }

            public int Height { get; }
        }

        public bool TryCreateRenderData(
            in VoxelRenderPipelineCameraContext context,
            out RenderData renderData)
        {
            renderData = default;

            if (_rayTracingShader == null || !SystemInfo.supportsRayTracing)
            {
                return false;
            }

            Camera camera = context.Camera;
            int width = Mathf.Max(camera.pixelWidth, 1);
            int height = Mathf.Max(camera.pixelHeight, 1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            if (!VoxelRuntimeBootstrapResolver.TryResolve(camera, out VoxelRuntimeBootstrap bootstrap))
            {
                return false;
            }

            renderData = new RenderData(bootstrap.Runtime, camera, width, height);
            return true;
        }

        public void ApplyLegacyConfiguration(
            RayTracingShader rayTracingShader,
            string shaderPassName)
        {
            if (_rayTracingShader == null && rayTracingShader != null)
            {
                _rayTracingShader = rayTracingShader;
            }

            if (!string.IsNullOrWhiteSpace(shaderPassName) &&
                (string.IsNullOrWhiteSpace(_shaderPassName) || _shaderPassName == DefaultShaderPassName))
            {
                _shaderPassName = shaderPassName;
            }
        }

        public void RecordGBufferFill(
            CommandBuffer commandBuffer,
            in RenderData renderData)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            AllocateTemporaryTargets(commandBuffer, renderData.Width, renderData.Height);

            renderData.Runtime.ResourceBinder.BindRayTracingShader(commandBuffer, _rayTracingShader);
            commandBuffer.SetRayTracingShaderPass(_rayTracingShader, ResolveShaderPassName());
            commandBuffer.SetRayTracingAccelerationStructure(
                _rayTracingShader,
                RayTracingAccelerationStructureId,
                renderData.Runtime.RayTracingScene.AccelerationStructure);
            commandBuffer.SetRayTracingMatrixParam(
                _rayTracingShader,
                PixelCoordToViewDirWsId,
                ComputePixelCoordToWorldSpaceViewDirectionMatrix(
                    renderData.Camera,
                    renderData.Width,
                    renderData.Height));

            Vector3 cameraPosition = renderData.Camera.transform.position;
            Vector3 cameraForward = renderData.Camera.transform.forward;
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                CameraPositionWsId,
                new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0.0f));
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                CameraForwardWsId,
                new Vector4(cameraForward.x, cameraForward.y, cameraForward.z, 0.0f));

            float rayTMin = MinimumRayT;
            float rayTMax = Mathf.Max(renderData.Camera.farClipPlane, rayTMin);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMinId, rayTMin);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMaxId, rayTMax);
            commandBuffer.SetRayTracingIntParam(_rayTracingShader, AllInstanceMaskId, AllInstanceMask);
            commandBuffer.SetRayTracingIntParam(_rayTracingShader, OpaqueInstanceMaskId, OpaqueInstanceMask);
            BindOutputTexture(commandBuffer, VoxelRtGbufferTexture.Albedo);
            BindOutputTexture(commandBuffer, VoxelRtGbufferTexture.Normal);
            BindOutputTexture(commandBuffer, VoxelRtGbufferTexture.Depth);
            BindOutputTexture(commandBuffer, VoxelRtGbufferTexture.SurfaceInfo);
            commandBuffer.DispatchRays(
                _rayTracingShader,
                RayGenerationShaderName,
                (uint)renderData.Width,
                (uint)renderData.Height,
                1u,
                renderData.Camera);

            ExposeGlobalTexture(commandBuffer, VoxelRtGbufferTexture.Albedo);
            ExposeGlobalTexture(commandBuffer, VoxelRtGbufferTexture.Normal);
            ExposeGlobalTexture(commandBuffer, VoxelRtGbufferTexture.Depth);
            ExposeGlobalTexture(commandBuffer, VoxelRtGbufferTexture.SurfaceInfo);
        }

        public void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            commandBuffer.ReleaseTemporaryRT(VoxelRtGbufferIds.AlbedoTextureId);
            commandBuffer.ReleaseTemporaryRT(VoxelRtGbufferIds.NormalTextureId);
            commandBuffer.ReleaseTemporaryRT(VoxelRtGbufferIds.DepthTextureId);
            commandBuffer.ReleaseTemporaryRT(VoxelRtGbufferIds.SurfaceInfoTextureId);
        }

        private void AllocateTemporaryTargets(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                VoxelRtGbufferIds.AlbedoTextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                VoxelRtGbufferIds.NormalTextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                VoxelRtGbufferIds.DepthTextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R16_SFloat),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                VoxelRtGbufferIds.SurfaceInfoTextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm),
                FilterMode.Point);
        }

        private void BindOutputTexture(CommandBuffer commandBuffer, VoxelRtGbufferTexture texture)
        {
            int textureId = VoxelRtGbufferIds.GetTextureId(texture);
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                textureId,
                new RenderTargetIdentifier(textureId));
        }

        private static void ExposeGlobalTexture(CommandBuffer commandBuffer, VoxelRtGbufferTexture texture)
        {
            int textureId = VoxelRtGbufferIds.GetTextureId(texture);
            commandBuffer.SetGlobalTexture(textureId, new RenderTargetIdentifier(textureId));
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

        private static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera camera, int width, int height)
        {
            Vector4 screenSize = new(width, height, 1.0f / width, 1.0f / height);
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
    }
}
