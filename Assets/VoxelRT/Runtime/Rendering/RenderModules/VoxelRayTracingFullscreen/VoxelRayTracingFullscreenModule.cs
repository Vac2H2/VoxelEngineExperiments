using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RenderPipeline;
using VoxelRT.Runtime.Rendering.VoxelRuntime;

namespace VoxelRT.Runtime.Rendering.RenderModules
{
    [CreateAssetMenu(
        menuName = "VoxelRT/Rendering/Render Modules/Fullscreen RayTracing Module",
        fileName = "VoxelRayTracingFullscreenModule")]
    public sealed class VoxelRayTracingFullscreenModule : VoxelRenderPipelineModule
    {
        private const string DefaultShaderPassName = "VoxelOccupancyDXR";
        private const string RayGenerationShaderName = "RayGenMain";
        private const float MinimumRayT = 0.001f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int OpaqueRayTracingAccelerationStructureId = Shader.PropertyToID("_OpaqueRaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int CameraForwardWsId = Shader.PropertyToID("_CameraForwardWS");
        private static readonly int RayTMinId = Shader.PropertyToID("_RayTMin");
        private static readonly int RayTMaxId = Shader.PropertyToID("_RayTMax");
        private static readonly int LayerMaskId = Shader.PropertyToID("_LayerMask");
        private static readonly int OutputTextureId = Shader.PropertyToID("_Output");
        private static readonly int TemporaryOutputTextureId = Shader.PropertyToID("_VoxelRayTracingFullscreenOutput");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField, Range(0, 255)] private int _instanceInclusionMask = 0xFF;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.R16G16B16A16_SFloat;

        protected override bool OnRender(in VoxelRenderPipelineCameraContext context)
        {
            if (RenderSubmodules(in context))
            {
                return true;
            }

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
                name = string.IsNullOrWhiteSpace(name) ? nameof(VoxelRayTracingFullscreenModule) : name
            };

            try
            {
                renderContext.SetupCameraProperties(camera);
                RecordRayTracingPass(commandBuffer, runtime, camera, width, height);
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

        private void RecordRayTracingPass(
            CommandBuffer commandBuffer,
            VoxelRuntime.VoxelRuntime runtime,
            Camera camera,
            int width,
            int height)
        {
            RenderTextureDescriptor outputDescriptor = CreateOutputDescriptor(width, height);
            commandBuffer.GetTemporaryRT(TemporaryOutputTextureId, outputDescriptor, FilterMode.Point);

            runtime.ResourceBinder.BindRayTracingShader(commandBuffer, _rayTracingShader);
            commandBuffer.SetRayTracingShaderPass(_rayTracingShader, ResolveShaderPassName());
            commandBuffer.SetRayTracingAccelerationStructure(
                _rayTracingShader,
                RayTracingAccelerationStructureId,
                runtime.RayTracingScene.AccelerationStructure);
            commandBuffer.SetRayTracingAccelerationStructure(
                _rayTracingShader,
                OpaqueRayTracingAccelerationStructureId,
                runtime.OpaqueRayTracingScene.AccelerationStructure);
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

            // For voxel procedural traversal we do not want the camera near clip
            // plane to slice chunk space before reaching the first occupied voxel.
            float rayTMin = MinimumRayT;
            float rayTMax = Mathf.Max(camera.farClipPlane, rayTMin);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMinId, rayTMin);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RayTMaxId, rayTMax);
            commandBuffer.SetRayTracingIntParam(
                _rayTracingShader,
                LayerMaskId,
                Mathf.Clamp(_instanceInclusionMask, 0, 0xFF));
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                OutputTextureId,
                new RenderTargetIdentifier(TemporaryOutputTextureId));
            commandBuffer.DispatchRays(
                _rayTracingShader,
                RayGenerationShaderName,
                (uint)width,
                (uint)height,
                1u,
                camera);
            commandBuffer.Blit(TemporaryOutputTextureId, BuiltinRenderTextureType.CameraTarget);
            commandBuffer.ReleaseTemporaryRT(TemporaryOutputTextureId);
        }

        private string ResolveShaderPassName()
        {
            return string.IsNullOrWhiteSpace(_shaderPassName)
                ? DefaultShaderPassName
                : _shaderPassName;
        }

        private RenderTextureDescriptor CreateOutputDescriptor(int width, int height)
        {
            RenderTextureDescriptor descriptor = new(width, height)
            {
                dimension = TextureDimension.Tex2D,
                depthBufferBits = 0,
                msaaSamples = 1,
                graphicsFormat = ResolveOutputFormat(),
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            return descriptor;
        }

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.R16G16B16A16_SFloat
                : _outputFormat;
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
