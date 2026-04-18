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
    public sealed class GenAoCore
    {
        private const string DefaultShaderPassName = "VoxelLightingAO";
        private const string RayGenerationShaderName = "RayGenMain";
        private const int OpaqueInstanceMask = 0x1;
        private const int DefaultSampleCount = 2;
        private const float DefaultMaxDistance = 24.0f;
        private const float DefaultMaxAmbientVisibility = 0.25f;
        private const float DefaultNormalBias = 0.05f;
        private const float DefaultJitterRadius = 0.025f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int CameraForwardWsId = Shader.PropertyToID("_CameraForwardWS");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int AoFrameIndexId = Shader.PropertyToID("_AoFrameIndex");
        private static readonly int AoSampleCountId = Shader.PropertyToID("_AoSampleCount");
        private static readonly int AoMaxDistanceId = Shader.PropertyToID("_AoMaxDistance");
        private static readonly int AoMaxAmbientVisibilityId = Shader.PropertyToID("_AoMaxAmbientVisibility");
        private static readonly int AoNormalBiasId = Shader.PropertyToID("_AoNormalBias");
        private static readonly int AoJitterRadiusId = Shader.PropertyToID("_AoJitterRadius");
        private static readonly int LightingBlueNoiseTexId = Shader.PropertyToID("_LightingBlueNoiseTex");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private RayTracingShader _blueNoiseRayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField, Min(0)] private int _sampleCount = DefaultSampleCount;
        [SerializeField, Min(0.0f)] private float _maxDistance = DefaultMaxDistance;
        [SerializeField, Range(0.0f, 1.0f)] private float _maxAmbientVisibility = DefaultMaxAmbientVisibility;
        [SerializeField, Min(0.0f)] private float _normalBias = DefaultNormalBias;
        [SerializeField, Min(0.0f)] private float _jitterRadius = DefaultJitterRadius;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.R16_SFloat;

        [NonSerialized] private LightingSamplingPattern _samplingPattern;
        [NonSerialized] private Texture _blueNoiseTexture;

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

            renderData = new RenderData(bootstrap.Runtime, camera, width, height);
            return true;
        }

        public void RecordAoFill(
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
            commandBuffer.SetRayTracingIntParam(rayTracingShader, AoFrameIndexId, Time.frameCount);
            commandBuffer.SetRayTracingIntParam(rayTracingShader, AoSampleCountId, Mathf.Max(_sampleCount, 0));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, AoMaxDistanceId, Mathf.Max(_maxDistance, 0.0f));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, AoMaxAmbientVisibilityId, Mathf.Clamp01(_maxAmbientVisibility));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, AoNormalBiasId, Mathf.Max(_normalBias, 0.0f));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, AoJitterRadiusId, Mathf.Max(_jitterRadius, 0.0f));
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
                VoxelExperimentsAoIds.AoTextureId,
                VoxelExperimentsAoIds.GetRenderTargetIdentifier());
            commandBuffer.DispatchRays(
                rayTracingShader,
                RayGenerationShaderName,
                (uint)renderData.Width,
                (uint)renderData.Height,
                1u,
                renderData.Camera);

            commandBuffer.SetGlobalTexture(
                VoxelExperimentsAoIds.AoTextureId,
                VoxelExperimentsAoIds.GetRenderTargetIdentifier());
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

            commandBuffer.ReleaseTemporaryRT(VoxelExperimentsAoIds.AoTextureId);
        }

        private void AllocateTemporaryTargets(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                VoxelExperimentsAoIds.AoTextureId,
                CreateTextureDescriptor(width, height, ResolveOutputFormat()),
                FilterMode.Point);
        }

        private void BindInputTexture(CommandBuffer commandBuffer, RayTracingShader rayTracingShader, VoxelExperimentsGbufferTexture texture)
        {
            int textureId = VoxelExperimentsGbufferIds.GetTextureId(texture);
            commandBuffer.SetRayTracingTextureParam(
                rayTracingShader,
                textureId,
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

        private string ResolveShaderPassName()
        {
            return string.IsNullOrWhiteSpace(_shaderPassName)
                ? DefaultShaderPassName
                : _shaderPassName;
        }

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.R16_SFloat
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
