using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Render.Cores;
using VoxelEngine.Render.NRD.Cores;

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
        [SerializeField] private bool _enableRtaoDenoiseInSrp;
        [SerializeField] private RtaoDenoiseCore _rtaoDenoiseCore = new RtaoDenoiseCore();

        public Material RayTracingMaterial => _rayTracingMaterial;
        public GbufferCore GbufferCore => _gbufferCore ?? (_gbufferCore = new GbufferCore());
        public RtaoCore RtaoCore => _rtaoCore ?? (_rtaoCore = new RtaoCore());
        public bool EnableRtaoDenoiseInSrp => _enableRtaoDenoiseInSrp;
        public RtaoDenoiseCore RtaoDenoiseCore => _rtaoDenoiseCore ?? (_rtaoDenoiseCore = new RtaoDenoiseCore());

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
            _rtaoDenoiseCore ??= new RtaoDenoiseCore();
            _rtaoCore.EditorAutoAssignDependencies();
        }
#endif
    }
}
