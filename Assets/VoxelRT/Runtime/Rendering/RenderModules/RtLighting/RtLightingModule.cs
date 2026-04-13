using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RenderModules.Core;
using VoxelRT.Runtime.Rendering.RenderPipeline;

namespace VoxelRT.Runtime.Rendering.RenderModules
{
    [CreateAssetMenu(
        menuName = "VoxelRT/Rendering/Render Modules/RtLighting Module",
        fileName = "RtLightingModule")]
    public sealed class RtLightingModule : VoxelRenderPipelineModule
    {
        private static readonly int AoPreviewTextureId = Shader.PropertyToID("_VoxelRtAoPreview");

        [SerializeField] private GenGbufferCore _genGbufferCore = new();
        [SerializeField] private GenAoCore _genAoCore = new();
        [SerializeField] private Material _aoPreviewMaterial;
        [SerializeField] private PreviewTarget _previewTarget = PreviewTarget.Ao;

        private enum PreviewTarget
        {
            Ao = 0,
            Albedo = 1,
            Normal = 2,
            Depth = 3,
            SurfaceInfo = 4,
        }

        protected override bool OnRender(in VoxelRenderPipelineCameraContext context)
        {
            EnsureCores();

            if (!_genGbufferCore.TryCreateRenderData(in context, out GenGbufferCore.RenderData gbufferRenderData))
            {
                return false;
            }

            if (!_genAoCore.TryCreateRenderData(in context, out GenAoCore.RenderData aoRenderData))
            {
                return false;
            }

            Camera camera = gbufferRenderData.Camera;
            ScriptableRenderContext renderContext = context.RenderContext;
            CommandBuffer commandBuffer = new()
            {
                name = string.IsNullOrWhiteSpace(name) ? nameof(RtLightingModule) : name
            };

            try
            {
                renderContext.SetupCameraProperties(camera);
                _genGbufferCore.RecordGBufferFill(commandBuffer, in gbufferRenderData);
                _genAoCore.RecordAoFill(commandBuffer, in aoRenderData);
                renderContext.ExecuteCommandBuffer(commandBuffer);
                commandBuffer.Clear();

                bool handledBySubmodule = RenderSubmodules(in context);
                if (!handledBySubmodule)
                {
                    RecordPreview(commandBuffer, gbufferRenderData.Width, gbufferRenderData.Height);
                    renderContext.ExecuteCommandBuffer(commandBuffer);
                    commandBuffer.Clear();
                }

                ReleasePreviewTargets(commandBuffer);
                _genAoCore.ReleaseTemporaryTargets(commandBuffer);
                _genGbufferCore.ReleaseTemporaryTargets(commandBuffer);
                renderContext.ExecuteCommandBuffer(commandBuffer);
            }
            finally
            {
                commandBuffer.Clear();
                commandBuffer.Release();
            }

            renderContext.Submit();
            return true;
        }

        private void OnValidate()
        {
            EnsureCores();
        }

        private void EnsureCores()
        {
            _genGbufferCore ??= new GenGbufferCore();
            _genAoCore ??= new GenAoCore();
        }

        private void RecordPreview(CommandBuffer commandBuffer, int width, int height)
        {
            RenderTargetIdentifier previewSource = ResolvePreviewSource();
            if (_previewTarget == PreviewTarget.Ao && _aoPreviewMaterial != null)
            {
                AllocateAoPreviewTarget(commandBuffer, width, height);
                commandBuffer.Blit(previewSource, AoPreviewTextureId, _aoPreviewMaterial);
                commandBuffer.SetGlobalTexture(AoPreviewTextureId, new RenderTargetIdentifier(AoPreviewTextureId));
                commandBuffer.Blit(AoPreviewTextureId, BuiltinRenderTextureType.CameraTarget);
                return;
            }

            commandBuffer.Blit(previewSource, BuiltinRenderTextureType.CameraTarget);
        }

        private static void AllocateAoPreviewTarget(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                AoPreviewTextureId,
                new RenderTextureDescriptor(width, height)
                {
                    dimension = TextureDimension.Tex2D,
                    depthBufferBits = 0,
                    msaaSamples = 1,
                    graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                    enableRandomWrite = false,
                    useMipMap = false,
                    autoGenerateMips = false
                },
                FilterMode.Point);
        }

        private void ReleasePreviewTargets(CommandBuffer commandBuffer)
        {
            if (_previewTarget == PreviewTarget.Ao && _aoPreviewMaterial != null)
            {
                commandBuffer.ReleaseTemporaryRT(AoPreviewTextureId);
            }
        }

        private RenderTargetIdentifier ResolvePreviewSource()
        {
            return _previewTarget switch
            {
                PreviewTarget.Albedo => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.Albedo),
                PreviewTarget.Normal => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.Normal),
                PreviewTarget.Depth => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.Depth),
                PreviewTarget.SurfaceInfo => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.SurfaceInfo),
                _ => VoxelRtAoIds.GetRenderTargetIdentifier(),
            };
        }
    }
}
