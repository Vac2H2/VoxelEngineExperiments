using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxelEngine.Render.Cores
{
    [Serializable]
    public sealed class TaaCore
    {
        private const string DefaultShaderName = "Hidden/VoxelEngine/Rendering/TaaResolve";
        private const string DefaultShaderPath = "Assets/VoxelEngine/Render/Shaders/VoxelTaaResolve.shader";

        private static readonly int CurrentColorId = Shader.PropertyToID("_VoxelEngineTaaCurrentColor");
        private static readonly int HistoryColorId = Shader.PropertyToID("_VoxelEngineTaaHistoryColor");
        private static readonly int HistoryViewZId = Shader.PropertyToID("_VoxelEngineTaaHistoryViewZ");
        private static readonly int HistoryNormalId = Shader.PropertyToID("_VoxelEngineTaaHistoryNormal");
        private static readonly int HasHistoryId = Shader.PropertyToID("_VoxelEngineTaaHasHistory");
        private static readonly int HistoryWeightId = Shader.PropertyToID("_VoxelEngineTaaHistoryWeight");
        private static readonly int DepthRelativeThresholdId = Shader.PropertyToID("_VoxelEngineTaaDepthRelativeThreshold");
        private static readonly int NormalThresholdId = Shader.PropertyToID("_VoxelEngineTaaNormalThreshold");
        private static readonly int CurrentTexelSizeId = Shader.PropertyToID("_VoxelEngineTaaCurrentTexelSize");

        [SerializeField] private bool _enabled;
        [SerializeField, Range(0.0f, 1.0f)] private float _jitterSpread = 0.5f;
        [SerializeField, Range(0.0f, 0.99f)] private float _historyWeight = 0.92f;
        [SerializeField, Range(0.0f, 0.5f)] private float _depthRelativeThreshold = 0.08f;
        [SerializeField, Range(0.0f, 1.0f)] private float _normalThreshold = 0.82f;
        [SerializeField] private Shader _shader;

        [NonSerialized] private readonly Dictionary<int, int> _frameIndexByCameraId = new Dictionary<int, int>();
        [NonSerialized] private Material _material;
        [NonSerialized] private RenderTexture _historyColorTexture;
        [NonSerialized] private RenderTexture _historyViewZTexture;
        [NonSerialized] private RenderTexture _historyNormalTexture;
        [NonSerialized] private RenderTexture _outputTexture;
        [NonSerialized] private int _historyCameraId = int.MinValue;
        [NonSerialized] private bool _hasHistory;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public float JitterSpread
        {
            get => _jitterSpread;
            set => _jitterSpread = Mathf.Clamp01(value);
        }

        public float HistoryWeight
        {
            get => _historyWeight;
            set => _historyWeight = Mathf.Clamp(value, 0.0f, 0.99f);
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

        public bool ShouldRender(Camera camera)
        {
            return _enabled && camera != null && camera.cameraType == CameraType.Game;
        }

        public Vector2 GetProjectionJitter(Camera camera)
        {
            if (!ShouldRender(camera))
            {
                return Vector2.zero;
            }

            int cameraId = camera.GetInstanceID();
            _frameIndexByCameraId.TryGetValue(cameraId, out int frameIndex);
            _frameIndexByCameraId[cameraId] = frameIndex + 1;

            int sequenceIndex = (frameIndex % 1024) + 1;
            return new Vector2(
                Halton(sequenceIndex, 2) - 0.5f,
                Halton(sequenceIndex, 3) - 0.5f) * Mathf.Clamp01(_jitterSpread);
        }

        public bool Record(
            CommandBuffer commandBuffer,
            Camera camera,
            RenderTargetIdentifier currentColor,
            GbufferCore gbufferCore,
            int width,
            int height)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (!ShouldRender(camera) || gbufferCore == null || !EnsureMaterial())
            {
                return false;
            }

            if (gbufferCore.ViewZTexture == null || gbufferCore.NormalTexture == null || gbufferCore.MotionTexture == null)
            {
                return false;
            }

            int cameraId = camera.GetInstanceID();
            if (_historyCameraId != cameraId)
            {
                _historyCameraId = cameraId;
                _hasHistory = false;
            }

            bool recreated = false;
            recreated |= EnsurePersistentTexture(
                ref _historyColorTexture,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "_VoxelEngineTaaHistoryColor",
                FilterMode.Bilinear);
            recreated |= EnsurePersistentTexture(
                ref _historyViewZTexture,
                width,
                height,
                GraphicsFormat.R16_SFloat,
                "_VoxelEngineTaaHistoryViewZ",
                FilterMode.Bilinear);
            recreated |= EnsurePersistentTexture(
                ref _historyNormalTexture,
                width,
                height,
                GraphicsFormat.R8G8B8A8_UNorm,
                "_VoxelEngineTaaHistoryNormal",
                FilterMode.Bilinear);
            recreated |= EnsurePersistentTexture(
                ref _outputTexture,
                width,
                height,
                GraphicsFormat.R16G16B16A16_SFloat,
                "_VoxelEngineTaaOutput",
                FilterMode.Point);

            if (recreated)
            {
                _hasHistory = false;
            }

            commandBuffer.SetGlobalTexture(CurrentColorId, currentColor);
            commandBuffer.SetGlobalTexture(HistoryColorId, _historyColorTexture);
            commandBuffer.SetGlobalTexture(HistoryViewZId, _historyViewZTexture);
            commandBuffer.SetGlobalTexture(HistoryNormalId, _historyNormalTexture);
            commandBuffer.SetGlobalFloat(HasHistoryId, _hasHistory ? 1.0f : 0.0f);
            commandBuffer.SetGlobalFloat(HistoryWeightId, Mathf.Clamp(_historyWeight, 0.0f, 0.99f));
            commandBuffer.SetGlobalFloat(DepthRelativeThresholdId, Mathf.Max(_depthRelativeThreshold, 0.0f));
            commandBuffer.SetGlobalFloat(NormalThresholdId, Mathf.Clamp01(_normalThreshold));
            commandBuffer.SetGlobalVector(
                CurrentTexelSizeId,
                new Vector4(1.0f / Mathf.Max(width, 1), 1.0f / Mathf.Max(height, 1), width, height));

            commandBuffer.Blit(currentColor, _outputTexture, _material);
            commandBuffer.Blit(_outputTexture, _historyColorTexture);
            commandBuffer.Blit(gbufferCore.ViewZTexture, _historyViewZTexture);
            commandBuffer.Blit(gbufferCore.NormalTexture, _historyNormalTexture);
            _hasHistory = true;
            return true;
        }

        public void ResetHistory(Camera camera = null)
        {
            if (camera != null && _historyCameraId != int.MinValue && _historyCameraId != camera.GetInstanceID())
            {
                return;
            }

            _hasHistory = false;
        }

        public void Dispose()
        {
            ReleasePersistentTexture(ref _historyColorTexture);
            ReleasePersistentTexture(ref _historyViewZTexture);
            ReleasePersistentTexture(ref _historyNormalTexture);
            ReleasePersistentTexture(ref _outputTexture);
            _hasHistory = false;
            _historyCameraId = int.MinValue;

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

        private static float Halton(int index, int basis)
        {
            float result = 0.0f;
            float fraction = 1.0f / basis;

            while (index > 0)
            {
                result += fraction * (index % basis);
                index /= basis;
                fraction /= basis;
            }

            return result;
        }

        private static bool EnsurePersistentTexture(
            ref RenderTexture renderTexture,
            int width,
            int height,
            GraphicsFormat graphicsFormat,
            string name,
            FilterMode filterMode)
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
                filterMode = filterMode,
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
