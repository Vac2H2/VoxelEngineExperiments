using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxelEngine.Render.Cores
{
    [Serializable]
    public sealed class RtaoFrameAverageCore
    {
        private const string DefaultShaderName = "Hidden/VoxelEngine/Rendering/RtaoFrameAverage";
        private const string DefaultShaderPath = "Assets/VoxelEngine/Render/Shaders/VoxelRtaoFrameAverage.shader";
        private const int ComposeRawLightPass = 0;
        private const int SpatialPass = 1;

        private static readonly int HitDistanceSourceId = Shader.PropertyToID("_VoxelEngineRtaoHitDistanceSource");
        private static readonly int AmbientVisibilityId = Shader.PropertyToID("_VoxelEngineRtaoAmbientVisibility");
        private static readonly int AmbientLightColorId = Shader.PropertyToID("_VoxelEngineAmbientLightColor");
        private static readonly int AoStepSizeId = Shader.PropertyToID("_VoxelEngineRtaoAoStepSize");
        private static readonly int AoMinVisibilityId = Shader.PropertyToID("_VoxelEngineRtaoAoMinVisibility");
        private static readonly int SpatialSourceTexelSizeId = Shader.PropertyToID("_VoxelEngineRtaoSpatialSourceTexelSize");
        private static readonly int SpatialSourceId = Shader.PropertyToID("_VoxelEngineRtaoSpatialSource");
        private static readonly int SpatialNormalPowerId = Shader.PropertyToID("_VoxelEngineRtaoSpatialNormalPower");
        private static readonly int SpatialDepthScaleId = Shader.PropertyToID("_VoxelEngineRtaoSpatialDepthScale");
        private static readonly int SpatialRadiusId = Shader.PropertyToID("_VoxelEngineRtaoSpatialRadius");
        private static readonly int SpatialSampleCountId = Shader.PropertyToID("_VoxelEngineRtaoSpatialSampleCount");

        [SerializeField] private bool _spatialEnabled = true;
        [SerializeField, Range(1.0f, 128.0f)] private float _spatialNormalPower = 64.0f;
        [SerializeField, Range(0.0f, 64.0f)] private float _spatialDepthScale = 8.0f;
        [SerializeField, Range(1.0f, 12.0f)] private float _spatialRadius = 4.0f;
        [SerializeField, Range(4, 24)] private int _spatialSampleCount = 12;
        [SerializeField, Range(0.0f, 1.0f)] private float _ambientVisibility = 1.0f;
        [SerializeField, Range(0.0f, 20.0f)] private float _aoStepSize;
        [SerializeField, Range(0.0f, 1.0f)] private float _aoMinVisibility;
        [SerializeField, ColorUsage(false, true)] private Color _ambientLightColor = Color.white;
        [SerializeField] private Shader _shader;

        [NonSerialized] private Material _material;
        [NonSerialized] private RenderTexture _rawLightTexture;
        [NonSerialized] private RenderTexture _spatialLightTexture;
        [NonSerialized] private RenderTexture _outputTexture;

        public bool SpatialEnabled
        {
            get => _spatialEnabled;
            set => _spatialEnabled = value;
        }

        public float SpatialNormalPower => _spatialNormalPower;

        public float SpatialDepthScale => _spatialDepthScale;

        public float SpatialRadius
        {
            get => _spatialRadius;
            set => _spatialRadius = Mathf.Clamp(value, 1.0f, 12.0f);
        }

        public int SpatialSampleCount
        {
            get => _spatialSampleCount;
            set => _spatialSampleCount = Mathf.Clamp(value, 4, 24);
        }

        public float AmbientVisibility
        {
            get => _ambientVisibility;
            set => _ambientVisibility = Mathf.Clamp01(value);
        }

        public float AoStepSize
        {
            get => _aoStepSize;
            set => _aoStepSize = Mathf.Clamp(value, 0.0f, 20.0f);
        }

        public float AoMinVisibility
        {
            get => _aoMinVisibility;
            set => _aoMinVisibility = Mathf.Clamp01(value);
        }

        public Color AmbientLightColor
        {
            get => _ambientLightColor;
            set => _ambientLightColor = value;
        }

        public RenderTexture OutputTexture => _outputTexture;

#if UNITY_EDITOR
        public void EditorAutoAssignDependencies()
        {
            if (_shader == null)
            {
                _shader = AssetDatabase.LoadAssetAtPath<Shader>(DefaultShaderPath);
            }
        }
#endif

        public bool Record(CommandBuffer commandBuffer, Camera camera, RtaoCore rtaoCore, GbufferCore gbufferCore)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (rtaoCore == null)
            {
                throw new ArgumentNullException(nameof(rtaoCore));
            }

            if (gbufferCore == null)
            {
                throw new ArgumentNullException(nameof(gbufferCore));
            }

            RenderTexture currentHitDistance = rtaoCore.HitDistanceTexture;
            if (currentHitDistance == null || !EnsureMaterial())
            {
                return false;
            }

            GraphicsFormat graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            EnsurePersistentTexture(
                ref _rawLightTexture,
                currentHitDistance.width,
                currentHitDistance.height,
                graphicsFormat,
                "_VoxelEngineRawLight");

            commandBuffer.SetGlobalTexture(HitDistanceSourceId, currentHitDistance);
            commandBuffer.SetGlobalFloat(AmbientVisibilityId, Mathf.Clamp01(_ambientVisibility));
            commandBuffer.SetGlobalFloat(AoStepSizeId, Mathf.Clamp(_aoStepSize, 0.0f, 20.0f));
            commandBuffer.SetGlobalFloat(AoMinVisibilityId, Mathf.Clamp01(_aoMinVisibility));
            Color ambientLightColor = _ambientLightColor.linear;
            commandBuffer.SetGlobalVector(
                AmbientLightColorId,
                new Vector4(
                    ambientLightColor.r,
                    ambientLightColor.g,
                    ambientLightColor.b,
                    ambientLightColor.a));
            commandBuffer.Blit(currentHitDistance, _rawLightTexture, _material, ComposeRawLightPass);
            commandBuffer.SetGlobalTexture(VoxelRtaoIds.RawLightTextureId, _rawLightTexture);
            commandBuffer.SetGlobalTexture(VoxelRtaoIds.LegacyCurrentAoTextureId, _rawLightTexture);

            RenderTexture finalTexture = _rawLightTexture;
            if (_spatialEnabled)
            {
                EnsurePersistentTexture(
                    ref _spatialLightTexture,
                    currentHitDistance.width,
                    currentHitDistance.height,
                    graphicsFormat,
                    "_VoxelEngineSpatialLight");
                commandBuffer.SetGlobalTexture(SpatialSourceId, _rawLightTexture);
                commandBuffer.SetGlobalVector(
                    SpatialSourceTexelSizeId,
                    new Vector4(
                        1.0f / Mathf.Max(currentHitDistance.width, 1),
                        1.0f / Mathf.Max(currentHitDistance.height, 1),
                        currentHitDistance.width,
                        currentHitDistance.height));
                commandBuffer.SetGlobalFloat(SpatialNormalPowerId, Mathf.Max(_spatialNormalPower, 1.0f));
                commandBuffer.SetGlobalFloat(SpatialDepthScaleId, Mathf.Max(_spatialDepthScale, 0.0f));
                commandBuffer.SetGlobalFloat(SpatialRadiusId, Mathf.Clamp(_spatialRadius, 1.0f, 12.0f));
                commandBuffer.SetGlobalInt(SpatialSampleCountId, Mathf.Clamp(_spatialSampleCount, 4, 24));
                commandBuffer.Blit(_rawLightTexture, _spatialLightTexture, _material, SpatialPass);
                finalTexture = _spatialLightTexture;
            }

            commandBuffer.SetGlobalTexture(VoxelRtaoIds.SpatialLightTextureId, finalTexture);
            _outputTexture = finalTexture;

            commandBuffer.SetGlobalTexture(VoxelRtaoIds.OutputTextureId, _outputTexture);
            commandBuffer.SetGlobalTexture(VoxelRtaoIds.LegacyOutputTextureId, _outputTexture);
            return true;
        }

        public void Dispose()
        {
            ReleasePersistentTexture(ref _rawLightTexture);
            ReleasePersistentTexture(ref _spatialLightTexture);
            _outputTexture = null;

            if (_material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_material);
            }

            _material = null;
        }

        private bool EnsureMaterial()
        {
            if (_material != null)
            {
                return true;
            }

            Shader shader = _shader != null ? _shader : Shader.Find(DefaultShaderName);
            if (shader == null)
            {
                return false;
            }

            _material = new Material(shader)
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
                enableRandomWrite = false,
                useMipMap = false,
                autoGenerateMips = false
            };
        }
    }
}
