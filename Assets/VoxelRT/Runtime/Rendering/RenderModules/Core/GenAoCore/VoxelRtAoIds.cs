using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    public static class VoxelRtAoIds
    {
        public static readonly int AoTextureId = Shader.PropertyToID("_VoxelRtAo");

        public static RenderTargetIdentifier GetRenderTargetIdentifier()
        {
            return new RenderTargetIdentifier(AoTextureId);
        }
    }
}
