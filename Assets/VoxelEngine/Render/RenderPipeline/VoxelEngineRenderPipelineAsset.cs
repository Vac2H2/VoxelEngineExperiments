using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Render.Cores;

namespace VoxelEngine.Render.RenderPipeline
{
    [CreateAssetMenu(
        menuName = "VoxelEngine/Rendering/Render Pipeline Asset",
        fileName = "VoxelEngineRenderPipelineAsset")]
    public sealed class VoxelEngineRenderPipelineAsset : RenderPipelineAsset<VoxelEngineRenderPipeline>
    {
        private const string RenderPipelineShaderTagValue = "VoxelEngineRenderPipeline";

        [SerializeField] private Material _rayTracingMaterial;
        [SerializeField] private GbufferCore _gbufferCore = new GbufferCore();
        [SerializeField] private RtaoCore _rtaoCore = new RtaoCore();

        public Material RayTracingMaterial => _rayTracingMaterial;
        public GbufferCore GbufferCore => _gbufferCore ?? (_gbufferCore = new GbufferCore());
        public RtaoCore RtaoCore => _rtaoCore ?? (_rtaoCore = new RtaoCore());

        public override string renderPipelineShaderTag => RenderPipelineShaderTagValue;

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
            if (_rayTracingMaterial == null)
            {
                Debug.LogError(
                    $"'{name}' requires a ray tracing material before {nameof(VoxelEngineRenderPipeline)} can be created.",
                    this);
                return null;
            }

            return new VoxelEngineRenderPipeline(this);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            _gbufferCore ??= new GbufferCore();
            _rtaoCore ??= new RtaoCore();
            _rtaoCore.EditorAutoAssignDependencies();
        }
#endif
    }
}
