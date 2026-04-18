using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
{
    public static class VoxelExperimentsAoIds
    {
        public static readonly int AoTextureId = Shader.PropertyToID("_VoxelExperimentsAo");

        public static RenderTargetIdentifier GetRenderTargetIdentifier()
        {
            return new RenderTargetIdentifier(AoTextureId);
        }
    }
}
