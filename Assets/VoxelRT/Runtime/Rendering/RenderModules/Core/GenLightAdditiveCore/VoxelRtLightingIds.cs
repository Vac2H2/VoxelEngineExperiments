using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    public enum VoxelRtLightingTexture
    {
        Raw = 0,
        AfterTemporal = 1,
        AfterSpatial = 2,
        FinalColor = 3,
    }

    public static class VoxelRtLightingIds
    {
        public static readonly int RawTextureId = Shader.PropertyToID("_VoxelRtLightingRaw");
        public static readonly int AfterTemporalTextureId = Shader.PropertyToID("_VoxelRtLightingAfterTemporal");
        public static readonly int AfterSpatialTextureId = Shader.PropertyToID("_VoxelRtLightingAfterSpatial");
        public static readonly int FinalColorTextureId = Shader.PropertyToID("_VoxelRtFinalColor");

        public static int GetTextureId(VoxelRtLightingTexture texture)
        {
            return texture switch
            {
                VoxelRtLightingTexture.AfterTemporal => AfterTemporalTextureId,
                VoxelRtLightingTexture.AfterSpatial => AfterSpatialTextureId,
                VoxelRtLightingTexture.FinalColor => FinalColorTextureId,
                _ => RawTextureId,
            };
        }

        public static RenderTargetIdentifier GetRenderTargetIdentifier(VoxelRtLightingTexture texture)
        {
            return new RenderTargetIdentifier(GetTextureId(texture));
        }
    }
}
