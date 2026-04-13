using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.Lighting;
using VoxelRT.Runtime.Rendering.RenderPipeline;
using VoxelRT.Runtime.Rendering.VoxelRuntime;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    [Serializable]
    public sealed class GenLocalLightCore : IDisposable
    {
        private const string DefaultShaderPassName = "VoxelLightingLocal";
        private const string RayGenerationShaderName = "RayGenMain";
        private const int OpaqueInstanceMask = 0x1;
        private const int DefaultMaxActiveLights = 64;
        private const int DefaultSphereSampleCount = 4;
        private const float DefaultNormalBias = 0.05f;
        private const float DefaultMinContribution = 1e-4f;
        private const float MinimumLightRange = 1e-4f;
        private const float PointLightType = 0.0f;
        private const float SpotLightType = 1.0f;
        private const float SphereLightType = 2.0f;
        private const float NoShadowFlag = 0.0f;
        private const float ShadowFlag = 1.0f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int CameraForwardWsId = Shader.PropertyToID("_CameraForwardWS");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int LocalLightFrameIndexId = Shader.PropertyToID("_LocalLightFrameIndex");
        private static readonly int LocalLightCountId = Shader.PropertyToID("_LocalLightCount");
        private static readonly int LocalLightSphereSampleCountId = Shader.PropertyToID("_LocalLightSphereSampleCount");
        private static readonly int LocalLightNormalBiasId = Shader.PropertyToID("_LocalLightNormalBias");
        private static readonly int LocalLightMinContributionId = Shader.PropertyToID("_LocalLightMinContribution");
        private static readonly int LocalLightBufferId = Shader.PropertyToID("_LocalLightBuffer");
        private static readonly int LightingBlueNoiseTexId = Shader.PropertyToID("_LightingBlueNoiseTex");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private RayTracingShader _blueNoiseRayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField, Min(0)] private int _maxActiveLights = DefaultMaxActiveLights;
        [SerializeField, Min(1)] private int _sphereSampleCount = DefaultSphereSampleCount;
        [SerializeField, Min(0.0f)] private float _normalBias = DefaultNormalBias;
        [SerializeField, Min(0.0f)] private float _minContribution = DefaultMinContribution;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.B10G11R11_UFloatPack32;

        [NonSerialized] private GraphicsBuffer _localLightBuffer;
        [NonSerialized] private int _localLightBufferCapacity;
        [NonSerialized] private GpuLocalLightData[] _uploadCache = Array.Empty<GpuLocalLightData>();
        [NonSerialized] private List<CpuCandidateLocalLight> _candidateLights = new(DefaultMaxActiveLights);
        [NonSerialized] private LightingSamplingPattern _samplingPattern;
        [NonSerialized] private Texture _blueNoiseTexture;

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuLocalLightData
        {
            public Vector4 PositionRange;
            public Vector4 ColorType;
            public Vector4 DirectionOuterCos;
            public Vector4 ExtraData;
        }

        private readonly struct CpuCandidateLocalLight
        {
            public CpuCandidateLocalLight(GpuLocalLightData data, float sortKey)
            {
                Data = data;
                SortKey = sortKey;
            }

            public GpuLocalLightData Data { get; }

            public float SortKey { get; }
        }

        public readonly struct RenderData
        {
            public RenderData(
                VoxelRuntime.VoxelRuntime runtime,
                Camera camera,
                int width,
                int height,
                int lightCount)
            {
                Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
                Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
                Width = width;
                Height = height;
                LightCount = lightCount;
            }

            public VoxelRuntime.VoxelRuntime Runtime { get; }

            public Camera Camera { get; }

            public int Width { get; }

            public int Height { get; }

            public int LightCount { get; }
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

            int lightCount = UpdateVisibleLights(camera);
            renderData = new RenderData(
                bootstrap.Runtime,
                camera,
                width,
                height,
                lightCount);
            return true;
        }

        public void RecordLocalLightFill(
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
            commandBuffer.SetRayTracingIntParam(rayTracingShader, LocalLightFrameIndexId, Time.frameCount);
            commandBuffer.SetRayTracingIntParam(rayTracingShader, LocalLightCountId, Mathf.Max(renderData.LightCount, 0));
            commandBuffer.SetRayTracingIntParam(rayTracingShader, LocalLightSphereSampleCountId, Mathf.Max(_sphereSampleCount, 1));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, LocalLightNormalBiasId, Mathf.Max(_normalBias, 0.0f));
            commandBuffer.SetRayTracingFloatParam(rayTracingShader, LocalLightMinContributionId, Mathf.Max(_minContribution, 0.0f));
            commandBuffer.SetRayTracingBufferParam(rayTracingShader, LocalLightBufferId, _localLightBuffer);
            if (UsesBlueNoiseSampling())
            {
                commandBuffer.SetRayTracingTextureParam(
                    rayTracingShader,
                    LightingBlueNoiseTexId,
                    _blueNoiseTexture != null ? _blueNoiseTexture : Texture2D.blackTexture);
            }

            BindInputTexture(commandBuffer, rayTracingShader, VoxelRtGbufferTexture.Normal);
            BindInputTexture(commandBuffer, rayTracingShader, VoxelRtGbufferTexture.Depth);
            commandBuffer.SetRayTracingTextureParam(
                rayTracingShader,
                VoxelRtLocalLightIds.LocalLightTextureId,
                VoxelRtLocalLightIds.GetRenderTargetIdentifier());
            commandBuffer.DispatchRays(
                rayTracingShader,
                RayGenerationShaderName,
                (uint)renderData.Width,
                (uint)renderData.Height,
                1u,
                renderData.Camera);

            commandBuffer.SetGlobalTexture(
                VoxelRtLocalLightIds.LocalLightTextureId,
                VoxelRtLocalLightIds.GetRenderTargetIdentifier());
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

            commandBuffer.ReleaseTemporaryRT(VoxelRtLocalLightIds.LocalLightTextureId);
        }

        public void Dispose()
        {
            ReleaseLocalLightBuffer();
            _uploadCache = Array.Empty<GpuLocalLightData>();
            _candidateLights.Clear();
        }

        private int UpdateVisibleLights(Camera camera)
        {
            _candidateLights ??= new List<CpuCandidateLocalLight>(DefaultMaxActiveLights);
            _candidateLights.Clear();

            VoxelLocalLight[] sceneLights = UnityEngine.Object.FindObjectsByType<VoxelLocalLight>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            if (sceneLights == null || sceneLights.Length == 0)
            {
                UploadLights(0);
                return 0;
            }

            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
            Vector3 cameraPosition = camera.transform.position;

            for (int i = 0; i < sceneLights.Length; i++)
            {
                VoxelLocalLight light = sceneLights[i];
                if (!TryBuildCandidateLight(light, frustumPlanes, cameraPosition, out CpuCandidateLocalLight candidate))
                {
                    continue;
                }

                _candidateLights.Add(candidate);
            }

            _candidateLights.Sort(static (left, right) => left.SortKey.CompareTo(right.SortKey));

            int maxActiveLights = Mathf.Max(_maxActiveLights, 0);
            int lightCount = maxActiveLights == 0
                ? 0
                : Mathf.Min(_candidateLights.Count, maxActiveLights);
            UploadLights(lightCount);
            return lightCount;
        }

        private bool TryBuildCandidateLight(
            VoxelLocalLight light,
            Plane[] frustumPlanes,
            Vector3 cameraPosition,
            out CpuCandidateLocalLight candidate)
        {
            candidate = default;

            if (light == null || !light.isActiveAndEnabled)
            {
                return false;
            }

            if (light.Shape != VoxelLocalLightShape.Point &&
                light.Shape != VoxelLocalLightShape.Spot &&
                light.Shape != VoxelLocalLightShape.Sphere)
            {
                return false;
            }

            float range = Mathf.Max(light.Range, 0.0f);
            float intensity = Mathf.Max(light.Intensity, 0.0f);
            if (range <= 0.0f || intensity <= 0.0f)
            {
                return false;
            }

            Vector3 positionWs = light.Position;
            Bounds lightBounds = light.GetInfluenceBounds();
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, lightBounds))
            {
                return false;
            }

            Color linearColor = light.Color.linear;
            Vector3 color = new(linearColor.r, linearColor.g, linearColor.b);
            if (color.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            bool isSpot = light.Shape == VoxelLocalLightShape.Spot;
            bool isSphere = light.Shape == VoxelLocalLightShape.Sphere;
            float lightType = isSphere
                ? SphereLightType
                : (isSpot ? SpotLightType : PointLightType);
            Vector3 directionWs = isSpot
                ? NormalizeOrFallback(light.Forward, Vector3.forward)
                : Vector3.forward;
            float outerCos = isSpot
                ? Mathf.Cos(0.5f * Mathf.Deg2Rad * light.SpotAngle)
                : -1.0f;
            float innerCos = isSpot
                ? Mathf.Cos(0.5f * Mathf.Deg2Rad * light.InnerSpotAngle)
                : 1.0f;
            float sphereRadius = isSphere ? light.SphereRadius : 0.0f;

            float cameraDistance = Mathf.Max(0.0f, Vector3.Distance(cameraPosition, positionWs) - (range + sphereRadius));
            GpuLocalLightData data = new()
            {
                PositionRange = new Vector4(positionWs.x, positionWs.y, positionWs.z, Mathf.Max(range, MinimumLightRange)),
                ColorType = new Vector4(color.x * intensity, color.y * intensity, color.z * intensity, lightType),
                DirectionOuterCos = new Vector4(directionWs.x, directionWs.y, directionWs.z, outerCos),
                ExtraData = new Vector4(innerCos, light.CastsShadows ? ShadowFlag : NoShadowFlag, sphereRadius, 0.0f)
            };

            candidate = new CpuCandidateLocalLight(data, cameraDistance * cameraDistance);
            return true;
        }

        private void UploadLights(int lightCount)
        {
            int uploadCount = Mathf.Max(lightCount, 1);
            EnsureUploadCapacity(uploadCount);
            EnsureLocalLightBufferCapacity(uploadCount);

            if (lightCount > 0)
            {
                for (int i = 0; i < lightCount; i++)
                {
                    _uploadCache[i] = _candidateLights[i].Data;
                }
            }
            else
            {
                _uploadCache[0] = default;
            }

            _localLightBuffer.SetData(_uploadCache, 0, 0, uploadCount);
        }

        private void EnsureUploadCapacity(int requiredCapacity)
        {
            if (_uploadCache.Length >= requiredCapacity)
            {
                return;
            }

            Array.Resize(ref _uploadCache, requiredCapacity);
        }

        private void EnsureLocalLightBufferCapacity(int requiredCapacity)
        {
            if (_localLightBuffer != null && _localLightBufferCapacity >= requiredCapacity)
            {
                return;
            }

            ReleaseLocalLightBuffer();
            _localLightBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredCapacity,
                Marshal.SizeOf<GpuLocalLightData>());
            _localLightBufferCapacity = requiredCapacity;
        }

        private void ReleaseLocalLightBuffer()
        {
            if (_localLightBuffer == null)
            {
                _localLightBufferCapacity = 0;
                return;
            }

            _localLightBuffer.Release();
            _localLightBuffer = null;
            _localLightBufferCapacity = 0;
        }

        private void AllocateTemporaryTargets(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                VoxelRtLocalLightIds.LocalLightTextureId,
                CreateTextureDescriptor(width, height, ResolveOutputFormat()),
                FilterMode.Point);
        }

        private void BindInputTexture(CommandBuffer commandBuffer, RayTracingShader rayTracingShader, VoxelRtGbufferTexture texture)
        {
            commandBuffer.SetRayTracingTextureParam(
                rayTracingShader,
                VoxelRtGbufferIds.GetTextureId(texture),
                VoxelRtGbufferIds.GetRenderTargetIdentifier(texture));
        }

        private string ResolveShaderPassName()
        {
            return string.IsNullOrWhiteSpace(_shaderPassName)
                ? DefaultShaderPassName
                : _shaderPassName;
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

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.B10G11R11_UFloatPack32
                : _outputFormat;
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 1e-8f
                ? value.normalized
                : fallback.normalized;
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
