using System;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RenderPipeline;
using VoxelRT.Runtime.Rendering.VoxelRuntime;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    [Serializable]
    public sealed class GenSunLightCore
    {
        private const string DefaultShaderPassName = "VoxelLightingSun";
        private const string RayGenerationShaderName = "RayGenMain";
        private const int OpaqueInstanceMask = 0x1;
        private const float DefaultNormalBias = 0.05f;
        private const float DefaultJitterRadius = 0.025f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int CameraForwardWsId = Shader.PropertyToID("_CameraForwardWS");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int SunFrameIndexId = Shader.PropertyToID("_SunFrameIndex");
        private static readonly int SunDirectionWsId = Shader.PropertyToID("_SunDirectionWS");
        private static readonly int SunShadowDistanceId = Shader.PropertyToID("_SunShadowDistance");
        private static readonly int SunNormalBiasId = Shader.PropertyToID("_SunNormalBias");
        private static readonly int SunJitterRadiusId = Shader.PropertyToID("_SunJitterRadius");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField] private Vector3 _fallbackSunDirection = new(0.4f, 1.0f, 0.2f);
        [SerializeField, Min(0.0f)] private float _shadowDistance;
        [SerializeField, Min(0.0f)] private float _normalBias = DefaultNormalBias;
        [SerializeField, Min(0.0f)] private float _jitterRadius = DefaultJitterRadius;
        public readonly struct RenderData
        {
            public RenderData(
                VoxelRuntime.VoxelRuntime runtime,
                Camera camera,
                int width,
                int height,
                Vector3 sunDirectionWs,
                float shadowDistance)
            {
                Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
                Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
                Width = width;
                Height = height;
                SunDirectionWs = sunDirectionWs;
                ShadowDistance = shadowDistance;
            }

            public VoxelRuntime.VoxelRuntime Runtime { get; }

            public Camera Camera { get; }

            public int Width { get; }

            public int Height { get; }

            public Vector3 SunDirectionWs { get; }

            public float ShadowDistance { get; }
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

            ResolveSun(camera, out Vector3 sunDirectionWs, out float shadowDistance);
            renderData = new RenderData(
                bootstrap.Runtime,
                camera,
                width,
                height,
                sunDirectionWs,
                shadowDistance);
            return true;
        }

        public void RecordSunLightFill(
            CommandBuffer commandBuffer,
            in RenderData renderData)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

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

            commandBuffer.SetRayTracingIntParam(_rayTracingShader, OpaqueInstanceMaskId, OpaqueInstanceMask);
            commandBuffer.SetRayTracingIntParam(_rayTracingShader, SunFrameIndexId, Time.frameCount);
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                SunDirectionWsId,
                new Vector4(renderData.SunDirectionWs.x, renderData.SunDirectionWs.y, renderData.SunDirectionWs.z, 0.0f));
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, SunShadowDistanceId, renderData.ShadowDistance);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, SunNormalBiasId, Mathf.Max(_normalBias, 0.0f));
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, SunJitterRadiusId, Mathf.Max(_jitterRadius, 0.0f));

            BindInputTexture(commandBuffer, VoxelRtGbufferTexture.Normal);
            BindInputTexture(commandBuffer, VoxelRtGbufferTexture.Depth);
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                VoxelRtSunLightIds.SunLightTextureId,
                VoxelRtSunLightIds.GetRenderTargetIdentifier());
            commandBuffer.DispatchRays(
                _rayTracingShader,
                RayGenerationShaderName,
                (uint)renderData.Width,
                (uint)renderData.Height,
                1u,
                renderData.Camera);

            commandBuffer.SetGlobalTexture(
                VoxelRtSunLightIds.SunLightTextureId,
                VoxelRtSunLightIds.GetRenderTargetIdentifier());
        }

        public void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }
        }

        private void BindInputTexture(CommandBuffer commandBuffer, VoxelRtGbufferTexture texture)
        {
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                VoxelRtGbufferIds.GetTextureId(texture),
                VoxelRtGbufferIds.GetRenderTargetIdentifier(texture));
        }

        private void ResolveSun(
            Camera camera,
            out Vector3 sunDirectionWs,
            out float shadowDistance)
        {
            Light sunLight = RenderSettings.sun;
            if (sunLight != null)
            {
                sunDirectionWs = NormalizeOrFallback(-sunLight.transform.forward, Vector3.up);
            }
            else
            {
                sunDirectionWs = NormalizeOrFallback(_fallbackSunDirection, Vector3.up);
            }

            float resolvedShadowDistance = _shadowDistance <= 0.0f
                ? camera.farClipPlane
                : _shadowDistance;
            shadowDistance = Mathf.Max(resolvedShadowDistance, 1e-4f);
        }

        private string ResolveShaderPassName()
        {
            return string.IsNullOrWhiteSpace(_shaderPassName)
                ? DefaultShaderPassName
                : _shaderPassName;
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 1e-8f
                ? value.normalized
                : fallback.normalized;
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
