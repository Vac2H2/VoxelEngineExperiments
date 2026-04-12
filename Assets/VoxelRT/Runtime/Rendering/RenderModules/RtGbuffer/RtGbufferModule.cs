using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RenderPipeline;
using VoxelRT.Runtime.Rendering.VoxelRuntime;

namespace VoxelRT.Runtime.Rendering.RenderModules
{
    [CreateAssetMenu(
        menuName = "VoxelRT/Rendering/Render Modules/RtGbuffer Module",
        fileName = "RtGbufferModule")]
    public sealed class RtGbufferModule : VoxelRenderPipelineModule
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

        private static readonly int GBuffer0TextureId = Shader.PropertyToID("_VoxelRtGBuffer0");
        private static readonly int GBuffer1TextureId = Shader.PropertyToID("_VoxelRtGBuffer1");
        private static readonly int GBuffer2TextureId = Shader.PropertyToID("_VoxelRtGBuffer2");
        private static readonly int SurfaceInfoTextureId = Shader.PropertyToID("_VoxelRtSurfaceInfo");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField] private PreviewTarget _previewTarget = PreviewTarget.GBuffer0Albedo;

        private enum PreviewTarget
        {
            GBuffer0Albedo = 0,
            GBuffer1Normal = 1,
            GBuffer2Depth = 2,
            SurfaceInfo = 3,
        }

        protected override bool OnRender(in VoxelRenderPipelineCameraContext context)
        {
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

            VoxelRuntime.VoxelRuntime runtime = bootstrap.Runtime;
            ScriptableRenderContext renderContext = context.RenderContext;
            CommandBuffer commandBuffer = new()
            {
                name = string.IsNullOrWhiteSpace(name) ? nameof(RtGbufferModule) : name
            };

            try
            {
                renderContext.SetupCameraProperties(camera);
                RecordGBufferFill(commandBuffer, runtime, camera, width, height);
                renderContext.ExecuteCommandBuffer(commandBuffer);
                commandBuffer.Clear();

                bool handledBySubmodule = RenderSubmodules(in context);
                if (!handledBySubmodule)
                {
                    commandBuffer.Blit(ResolvePreviewSource(), BuiltinRenderTextureType.CameraTarget);
                    renderContext.ExecuteCommandBuffer(commandBuffer);
                    commandBuffer.Clear();
                }

                ReleaseTemporaryTargets(commandBuffer);
                renderContext.ExecuteCommandBuffer(commandBuffer);
            }
            finally
            {
                commandBuffer.Clear();
                commandBuffer.Release();
            }

            renderContext.Submit();
            return true;
        }

        private void RecordGBufferFill(
            CommandBuffer commandBuffer,
            VoxelRuntime.VoxelRuntime runtime,
            Camera camera,
            int width,
            int height)
        {
            AllocateTemporaryTargets(commandBuffer, width, height);

            runtime.ResourceBinder.BindRayTracingShader(commandBuffer, _rayTracingShader);
            commandBuffer.SetRayTracingShaderPass(_rayTracingShader, ResolveShaderPassName());
            commandBuffer.SetRayTracingAccelerationStructure(
                _rayTracingShader,
                RayTracingAccelerationStructureId,
                runtime.RayTracingScene.AccelerationStructure);
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

            float rayTMin = MinimumRayT;
            float rayTMax = Mathf.Max(camera.farClipPlane, rayTMin);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMinId, rayTMin);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMaxId, rayTMax);
            commandBuffer.SetRayTracingIntParam(_rayTracingShader, AllInstanceMaskId, AllInstanceMask);
            commandBuffer.SetRayTracingIntParam(_rayTracingShader, OpaqueInstanceMaskId, OpaqueInstanceMask);
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                GBuffer0TextureId,
                new RenderTargetIdentifier(GBuffer0TextureId));
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                GBuffer1TextureId,
                new RenderTargetIdentifier(GBuffer1TextureId));
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                GBuffer2TextureId,
                new RenderTargetIdentifier(GBuffer2TextureId));
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                SurfaceInfoTextureId,
                new RenderTargetIdentifier(SurfaceInfoTextureId));
            commandBuffer.DispatchRays(
                _rayTracingShader,
                RayGenerationShaderName,
                (uint)width,
                (uint)height,
                1u,
                camera);

            commandBuffer.SetGlobalTexture(GBuffer0TextureId, new RenderTargetIdentifier(GBuffer0TextureId));
            commandBuffer.SetGlobalTexture(GBuffer1TextureId, new RenderTargetIdentifier(GBuffer1TextureId));
            commandBuffer.SetGlobalTexture(GBuffer2TextureId, new RenderTargetIdentifier(GBuffer2TextureId));
            commandBuffer.SetGlobalTexture(SurfaceInfoTextureId, new RenderTargetIdentifier(SurfaceInfoTextureId));
        }

        private void AllocateTemporaryTargets(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                GBuffer0TextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                GBuffer1TextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                GBuffer2TextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R16_SFloat),
                FilterMode.Point);
            commandBuffer.GetTemporaryRT(
                SurfaceInfoTextureId,
                CreateTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm),
                FilterMode.Point);
        }

        private void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            commandBuffer.ReleaseTemporaryRT(GBuffer0TextureId);
            commandBuffer.ReleaseTemporaryRT(GBuffer1TextureId);
            commandBuffer.ReleaseTemporaryRT(GBuffer2TextureId);
            commandBuffer.ReleaseTemporaryRT(SurfaceInfoTextureId);
        }

        private RenderTargetIdentifier ResolvePreviewSource()
        {
            return _previewTarget switch
            {
                PreviewTarget.GBuffer1Normal => new RenderTargetIdentifier(GBuffer1TextureId),
                PreviewTarget.GBuffer2Depth => new RenderTargetIdentifier(GBuffer2TextureId),
                PreviewTarget.SurfaceInfo => new RenderTargetIdentifier(SurfaceInfoTextureId),
                _ => new RenderTargetIdentifier(GBuffer0TextureId),
            };
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
