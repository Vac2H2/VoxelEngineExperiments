using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderPipeline
{
    public readonly struct VoxelRenderPipelineCameraContext
    {
        public VoxelRenderPipelineCameraContext(
            in VoxelRenderPipelineFrameContext frameContext,
            Camera camera,
            int cameraIndex)
        {
            Frame = frameContext;
            Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            CameraIndex = cameraIndex;
        }

        public VoxelRenderPipelineFrameContext Frame { get; }

        public VoxelRenderPipeline Pipeline => Frame.Pipeline;

        public VoxelRenderPipelineAsset Asset => Frame.Asset;

        public ScriptableRenderContext RenderContext => Frame.RenderContext;

        public Camera Camera { get; }

        public int CameraIndex { get; }
    }
}
