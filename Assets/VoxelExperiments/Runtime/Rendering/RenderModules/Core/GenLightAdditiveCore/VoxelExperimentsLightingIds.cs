using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
{
    public enum VoxelExperimentsLightingTexture
    {
        Raw = 0,
        AfterTemporal = 1,
        AfterSpatial = 2,
        FinalColor = 3,
    }

    public static class VoxelExperimentsLightingIds
    {
        public static readonly int RawTextureId = Shader.PropertyToID("_VoxelExperimentsLightingRaw");
        public static readonly int AfterTemporalTextureId = Shader.PropertyToID("_VoxelExperimentsLightingAfterTemporal");
        public static readonly int AfterSpatialTextureId = Shader.PropertyToID("_VoxelExperimentsLightingAfterSpatial");
        public static readonly int FinalColorTextureId = Shader.PropertyToID("_VoxelExperimentsFinalColor");

        public static int GetTextureId(VoxelExperimentsLightingTexture texture)
        {
            return texture switch
            {
                VoxelExperimentsLightingTexture.AfterTemporal => AfterTemporalTextureId,
                VoxelExperimentsLightingTexture.AfterSpatial => AfterSpatialTextureId,
                VoxelExperimentsLightingTexture.FinalColor => FinalColorTextureId,
                _ => RawTextureId,
            };
        }

        public static RenderTargetIdentifier GetRenderTargetIdentifier(VoxelExperimentsLightingTexture texture)
        {
            return new RenderTargetIdentifier(GetTextureId(texture));
        }
    }
}
