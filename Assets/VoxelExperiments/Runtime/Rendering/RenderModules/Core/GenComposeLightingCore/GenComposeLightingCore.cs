using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
{
    [Serializable]
    public sealed class GenComposeLightingCore : IDisposable
    {
        private const string DefaultShaderName = "Hidden/VoxelExperiments/Rendering/RtLightingCompose";

        [SerializeField] private GraphicsFormat _outputFormat = GraphicsFormat.B10G11R11_UFloatPack32;

        [NonSerialized] private Material _runtimeMaterial;

        public readonly struct RenderData
        {
            public RenderData(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; }

            public int Height { get; }
        }

        public bool TryCreateRenderData(Camera camera, out RenderData renderData)
        {
            renderData = default;

            if (camera == null || !EnsureMaterial())
            {
                return false;
            }

            int width = Mathf.Max(camera.pixelWidth, 1);
            int height = Mathf.Max(camera.pixelHeight, 1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            renderData = new RenderData(width, height);
            return true;
        }

        public void RecordCompose(CommandBuffer commandBuffer, in RenderData renderData)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (_runtimeMaterial == null)
            {
                throw new InvalidOperationException("Compose material has not been initialized.");
            }

            commandBuffer.GetTemporaryRT(
                VoxelExperimentsLightingIds.FinalColorTextureId,
                CreateTextureDescriptor(renderData.Width, renderData.Height, ResolveOutputFormat()),
                FilterMode.Bilinear);
            commandBuffer.Blit(
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.AfterSpatial),
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.FinalColor),
                _runtimeMaterial);
            commandBuffer.SetGlobalTexture(
                VoxelExperimentsLightingIds.FinalColorTextureId,
                VoxelExperimentsLightingIds.GetRenderTargetIdentifier(VoxelExperimentsLightingTexture.FinalColor));
        }

        public void ReleaseTemporaryTargets(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            commandBuffer.ReleaseTemporaryRT(VoxelExperimentsLightingIds.FinalColorTextureId);
        }

        public void Dispose()
        {
            DestroyMaterial(ref _runtimeMaterial);
        }

        private bool EnsureMaterial()
        {
            if (_runtimeMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find(DefaultShaderName);
            if (shader == null)
            {
                return false;
            }

            _runtimeMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private GraphicsFormat ResolveOutputFormat()
        {
            return _outputFormat == GraphicsFormat.None
                ? GraphicsFormat.B10G11R11_UFloatPack32
                : _outputFormat;
        }

        private static RenderTextureDescriptor CreateTextureDescriptor(int width, int height, GraphicsFormat graphicsFormat)
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
    }
}
