using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RenderPipeline
{
    public readonly struct VoxelRenderPipelineFrameContext
    {
        private readonly IReadOnlyList<Camera> _cameras;

        public VoxelRenderPipelineFrameContext(
            VoxelRenderPipeline pipeline,
            VoxelRenderPipelineAsset asset,
            ScriptableRenderContext renderContext,
            IReadOnlyList<Camera> cameras)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            RenderContext = renderContext;
            _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
        }

        public VoxelRenderPipeline Pipeline { get; }

        public VoxelRenderPipelineAsset Asset { get; }

        public ScriptableRenderContext RenderContext { get; }

        public IReadOnlyList<Camera> Cameras => _cameras;
    }
}
