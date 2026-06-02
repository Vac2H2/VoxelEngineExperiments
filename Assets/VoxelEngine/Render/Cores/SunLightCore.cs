using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelEngine.LifeCycle.Manager;
using VoxelEngine.Render.RenderBackend;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxelEngine.Render.Cores
{
    public static class VoxelSunLightIds
    {
        public static readonly int SunLightTextureId = Shader.PropertyToID("_VoxelEngineSunLight");
        public static readonly int SunDirectionWsId = Shader.PropertyToID("_VoxelEngineSunDirectionWS");
        public static readonly int SunColorId = Shader.PropertyToID("_VoxelEngineSunColor");
        public static readonly int VisibleSunEnabledId = Shader.PropertyToID("_VoxelEngineVisibleSunEnabled");
        public static readonly int VisibleSunIntensityId = Shader.PropertyToID("_VoxelEngineVisibleSunIntensity");
        public static readonly int VisibleSunAngularRadiusId = Shader.PropertyToID("_VoxelEngineVisibleSunAngularRadius");
        public static readonly int VisibleSunHaloSizeId = Shader.PropertyToID("_VoxelEngineVisibleSunHaloSize");
        public static readonly int VisibleSunHaloIntensityId = Shader.PropertyToID("_VoxelEngineVisibleSunHaloIntensity");

        public static RenderTargetIdentifier SunLightTarget => new RenderTargetIdentifier(SunLightTextureId);
    }

    public static class VoxelSkyIds
    {
        public static readonly int SkyTextureId = Shader.PropertyToID("_VoxelEngineSkyTexture");
        public static readonly int SkyEnabledId = Shader.PropertyToID("_VoxelEngineSkyEnabled");
        public static readonly int SkyExposureId = Shader.PropertyToID("_VoxelEngineSkyExposure");
        public static readonly int SkyRotationId = Shader.PropertyToID("_VoxelEngineSkyRotation");
    }

    [Serializable]
    public sealed class SunLightCore
    {
        private const string DefaultShaderPassName = "VoxelProceduralDXR";
        private const string DefaultShaderPath = "Assets/VoxelEngine/Render/Shaders/VoxelSunLight.raytrace";
        private const string RayGenerationShaderName = "RayGenMain";
        private const float DefaultNormalBias = 0.001f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int ProjectionJitterId = Shader.PropertyToID("_VoxelEngineProjectionJitter");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int SunDirectionWsId = Shader.PropertyToID("_SunDirectionWS");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunShadowDistanceId = Shader.PropertyToID("_SunShadowDistance");
        private static readonly int SunNormalBiasId = Shader.PropertyToID("_SunNormalBias");

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField] private Vector3 _fallbackSunDirection = new Vector3(0.4f, 1.0f, 0.2f);
        [SerializeField, ColorUsage(false, true)] private Color _fallbackSunColor = Color.white;
        [SerializeField, ColorUsage(false, true)] private Color _sunLightColor = Color.white;
        [SerializeField] private bool _visibleSunEnabled = true;
        [SerializeField, Min(0.0f)] private float _visibleSunIntensity = 18.0f;
        [SerializeField, Range(0.05f, 5.0f)] private float _visibleSunAngularRadius = 0.53f;
        [SerializeField, Range(1.0f, 30.0f)] private float _visibleSunHaloSize = 8.0f;
        [SerializeField, Min(0.0f)] private float _visibleSunHaloIntensity = 1.25f;
        [SerializeField, Min(0.0f)] private float _shadowDistance;
        [SerializeField, Min(0.0f)] private float _normalBias = DefaultNormalBias;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.None;

        [NonSerialized] private RenderTexture _sunLightTexture;

        public RenderTexture OutputTexture => _sunLightTexture;

        public Color SunLightColor
        {
            get => _sunLightColor;
            set => _sunLightColor = value;
        }

        public bool VisibleSunEnabled
        {
            get => _visibleSunEnabled;
            set => _visibleSunEnabled = value;
        }

        public float VisibleSunIntensity
        {
            get => _visibleSunIntensity;
            set => _visibleSunIntensity = Mathf.Max(value, 0.0f);
        }

#if UNITY_EDITOR
        public void EditorAutoAssignDependencies()
        {
            if (_rayTracingShader == null)
            {
                _rayTracingShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(DefaultShaderPath);
            }
        }
#endif

        public bool Record(
            CommandBuffer commandBuffer,
            Camera camera,
            VoxelEngineRenderBackend renderBackend,
            GbufferCore gbufferCore,
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

            if (gbufferCore == null)
            {
                throw new ArgumentNullException(nameof(gbufferCore));
            }

            if (_rayTracingShader == null ||
                !SystemInfo.supportsRayTracing ||
                !renderBackend.HasInstances ||
                gbufferCore.NormalTexture == null)
            {
                return false;
            }

            int width = Mathf.Max(camera.pixelWidth, 1);
            int height = Mathf.Max(camera.pixelHeight, 1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            EnsurePersistentTexture(
                ref _sunLightTexture,
                width,
                height,
                ResolveOutputFormat(),
                "_VoxelEngineSunLight");

            if (renderBackend.RtasManager.HasPendingBuild)
            {
                renderBackend.RtasManager.Build(commandBuffer);
            }

            ResolveSun(camera, out Vector3 sunDirectionWs, out Vector3 sunColor, out float shadowDistance);
            commandBuffer.SetGlobalVector(
                VoxelSunLightIds.SunDirectionWsId,
                new Vector4(sunDirectionWs.x, sunDirectionWs.y, sunDirectionWs.z, 0.0f));
            commandBuffer.SetGlobalVector(
                VoxelSunLightIds.SunColorId,
                new Vector4(sunColor.x, sunColor.y, sunColor.z, 1.0f));
            commandBuffer.SetGlobalFloat(VoxelSunLightIds.VisibleSunEnabledId, _visibleSunEnabled ? 1.0f : 0.0f);
            commandBuffer.SetGlobalFloat(VoxelSunLightIds.VisibleSunIntensityId, Mathf.Max(_visibleSunIntensity, 0.0f));
            commandBuffer.SetGlobalFloat(
                VoxelSunLightIds.VisibleSunAngularRadiusId,
                Mathf.Deg2Rad * Mathf.Max(_visibleSunAngularRadius, 0.01f));
            commandBuffer.SetGlobalFloat(
                VoxelSunLightIds.VisibleSunHaloSizeId,
                Mathf.Max(_visibleSunHaloSize, 1.0f));
            commandBuffer.SetGlobalFloat(
                VoxelSunLightIds.VisibleSunHaloIntensityId,
                Mathf.Max(_visibleSunHaloIntensity, 0.0f));

            commandBuffer.SetRayTracingShaderPass(_rayTracingShader, ResolveShaderPassName());
            commandBuffer.SetRayTracingAccelerationStructure(
                _rayTracingShader,
                RayTracingAccelerationStructureId,
                renderBackend.RtasManager.AccelerationStructure);
            commandBuffer.SetRayTracingMatrixParam(
                _rayTracingShader,
                PixelCoordToViewDirWsId,
                ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, width, height));
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                ProjectionJitterId,
                new Vector4(projectionJitter.x, projectionJitter.y, 0.0f, 0.0f));

            Vector3 cameraPosition = VoxelCameraState.FromCamera(camera).Position;
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                CameraPositionWsId,
                new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0.0f));
            commandBuffer.SetRayTracingIntParam(
                _rayTracingShader,
                OpaqueInstanceMaskId,
                unchecked((int)VoxelRtasManager.OpaqueInstanceMask));
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                SunDirectionWsId,
                new Vector4(sunDirectionWs.x, sunDirectionWs.y, sunDirectionWs.z, 0.0f));
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                SunColorId,
                new Vector4(sunColor.x, sunColor.y, sunColor.z, 0.0f));
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, SunShadowDistanceId, shadowDistance);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, SunNormalBiasId, Mathf.Max(_normalBias, 0.0f));
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelGbufferIds.NormalTextureId, gbufferCore.NormalTexture);
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelGbufferIds.DepthTextureId, VoxelGbufferIds.DepthTarget);
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelSunLightIds.SunLightTextureId, _sunLightTexture);
            commandBuffer.DispatchRays(
                _rayTracingShader,
                RayGenerationShaderName,
                (uint)width,
                (uint)height,
                1u,
                camera);

            commandBuffer.SetGlobalTexture(VoxelSunLightIds.SunLightTextureId, _sunLightTexture);
            return true;
        }

        public void Dispose()
        {
            ReleasePersistentTexture(ref _sunLightTexture);
        }

        private void ResolveSun(
            Camera camera,
            out Vector3 sunDirectionWs,
            out Vector3 sunColor,
            out float shadowDistance)
        {
            Light sunLight = RenderSettings.sun;
            if (sunLight != null && sunLight.type == LightType.Directional)
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

            Color artSunColor = _sunLightColor.linear;
            sunColor = Vector3.Scale(
                sunColor,
                new Vector3(artSunColor.r, artSunColor.g, artSunColor.b));

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

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.R16G16B16A16_SFloat
                : _outputFormat;
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 1e-8f
                ? value.normalized
                : fallback.normalized;
        }

        private static bool EnsurePersistentTexture(
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
                return false;
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
            return true;
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

        private static RenderTextureDescriptor CreateTextureDescriptor(int width, int height, GraphicsFormat graphicsFormat)
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
    }
}
