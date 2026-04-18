using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using VoxelExperiments.Runtime.Rendering.RenderModules.Core;
using VoxelExperiments.Runtime.Rendering.RenderPipeline;

namespace VoxelExperiments.Runtime.Rendering.RenderModules
{
    [CreateAssetMenu(
        menuName = "VoxelExperiments/Rendering/Render Modules/RtGbuffer Module",
        fileName = "RtGbufferModule")]
    public sealed class RtGbufferModule : VoxelRenderPipelineModule, ISerializationCallbackReceiver
    {
        [SerializeField] private GenGbufferCore _genGbufferCore = new();
        [SerializeField] private PreviewTarget _previewTarget = PreviewTarget.Albedo;

        // Keep these hidden legacy fields so existing module assets migrate cleanly.
        [FormerlySerializedAs("_rayTracingShader")]
        [SerializeField, HideInInspector] private RayTracingShader _legacyRayTracingShader;
        [FormerlySerializedAs("_shaderPassName")]
        [SerializeField, HideInInspector] private string _legacyShaderPassName;

        private enum PreviewTarget
        {
            Albedo = 0,
            Normal = 1,
            Depth = 2,
            SurfaceInfo = 3,
        }

        protected override bool OnRender(in VoxelRenderPipelineCameraContext context)
        {
            if (!_genGbufferCore.TryCreateRenderData(in context, out GenGbufferCore.RenderData renderData))
            {
                return false;
            }

            Camera camera = renderData.Camera;
            ScriptableRenderContext renderContext = context.RenderContext;
            CommandBuffer commandBuffer = new()
            {
                name = string.IsNullOrWhiteSpace(name) ? nameof(RtGbufferModule) : name
            };

            try
            {
                renderContext.SetupCameraProperties(camera);
                _genGbufferCore.RecordGBufferFill(commandBuffer, in renderData);
                renderContext.ExecuteCommandBuffer(commandBuffer);
                commandBuffer.Clear();

                bool handledBySubmodule = RenderSubmodules(in context);
                if (!handledBySubmodule)
                {
                    commandBuffer.Blit(ResolvePreviewSource(), BuiltinRenderTextureType.CameraTarget);
                    renderContext.ExecuteCommandBuffer(commandBuffer);
                    commandBuffer.Clear();
                }

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

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            MigrateLegacyConfiguration();
        }

        private void OnValidate()
        {
            MigrateLegacyConfiguration();
        }

        private void MigrateLegacyConfiguration()
        {
            _genGbufferCore ??= new GenGbufferCore();
            _genGbufferCore.ApplyLegacyConfiguration(_legacyRayTracingShader, _legacyShaderPassName);
        }

        private RenderTargetIdentifier ResolvePreviewSource()
        {
            return _previewTarget switch
            {
                PreviewTarget.Normal => VoxelExperimentsGbufferIds.GetRenderTargetIdentifier(VoxelExperimentsGbufferTexture.Normal),
                PreviewTarget.Depth => VoxelExperimentsGbufferIds.GetRenderTargetIdentifier(VoxelExperimentsGbufferTexture.Depth),
                PreviewTarget.SurfaceInfo => VoxelExperimentsGbufferIds.GetRenderTargetIdentifier(VoxelExperimentsGbufferTexture.SurfaceInfo),
                _ => VoxelExperimentsGbufferIds.GetRenderTargetIdentifier(VoxelExperimentsGbufferTexture.Albedo),
            };
        }
    }
}
