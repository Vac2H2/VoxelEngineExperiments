using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelExperiments.Runtime.Rendering.Lighting;
using VoxelExperiments.Runtime.Rendering.RenderPipeline;
using VoxelExperiments.Runtime.Rendering.VoxelRuntime;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
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
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunShadowDistanceId = Shader.PropertyToID("_SunShadowDistance");
        private static readonly int SunNormalBiasId = Shader.PropertyToID("_SunNormalBias");
        private static readonly int SunJitterRadiusId = Shader.PropertyToID("_SunJitterRadius");
        private static readonly int LightingBlueNoiseTexId = Shader.PropertyToID("_LightingBlueNoiseTex");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private RayTracingShader _blueNoiseRayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField] private Vector3 _fallbackSunDirection = new(0.4f, 1.0f, 0.2f);
        [SerializeField] private Color _fallbackSunColor = Color.white;
        [SerializeField, Min(0.0f)] private float _shadowDistance;
        [SerializeField, Min(0.0f)] private float _normalBias = DefaultNormalBias;
        [SerializeField, Min(0.0f)] private float _jitterRadius = DefaultJitterRadius;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.B10G11R11_UFloatPack32;

        [NonSerialized] private LightingSamplingPattern _samplingPattern;
        [NonSerialized] private Texture _blueNoiseTexture;
        public readonly struct RenderData
        {
            public RenderData(
                VoxelRuntime.VoxelRuntime runtime,
                Camera camera,
                int width,
                int height,
                Vector3 sunDirectionWs,
                Vector3 sunColor,
                float shadowDistance)
            {
                Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
                Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
                Width = width;
                Height = height;
                SunDirectionWs = sunDirectionWs;
                SunColor = sunColor;
                ShadowDistance = shadowDistance;
            }

            public VoxelRuntime.VoxelRuntime Runtime { get; }

            public Camera Camera { get; }

            public int Width { get; }

            public int Height { get; }

            public Vector3 SunDirectionWs { get; }

            public Vector3 SunColor { get; }

            public float ShadowDistance { get; }
        }

        public bool TryCreateRenderData(
            in VoxelRenderPipelineCameraContext context,
            out RenderData renderData)
        {
            renderData = default;

            if (ResolveRayTracingShader() == null || !SystemInfo.supportsRayTracing)
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

            ResolveSun(camera, out Vector3 sunDirectionWs, out Vector3 sunColor, out float shadowDistance);
            renderData = new RenderData(
                bootstrap.Runtime,
                camera,
                width,
                height,
                sunDirectionWs,
                sunColor,
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

            RayTracingShader rayTracingShader = ResolveRayTracingShader();
            AllocateTemporaryTargets(commandBuffer, renderData.Width, renderData.Height);

            renderData.Runtime.ResourceBinder.BindRayTracingShader(commandBuffer, rayTracingShader);
            commandBuffer.SetRayTracingShaderPass(rayTracingShader, ResolveShaderPassName());
            commandBuffer.SetRayTracingAccelerationStructure(
                rayTracingShader,
                RayTracingAccelerationStructureId,
                renderData.Runtime.RayTracingScene.AccelerationStructure);
            commandBuffer.SetRayTracingMatrixParam(
                rayTracingShader,
                PixelCoordToViewDirWsId,
                ComputePixelCoordToWorldSpaceViewDirectionMatrix(
                    renderData.Camera,
                    renderData.Width,
                    renderData.Height));

            Vector3 cameraPosition = renderData.Camera.transform.position;
            Vector3 cameraForward = renderData.Camera.transform.forward;
            commandBuffer.SetRayTracingVectorParam(
                rayTracingShader,
                CameraPositionWsId,
                new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0.0f));
            commandBuffer.SetRayTracingVectorParam(
                rayTracingShader,
                CameraForwardWsId,
                new Vector4(cameraForward.x, cameraForward.y, cameraForward.z, 0.0f));

            commandBuffer.SetRayTracingIntParam(rayTracingShader, OpaqueInstanceMaskId, OpaqueInstanceMask);
            commandBuffer.SetRayTracingIntParam(rayTracingShader, SunFrameIndexId, Time.frameCount);
            commandBuffer.SetRayTracingVectorParam(
                rayTracingShader,
                SunDirectionWsId,
                new Vector4(renderData.SunDirectionWs.x, renderData.SunDirectionWs.y, renderData.SunDirectionWs.z, 0.0f));
            commandBuffer.SetRayTracingVectorParam(
                rayTracingShader,
                SunColorId,
                new Vector4(renderData.SunColor.x, renderData.SunColor.y, renderData.SunColor.z, 0.0f));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, SunShadowDistanceId, renderData.ShadowDistance);
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, SunNormalBiasId, Mathf.Max(_normalBias, 0.0f));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, SunJitterRadiusId, Mathf.Max(_jitterRadius, 0.0f));
            if (UsesBlueNoiseSampling())
            {
                commandBuffer.SetRayTracingTextureParam(
                    rayTracingShader,
                    LightingBlueNoiseTexId,
                    _blueNoiseTexture != null ? _blueNoiseTexture : Texture2D.blackTexture);
            }

            BindInputTexture(commandBuffer, rayTracingShader, VoxelExperimentsGbufferTexture.Normal);
            BindInputTexture(commandBuffer, rayTracingShader, VoxelExperimentsGbufferTexture.Depth);
            commandBuffer.SetRayTracingTextureParam(
                rayTracingShader,
                VoxelExperimentsSunLightIds.SunLightTextureId,
                VoxelExperimentsSunLightIds.GetRenderTargetIdentifier());
            commandBuffer.DispatchRays(
                rayTracingShader,
                RayGenerationShaderName,
                (uint)renderData.Width,
                (uint)renderData.Height,
                1u,
                renderData.Camera);

            commandBuffer.SetGlobalTexture(
                VoxelExperimentsSunLightIds.SunLightTextureId,
                VoxelExperimentsSunLightIds.GetRenderTargetIdentifier());
        }

        public void ConfigureSampling(LightingSamplingPattern samplingPattern, Texture blueNoiseTexture)
        {
            _samplingPattern = samplingPattern;
            _blueNoiseTexture = blueNoiseTexture;
        }

        public void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            commandBuffer.ReleaseTemporaryRT(VoxelExperimentsSunLightIds.SunLightTextureId);
        }

        private void BindInputTexture(CommandBuffer commandBuffer, RayTracingShader rayTracingShader, VoxelExperimentsGbufferTexture texture)
        {
            commandBuffer.SetRayTracingTextureParam(
                rayTracingShader,
                VoxelExperimentsGbufferIds.GetTextureId(texture),
                VoxelExperimentsGbufferIds.GetRenderTargetIdentifier(texture));
        }

        private RayTracingShader ResolveRayTracingShader()
        {
            return UsesBlueNoiseSampling()
                ? _blueNoiseRayTracingShader
                : _rayTracingShader;
        }

        private bool UsesBlueNoiseSampling()
        {
            return _samplingPattern == LightingSamplingPattern.BlueNoise &&
                   _blueNoiseRayTracingShader != null &&
                   _blueNoiseTexture != null;
        }

        private void AllocateTemporaryTargets(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                VoxelExperimentsSunLightIds.SunLightTextureId,
                CreateTextureDescriptor(width, height, ResolveOutputFormat()),
                FilterMode.Point);
        }

        private void ResolveSun(
            Camera camera,
            out Vector3 sunDirectionWs,
            out Vector3 sunColor,
            out float shadowDistance)
        {
            Light sunLight = RenderSettings.sun;
            if (sunLight != null)
            {
                sunDirectionWs = NormalizeOrFallback(-sunLight.transform.forward, Vector3.up);
                Color linearColor = sunLight.color.linear;
                sunColor = new Vector3(linearColor.r, linearColor.g, linearColor.b) * Mathf.Max(sunLight.intensity, 0.0f);
            }
            else
            {
                sunDirectionWs = NormalizeOrFallback(_fallbackSunDirection, Vector3.up);
                Color linearColor = _fallbackSunColor.linear;
                sunColor = new Vector3(linearColor.r, linearColor.g, linearColor.b);
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

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.B10G11R11_UFloatPack32
                : _outputFormat;
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
