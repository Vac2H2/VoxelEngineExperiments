using System;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.RenderPipeline
{
    [CreateAssetMenu(menuName = "VoxelRT/Rendering/Render Pipeline Asset", fileName = "VoxelRenderPipelineAsset")]
    public sealed class VoxelRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset<VoxelRenderPipeline>
    {
        private const string RenderPipelineShaderTagValue = "VoxelRenderPipeline";

        [SerializeField] private VoxelRenderPipelineModule[] _modules = Array.Empty<VoxelRenderPipelineModule>();

        internal VoxelRenderPipelineModule[] Modules => _modules ?? Array.Empty<VoxelRenderPipelineModule>();

        public override string renderPipelineShaderTag => RenderPipelineShaderTagValue;

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
            return new VoxelRenderPipeline(this);
        }
    }
}
