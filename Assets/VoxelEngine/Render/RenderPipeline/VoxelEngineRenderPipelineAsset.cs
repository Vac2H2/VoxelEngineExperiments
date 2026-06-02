using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine;
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
        [SerializeField] private Shader _gbufferPreviewShader;
        [SerializeField] private Texture2D _skyTexture;
        [SerializeField, Min(0.0f)] private float _skyExposure = 1.0f;
        [SerializeField, Range(0.0f, 360.0f)] private float _skyRotation;
        [SerializeField, Min(VoxelEngineSettings.MinimumVoxelSize)] private float _globalVoxelSize = VoxelEngineSettings.DefaultVoxelSize;
        [SerializeField] private GbufferCore _gbufferCore = new GbufferCore();
        [SerializeField] private RtaoCore _rtaoCore = new RtaoCore();
        [SerializeField] private SunLightCore _sunLightCore = new SunLightCore();
        [SerializeField] private ReflectionCore _reflectionCore = new ReflectionCore();
        [SerializeField] private RtaoFrameAverageCore _rtaoFrameAverageCore = new RtaoFrameAverageCore();
        [SerializeField] private TaaCore _taaCore = new TaaCore();

        public Material RayTracingMaterial => _rayTracingMaterial;
        public Shader GbufferPreviewShader => _gbufferPreviewShader;
        public Texture2D SkyTexture => _skyTexture;

        public float GlobalVoxelSize
        {
            get => VoxelEngineSettings.SanitizeVoxelSize(_globalVoxelSize);
            set => _globalVoxelSize = VoxelEngineSettings.SanitizeVoxelSize(value);
        }

        public float SkyExposure
        {
            get => _skyExposure;
            set => _skyExposure = Mathf.Max(value, 0.0f);
        }

        public float SkyRotation
        {
            get => _skyRotation;
            set => _skyRotation = Mathf.Repeat(value, 360.0f);
        }

        public GbufferCore GbufferCore => _gbufferCore ?? (_gbufferCore = new GbufferCore());
        public RtaoCore RtaoCore => _rtaoCore ?? (_rtaoCore = new RtaoCore());
        public SunLightCore SunLightCore => _sunLightCore ?? (_sunLightCore = new SunLightCore());
        public ReflectionCore ReflectionCore => _reflectionCore ?? (_reflectionCore = new ReflectionCore());
        public RtaoFrameAverageCore RtaoFrameAverageCore => _rtaoFrameAverageCore ?? (_rtaoFrameAverageCore = new RtaoFrameAverageCore());
        public TaaCore TaaCore => _taaCore ?? (_taaCore = new TaaCore());

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
            _sunLightCore ??= new SunLightCore();
            _reflectionCore ??= new ReflectionCore();
            _rtaoFrameAverageCore ??= new RtaoFrameAverageCore();
            _taaCore ??= new TaaCore();
            _globalVoxelSize = VoxelEngineSettings.SanitizeVoxelSize(_globalVoxelSize);
            _rtaoCore.EditorAutoAssignDependencies();
            _sunLightCore.EditorAutoAssignDependencies();
            _reflectionCore.EditorAutoAssignDependencies();
            _rtaoFrameAverageCore.EditorAutoAssignDependencies();
            _taaCore.EditorAutoAssignDependencies();
        }
#endif
    }
}
