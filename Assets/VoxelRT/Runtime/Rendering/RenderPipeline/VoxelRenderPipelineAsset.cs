using System;
using UnityEngine;
using UnityEngine.Serialization;
using VoxelRT.Runtime.Rendering.RenderModules;

namespace VoxelRT.Runtime.Rendering.RenderPipeline
{
    [CreateAssetMenu(menuName = "VoxelRT/Rendering/Render Pipeline Asset", fileName = "VoxelRenderPipelineAsset")]
    public sealed class VoxelRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset<VoxelRenderPipeline>
    {
        private const string RenderPipelineShaderTagValue = "VoxelRenderPipeline";

        [FormerlySerializedAs("_modules")]
        [SerializeField] private VoxelRenderPipelineModule[] _availableModules = Array.Empty<VoxelRenderPipelineModule>();
        [SerializeField] private VoxelRenderPipelineModule _selectedModule;

        internal VoxelRenderPipelineModule[] RootModules
        {
            get
            {
                if (_selectedModule != null)
                {
                    return new[] { _selectedModule };
                }

                return _availableModules ?? Array.Empty<VoxelRenderPipelineModule>();
            }
        }

        internal VoxelRenderPipelineModule[] AvailableModules => _availableModules ?? Array.Empty<VoxelRenderPipelineModule>();

        internal VoxelRenderPipelineModule SelectedModule => _selectedModule;

        public override string renderPipelineShaderTag => RenderPipelineShaderTagValue;

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
            return new VoxelRenderPipeline(this);
        }

        private void OnValidate()
        {
            _availableModules ??= Array.Empty<VoxelRenderPipelineModule>();

            if (_selectedModule == null && _availableModules.Length == 1)
            {
                _selectedModule = _availableModules[0];
            }
        }
    }
}
