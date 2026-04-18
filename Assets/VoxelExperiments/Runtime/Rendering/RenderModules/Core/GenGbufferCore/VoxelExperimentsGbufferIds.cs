using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
{
    public enum VoxelExperimentsGbufferTexture
    {
        Albedo = 0,
        Normal = 1,
        Depth = 2,
        SurfaceInfo = 3,
        Velocity = 4,
    }

    public static class VoxelExperimentsGbufferIds
    {
        public static readonly int AlbedoTextureId = Shader.PropertyToID("_VoxelExperimentsAlbedo");
        public static readonly int NormalTextureId = Shader.PropertyToID("_VoxelExperimentsNormal");
        public static readonly int DepthTextureId = Shader.PropertyToID("_VoxelExperimentsDepth");
        public static readonly int SurfaceInfoTextureId = Shader.PropertyToID("_VoxelExperimentsSurfaceInfo");
        public static readonly int VelocityTextureId = Shader.PropertyToID("_VoxelExperimentsVelocity");

        public static int GetTextureId(VoxelExperimentsGbufferTexture texture)
        {
            return texture switch
            {
                VoxelExperimentsGbufferTexture.Normal => NormalTextureId,
                VoxelExperimentsGbufferTexture.Depth => DepthTextureId,
                VoxelExperimentsGbufferTexture.SurfaceInfo => SurfaceInfoTextureId,
                VoxelExperimentsGbufferTexture.Velocity => VelocityTextureId,
                _ => AlbedoTextureId,
            };
        }

        public static RenderTargetIdentifier GetRenderTargetIdentifier(VoxelExperimentsGbufferTexture texture)
        {
            return new RenderTargetIdentifier(GetTextureId(texture));
        }
    }
}
