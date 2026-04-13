using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    public enum VoxelRtGbufferTexture
    {
        Albedo = 0,
        Normal = 1,
        Depth = 2,
        SurfaceInfo = 3,
    }

    public static class VoxelRtGbufferIds
    {
        public static readonly int AlbedoTextureId = Shader.PropertyToID("_VoxelRtAlbedo");
        public static readonly int NormalTextureId = Shader.PropertyToID("_VoxelRtNormal");
        public static readonly int DepthTextureId = Shader.PropertyToID("_VoxelRtDepth");
        public static readonly int SurfaceInfoTextureId = Shader.PropertyToID("_VoxelRtSurfaceInfo");

        public static int GetTextureId(VoxelRtGbufferTexture texture)
        {
            return texture switch
            {
                VoxelRtGbufferTexture.Normal => NormalTextureId,
                VoxelRtGbufferTexture.Depth => DepthTextureId,
                VoxelRtGbufferTexture.SurfaceInfo => SurfaceInfoTextureId,
                _ => AlbedoTextureId,
            };
        }

        public static RenderTargetIdentifier GetRenderTargetIdentifier(VoxelRtGbufferTexture texture)
        {
            return new RenderTargetIdentifier(GetTextureId(texture));
        }
    }
}
