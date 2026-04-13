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
        private static readonly int ScalarPreviewTextureId = Shader.PropertyToID("_VoxelRtScalarPreview");

        [SerializeField] private GenGbufferCore _genGbufferCore = new();
        [SerializeField] private GenAoCore _genAoCore = new();
        [SerializeField] private GenSunLightCore _genSunLightCore = new();
        [SerializeField] private GenLocalLightCore _genLocalLightCore = new();
        [SerializeField] private GenLightAdditiveCore _genLightAdditiveCore = new();
        [SerializeField] private GenDenoiseLightingCore _genDenoiseLightingCore = new();
        [SerializeField] private GenComposeLightingCore _genComposeLightingCore = new();
        [SerializeField] private Material _aoPreviewMaterial;
        [SerializeField] private PreviewTarget _previewTarget = PreviewTarget.FinalColor;

        private enum PreviewTarget
        {
            Ao = 0,
            SunLight = 1,
            Albedo = 2,
            Normal = 3,
            Depth = 4,
            SurfaceInfo = 5,
            LocalLight = 6,
            LightingRaw = 7,
            LightingAfterTemporal = 8,
            LightingAfterSpatial = 9,
            FinalColor = 10,
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

            if (!_genSunLightCore.TryCreateRenderData(in context, out GenSunLightCore.RenderData sunLightRenderData))
            {
                return false;
            }

            if (!_genLocalLightCore.TryCreateRenderData(in context, out GenLocalLightCore.RenderData localLightRenderData))
            {
                return false;
            }

            if (!_genLightAdditiveCore.TryCreateRenderData(context.Camera, out GenLightAdditiveCore.RenderData lightAdditiveRenderData))
            {
                return false;
            }

            if (!_genDenoiseLightingCore.TryCreateRenderData(context.Camera, out GenDenoiseLightingCore.RenderData denoiseRenderData))
            {
                return false;
            }

            if (!_genComposeLightingCore.TryCreateRenderData(context.Camera, out GenComposeLightingCore.RenderData composeRenderData))
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
                CaptureScalarStagePreviewIfRequested(commandBuffer, gbufferRenderData.Width, gbufferRenderData.Height, PreviewTarget.Ao);
                _genSunLightCore.RecordSunLightFill(commandBuffer, in sunLightRenderData);
                _genLocalLightCore.RecordLocalLightFill(commandBuffer, in localLightRenderData);
                _genLightAdditiveCore.RecordLightAdditive(commandBuffer, in lightAdditiveRenderData);
                _genDenoiseLightingCore.RecordDenoise(commandBuffer, in denoiseRenderData);
                _genComposeLightingCore.RecordCompose(commandBuffer, in composeRenderData);
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
                _genComposeLightingCore.ReleaseTemporaryTargets(commandBuffer);
                _genDenoiseLightingCore.ReleaseTemporaryTargets(commandBuffer);
                _genLightAdditiveCore.ReleaseTemporaryTargets(commandBuffer);
                _genLocalLightCore.ReleaseTemporaryTargets(commandBuffer);
                _genSunLightCore.ReleaseTemporaryTargets(commandBuffer);
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
            _genSunLightCore ??= new GenSunLightCore();
            _genLocalLightCore ??= new GenLocalLightCore();
            _genLightAdditiveCore ??= new GenLightAdditiveCore();
            _genDenoiseLightingCore ??= new GenDenoiseLightingCore();
            _genComposeLightingCore ??= new GenComposeLightingCore();
        }

        protected override void OnPipelineDisposed()
        {
            _genLocalLightCore?.Dispose();
            _genLightAdditiveCore?.Dispose();
            _genDenoiseLightingCore?.Dispose();
            _genComposeLightingCore?.Dispose();
        }

        private void RecordPreview(CommandBuffer commandBuffer, int width, int height)
        {
            RenderTargetIdentifier previewSource = ResolvePreviewSource();
            if (UsesScalarPreview() && _aoPreviewMaterial != null)
            {
                commandBuffer.Blit(ScalarPreviewTextureId, BuiltinRenderTextureType.CameraTarget);
                return;
            }

            commandBuffer.Blit(previewSource, BuiltinRenderTextureType.CameraTarget);
        }

        private static void AllocateScalarPreviewTarget(CommandBuffer commandBuffer, int width, int height)
        {
            commandBuffer.GetTemporaryRT(
                ScalarPreviewTextureId,
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
            if (UsesScalarPreview() && _aoPreviewMaterial != null)
            {
                commandBuffer.ReleaseTemporaryRT(ScalarPreviewTextureId);
            }
        }

        private bool UsesScalarPreview()
        {
            return _previewTarget == PreviewTarget.Ao;
        }

        private void CaptureScalarStagePreviewIfRequested(
            CommandBuffer commandBuffer,
            int width,
            int height,
            PreviewTarget stage)
        {
            if (_previewTarget != stage || _aoPreviewMaterial == null)
            {
                return;
            }

            AllocateScalarPreviewTarget(commandBuffer, width, height);
            commandBuffer.Blit(ResolvePreviewSource(stage), ScalarPreviewTextureId, _aoPreviewMaterial);
            commandBuffer.SetGlobalTexture(ScalarPreviewTextureId, new RenderTargetIdentifier(ScalarPreviewTextureId));
        }

        private RenderTargetIdentifier ResolvePreviewSource()
        {
            return ResolvePreviewSource(_previewTarget);
        }

        private static RenderTargetIdentifier ResolvePreviewSource(PreviewTarget previewTarget)
        {
            return previewTarget switch
            {
                PreviewTarget.SunLight => VoxelRtSunLightIds.GetRenderTargetIdentifier(),
                PreviewTarget.LocalLight => VoxelRtLocalLightIds.GetRenderTargetIdentifier(),
                PreviewTarget.LightingRaw => VoxelRtLightingIds.GetRenderTargetIdentifier(VoxelRtLightingTexture.Raw),
                PreviewTarget.LightingAfterTemporal => VoxelRtLightingIds.GetRenderTargetIdentifier(VoxelRtLightingTexture.AfterTemporal),
                PreviewTarget.LightingAfterSpatial => VoxelRtLightingIds.GetRenderTargetIdentifier(VoxelRtLightingTexture.AfterSpatial),
                PreviewTarget.FinalColor => VoxelRtLightingIds.GetRenderTargetIdentifier(VoxelRtLightingTexture.FinalColor),
                PreviewTarget.Albedo => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.Albedo),
                PreviewTarget.Normal => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.Normal),
                PreviewTarget.Depth => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.Depth),
                PreviewTarget.SurfaceInfo => VoxelRtGbufferIds.GetRenderTargetIdentifier(VoxelRtGbufferTexture.SurfaceInfo),
                _ => VoxelRtAoIds.GetRenderTargetIdentifier(),
            };
        }
    }
}
