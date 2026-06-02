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
    public static class VoxelReflectionIds
    {
        public static readonly int RawReflectionTextureId = Shader.PropertyToID("_VoxelEngineRawReflectionColor");
        public static readonly int ReflectionTextureId = Shader.PropertyToID("_VoxelEngineReflectionColor");

        public static RenderTargetIdentifier RawReflectionTarget => new RenderTargetIdentifier(RawReflectionTextureId);
        public static RenderTargetIdentifier ReflectionTarget => new RenderTargetIdentifier(ReflectionTextureId);
    }

    [Serializable]
    public sealed class ReflectionCore
    {
        private const string DefaultShaderPassName = "VoxelProceduralDXR";
        private const string DefaultRayTracingShaderPath = "Assets/VoxelEngine/Render/Shaders/VoxelReflection.raytrace";
        private const string DefaultSpatialDenoiseShaderName = "Hidden/VoxelEngine/Rendering/ReflectionSpatialDenoise";
        private const string DefaultSpatialDenoiseShaderPath = "Assets/VoxelEngine/Render/Shaders/VoxelReflectionSpatialDenoise.shader";
        private const string RayGenerationShaderName = "RayGenMain";
        private const int SpatialDenoisePass = 0;
        private const float DefaultMaxDistance = 128.0f;
        private const float DefaultNormalBias = 0.01f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int ProjectionJitterId = Shader.PropertyToID("_VoxelEngineProjectionJitter");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int BackgroundColorId = Shader.PropertyToID("_BackgroundColor");
        private static readonly int ReflectionMaxDistanceId = Shader.PropertyToID("_VoxelEngineReflectionMaxDistance");
        private static readonly int ReflectionNormalBiasId = Shader.PropertyToID("_VoxelEngineReflectionNormalBias");
        private static readonly int SpatialTexelSizeId = Shader.PropertyToID("_VoxelEngineReflectionSpatialTexelSize");
        private static readonly int SpatialNormalPowerId = Shader.PropertyToID("_VoxelEngineReflectionSpatialNormalPower");
        private static readonly int SpatialDepthScaleId = Shader.PropertyToID("_VoxelEngineReflectionSpatialDepthScale");

        [SerializeField] private bool _enabled = true;
        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField, Min(0.0f)] private float _maxDistance = DefaultMaxDistance;
        [SerializeField, Min(0.0f)] private float _normalBias = DefaultNormalBias;
        [SerializeField] private bool _spatialDenoiseEnabled = true;
        [SerializeField, Range(1.0f, 128.0f)] private float _spatialNormalPower = 64.0f;
        [SerializeField, Range(0.0f, 64.0f)] private float _spatialDepthScale = 8.0f;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.None;
        [SerializeField] private Shader _spatialDenoiseShader;

        [NonSerialized] private Material _spatialDenoiseMaterial;
        [NonSerialized] private RenderTexture _rawReflectionTexture;
        [NonSerialized] private RenderTexture _reflectionTexture;
        [NonSerialized] private RenderTexture _outputTexture;

        public RenderTexture OutputTexture => _outputTexture;

        public RenderTexture RawTexture => _rawReflectionTexture;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public bool SpatialDenoiseEnabled
        {
            get => _spatialDenoiseEnabled;
            set => _spatialDenoiseEnabled = value;
        }

        public float MaxDistance => _maxDistance;

        public float NormalBias => _normalBias;

        public float SpatialNormalPower => _spatialNormalPower;

        public float SpatialDepthScale => _spatialDepthScale;

#if UNITY_EDITOR
        public void EditorAutoAssignDependencies()
        {
            if (_rayTracingShader == null)
            {
                _rayTracingShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(DefaultRayTracingShaderPath);
            }

            if (_spatialDenoiseShader == null)
            {
                _spatialDenoiseShader = AssetDatabase.LoadAssetAtPath<Shader>(DefaultSpatialDenoiseShaderPath);
            }
        }
#endif

        public bool Record(
            CommandBuffer commandBuffer,
            Camera camera,
            VoxelEngineRenderBackend renderBackend,
            GbufferCore gbufferCore,
            Texture skyTexture,
            float skyExposure,
            float skyRotationDegrees,
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

            if (!_enabled ||
                _rayTracingShader == null ||
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
                ref _rawReflectionTexture,
                width,
                height,
                ResolveOutputFormat(),
                "_VoxelEngineRawReflectionColor");

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

            Color backgroundColor = camera.backgroundColor.linear;
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                BackgroundColorId,
                new Vector4(backgroundColor.r, backgroundColor.g, backgroundColor.b, backgroundColor.a));
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, ReflectionMaxDistanceId, Mathf.Max(_maxDistance, 0.0f));
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, ReflectionNormalBiasId, Mathf.Max(_normalBias, 0.0f));
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, VoxelSkyIds.SkyEnabledId, skyTexture != null ? 1.0f : 0.0f);
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, VoxelSkyIds.SkyExposureId, Mathf.Max(skyExposure, 0.0f));
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, VoxelSkyIds.SkyRotationId, skyRotationDegrees * Mathf.Deg2Rad);
            commandBuffer.SetRayTracingTextureParam(
                _rayTracingShader,
                VoxelSkyIds.SkyTextureId,
                skyTexture != null ? skyTexture : Texture2D.blackTexture);

            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelGbufferIds.NormalTextureId, gbufferCore.NormalTexture);
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelGbufferIds.DepthTextureId, VoxelGbufferIds.DepthTarget);
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelReflectionIds.RawReflectionTextureId, _rawReflectionTexture);
            commandBuffer.DispatchRays(
                _rayTracingShader,
                RayGenerationShaderName,
                (uint)width,
                (uint)height,
                1u,
                camera);

            commandBuffer.SetGlobalTexture(VoxelReflectionIds.RawReflectionTextureId, _rawReflectionTexture);
            RenderTexture finalTexture = _rawReflectionTexture;
            if (_spatialDenoiseEnabled && EnsureSpatialDenoiseMaterial())
            {
                EnsurePersistentTexture(
                    ref _reflectionTexture,
                    width,
                    height,
                    ResolveOutputFormat(),
                    "_VoxelEngineReflectionColor");
                commandBuffer.SetGlobalVector(
                    SpatialTexelSizeId,
                    new Vector4(
                        1.0f / Mathf.Max(width, 1),
                        1.0f / Mathf.Max(height, 1),
                        width,
                        height));
                commandBuffer.SetGlobalFloat(SpatialNormalPowerId, Mathf.Max(_spatialNormalPower, 1.0f));
                commandBuffer.SetGlobalFloat(SpatialDepthScaleId, Mathf.Max(_spatialDepthScale, 0.0f));
                commandBuffer.Blit(_rawReflectionTexture, _reflectionTexture, _spatialDenoiseMaterial, SpatialDenoisePass);
                finalTexture = _reflectionTexture;
            }

            _outputTexture = finalTexture;
            commandBuffer.SetGlobalTexture(VoxelReflectionIds.ReflectionTextureId, _outputTexture);
            return true;
        }

        public void Dispose()
        {
            ReleasePersistentTexture(ref _rawReflectionTexture);
            ReleasePersistentTexture(ref _reflectionTexture);
            _outputTexture = null;
            DestroyMaterial(ref _spatialDenoiseMaterial);
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

        private bool EnsureSpatialDenoiseMaterial()
        {
            if (_spatialDenoiseMaterial != null)
            {
                return true;
            }

            Shader shader = _spatialDenoiseShader != null
                ? _spatialDenoiseShader
                : Shader.Find(DefaultSpatialDenoiseShaderName);
            if (shader == null)
            {
                return false;
            }

            _spatialDenoiseMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
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

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            material = null;
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
