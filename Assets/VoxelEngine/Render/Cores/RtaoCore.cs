using System;
using System.IO;
using System.Linq;
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
    public static class VoxelRtaoIds
    {
        public static readonly int HitDistanceTextureId = Shader.PropertyToID("_VoxelEngineHitDist");
        public static readonly int HitDistanceMaxId = Shader.PropertyToID("_VoxelEngineRtaoHitDistanceMax");
        public static readonly int OutputTextureId = Shader.PropertyToID("_VoxelEngineRtao");

        public static RenderTargetIdentifier HitDistanceTarget => new RenderTargetIdentifier(HitDistanceTextureId);
        public static RenderTargetIdentifier OutputTarget => new RenderTargetIdentifier(OutputTextureId);
    }

    public enum RtaoResolutionMode
    {
        Full = 1,
        Half = 2,
    }

    [Serializable]
    public sealed class RtaoCore
    {
        [Serializable]
        private sealed class HitDistanceSettings
        {
            [SerializeField, Min(0.0f)] private float _maxDistance = DefaultMaxDistance;
            [SerializeField, Min(0.0f)] private float _normalBias = DefaultNormalBias;
            [SerializeField, Range(MinRaysPerPixel, MaxRaysPerPixel)] private int _raysPerPixel = MinRaysPerPixel;

            public float MaxDistance => _maxDistance;
            public float NormalBias => _normalBias;
            public int RaysPerPixel
            {
                get => _raysPerPixel;
                set => _raysPerPixel = Mathf.Clamp(value, MinRaysPerPixel, MaxRaysPerPixel);
            }
        }

        public const int MinRaysPerPixel = 1;
        public const int MaxRaysPerPixel = 8;

        private const string DefaultShaderPassName = "VoxelProceduralDXR";
        private const string DefaultShaderPath = "Assets/VoxelEngine/Render/Shaders/VoxelRtao.raytrace";
        private const string DefaultStbnFolder = "Assets/STBN";
        private const string ExpectedStbnSlicePrefix = "stbn_vec2_";
        private const string RayGenerationShaderName = "RayGenMain";
        private const float DefaultMaxDistance = 24.0f;
        private const float DefaultNormalBias = 0.05f;

        private static readonly int RayTracingAccelerationStructureId = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int PixelCoordToViewDirWsId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int RayTMaxId = Shader.PropertyToID("_RayTMax");
        private static readonly int OpaqueInstanceMaskId = Shader.PropertyToID("_OpaqueInstanceMask");
        private static readonly int RtaoPixelStepId = Shader.PropertyToID("_RtaoPixelStep");
        private static readonly int RtaoMaxDistanceParamId = Shader.PropertyToID("_RtaoAmbientMaxDistance");
        private static readonly int RtaoNormalBiasParamId = Shader.PropertyToID("_RtaoAmbientNormalBias");
        private static readonly int RtaoRaysPerPixelParamId = Shader.PropertyToID("_RtaoAmbientRaysPerPixel");
        private static readonly int RtaoStbnSliceId = Shader.PropertyToID("_VoxelEngineRtaoStbnSlice");

        [NonSerialized] private RenderTexture _hitDistanceTexture;

        [SerializeField] private RayTracingShader _rayTracingShader;
        [SerializeField] private string _shaderPassName = DefaultShaderPassName;
        [SerializeField] private RtaoResolutionMode _resolutionMode = RtaoResolutionMode.Full;
        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.None;
        [SerializeField] private HitDistanceSettings _ambient = new HitDistanceSettings();
        [SerializeField] private Texture2D[] _stbnSlices = Array.Empty<Texture2D>();

        public int RaysPerPixel
        {
            get => AmbientRaysPerPixel;
            set => AmbientRaysPerPixel = value;
        }

        public int AmbientRaysPerPixel
        {
            get => ResolveRaysPerPixel();
            set => _ambient.RaysPerPixel = value;
        }

        public float AmbientMaxDistance => ResolveMaxDistance();

        public RtaoResolutionMode ResolutionMode
        {
            get => ResolveResolutionMode();
            set => _resolutionMode = value == RtaoResolutionMode.Half ? RtaoResolutionMode.Half : RtaoResolutionMode.Full;
        }

        public RenderTexture HitDistanceTexture => _hitDistanceTexture;

        public RenderTexture OutputTexture => _hitDistanceTexture;

        public bool Record(
            CommandBuffer commandBuffer,
            Camera camera,
            VoxelEngineRenderBackend renderBackend,
            GbufferCore gbufferCore)
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

            if (!TryGetCurrentStbnSlice(out Texture2D stbnSlice))
            {
                return false;
            }

            int pixelStep = GetPixelStep();
            int width = Mathf.Max((camera.pixelWidth + pixelStep - 1) / pixelStep, 1);
            int height = Mathf.Max((camera.pixelHeight + pixelStep - 1) / pixelStep, 1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            EnsurePersistentTexture(
                ref _hitDistanceTexture,
                width,
                height,
                ResolveOutputFormat(),
                "_VoxelEngineHitDist");

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
                ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera, camera.pixelWidth, camera.pixelHeight));

            Vector3 cameraPosition = camera.transform.position;
            commandBuffer.SetRayTracingVectorParam(
                _rayTracingShader,
                CameraPositionWsId,
                new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0.0f));
            commandBuffer.SetRayTracingFloatParam(
                _rayTracingShader,
                RayTMaxId,
                Mathf.Max(camera.farClipPlane, 1e-4f));
            commandBuffer.SetRayTracingIntParam(
                _rayTracingShader,
                OpaqueInstanceMaskId,
                unchecked((int)VoxelRtasManager.OpaqueInstanceMask));
            commandBuffer.SetRayTracingIntParam(_rayTracingShader, RtaoPixelStepId, pixelStep);
            commandBuffer.SetRayTracingIntParam(_rayTracingShader, RtaoRaysPerPixelParamId, ResolveRaysPerPixel());
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RtaoMaxDistanceParamId, ResolveMaxDistance());
            commandBuffer.SetRayTracingFloatParam(_rayTracingShader, RtaoNormalBiasParamId, ResolveNormalBias());
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelGbufferIds.NormalTextureId, gbufferCore.NormalTexture);
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelGbufferIds.DepthTextureId, VoxelGbufferIds.DepthTarget);
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, RtaoStbnSliceId, stbnSlice);
            commandBuffer.SetRayTracingTextureParam(_rayTracingShader, VoxelRtaoIds.HitDistanceTextureId, _hitDistanceTexture);
            commandBuffer.DispatchRays(
                _rayTracingShader,
                RayGenerationShaderName,
                (uint)width,
                (uint)height,
                1u,
                camera);

            commandBuffer.SetGlobalTexture(VoxelRtaoIds.HitDistanceTextureId, _hitDistanceTexture);
            commandBuffer.SetGlobalTexture(VoxelRtaoIds.OutputTextureId, _hitDistanceTexture);
            commandBuffer.SetGlobalFloat(VoxelRtaoIds.HitDistanceMaxId, ResolveMaxDistance());
            return true;
        }

        public void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }
        }

        public void Dispose()
        {
            ReleasePersistentTexture(ref _hitDistanceTexture);
        }

#if UNITY_EDITOR
        public void EditorAutoAssignDependencies()
        {
            if (_rayTracingShader == null)
            {
                _rayTracingShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(DefaultShaderPath);
            }

            Texture2D[] discoveredSlices = AssetDatabase.FindAssets("t:Texture2D", new[] { DefaultStbnFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsExpectedStbnAssetPath)
                .Select(EnsureStbnImportSettings)
                .OrderBy(ExtractSliceSortKey, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .Where(texture => texture != null)
                .ToArray();

            bool needsUpdate = _stbnSlices == null || _stbnSlices.Length != discoveredSlices.Length;
            if (!needsUpdate)
            {
                for (int index = 0; index < discoveredSlices.Length; index++)
                {
                    if (_stbnSlices[index] != discoveredSlices[index])
                    {
                        needsUpdate = true;
                        break;
                    }
                }
            }

            if (needsUpdate)
            {
                _stbnSlices = discoveredSlices;
            }
        }

        private static string ExtractSliceSortKey(string assetPath)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);
            int separatorIndex = fileNameWithoutExtension.LastIndexOf('_');
            if (separatorIndex < 0 || separatorIndex >= fileNameWithoutExtension.Length - 1)
            {
                return fileNameWithoutExtension;
            }

            string suffix = fileNameWithoutExtension.Substring(separatorIndex + 1);
            return int.TryParse(suffix, out int sliceIndex)
                ? sliceIndex.ToString("D4")
                : fileNameWithoutExtension;
        }

        private static bool IsExpectedStbnAssetPath(string assetPath)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);
            return fileNameWithoutExtension.StartsWith(ExpectedStbnSlicePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureStbnImportSettings(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter))
            {
                return assetPath;
            }

            bool hasChanges = false;
            if (textureImporter.sRGBTexture)
            {
                textureImporter.sRGBTexture = false;
                hasChanges = true;
            }

            if (textureImporter.mipmapEnabled)
            {
                textureImporter.mipmapEnabled = false;
                hasChanges = true;
            }

            if (textureImporter.filterMode != FilterMode.Point)
            {
                textureImporter.filterMode = FilterMode.Point;
                hasChanges = true;
            }

            if (textureImporter.wrapMode != TextureWrapMode.Repeat)
            {
                textureImporter.wrapMode = TextureWrapMode.Repeat;
                hasChanges = true;
            }

            if (textureImporter.textureCompression != TextureImporterCompression.Uncompressed)
            {
                textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                hasChanges = true;
            }

            if (textureImporter.npotScale != TextureImporterNPOTScale.None)
            {
                textureImporter.npotScale = TextureImporterNPOTScale.None;
                hasChanges = true;
            }

            if (hasChanges)
            {
                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }

            return assetPath;
        }
#endif

        private bool TryGetCurrentStbnSlice(out Texture2D stbnSlice)
        {
            stbnSlice = null;
            if (_stbnSlices == null || _stbnSlices.Length == 0)
            {
                return false;
            }

            int sliceCount = 0;
            for (int index = 0; index < _stbnSlices.Length; index++)
            {
                if (IsExpectedStbnSlice(_stbnSlices[index]))
                {
                    sliceCount++;
                }
            }

            if (sliceCount == 0)
            {
                return false;
            }

            int sliceIndex = Mathf.Abs(Time.frameCount) % sliceCount;
            int validIndex = 0;
            for (int index = 0; index < _stbnSlices.Length; index++)
            {
                Texture2D candidate = _stbnSlices[index];
                if (!IsExpectedStbnSlice(candidate))
                {
                    continue;
                }

                if (validIndex == sliceIndex)
                {
                    stbnSlice = candidate;
                    return true;
                }

                validIndex++;
            }

            for (int index = 0; index < _stbnSlices.Length; index++)
            {
                if (IsExpectedStbnSlice(_stbnSlices[index]))
                {
                    stbnSlice = _stbnSlices[index];
                    return true;
                }
            }

            return false;
        }

        private static bool IsExpectedStbnSlice(Texture2D texture)
        {
            return texture != null &&
                   !string.IsNullOrWhiteSpace(texture.name) &&
                   texture.name.StartsWith(ExpectedStbnSlicePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private int GetPixelStep()
        {
            return ResolveResolutionMode() == RtaoResolutionMode.Half ? 2 : 1;
        }

        private string ResolveShaderPassName()
        {
            return string.IsNullOrWhiteSpace(_shaderPassName)
                ? DefaultShaderPassName
                : _shaderPassName;
        }

        private RtaoResolutionMode ResolveResolutionMode()
        {
            return _resolutionMode == RtaoResolutionMode.Half
                ? RtaoResolutionMode.Half
                : RtaoResolutionMode.Full;
        }

        private float ResolveMaxDistance()
        {
            return _ambient == null || _ambient.MaxDistance <= 0.0f
                ? DefaultMaxDistance
                : _ambient.MaxDistance;
        }

        private float ResolveNormalBias()
        {
            if (_ambient == null)
            {
                return DefaultNormalBias;
            }

            return _ambient.NormalBias < 0.0f
                ? 0.0f
                : (_ambient.NormalBias == 0.0f ? DefaultNormalBias : _ambient.NormalBias);
        }

        private int ResolveRaysPerPixel()
        {
            return Mathf.Clamp(_ambient?.RaysPerPixel ?? MinRaysPerPixel, MinRaysPerPixel, MaxRaysPerPixel);
        }

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.R16_SFloat
                : _outputFormat;
        }

        private static void EnsurePersistentTexture(
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
                return;
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
