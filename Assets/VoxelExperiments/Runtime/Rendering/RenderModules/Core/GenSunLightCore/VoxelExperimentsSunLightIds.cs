using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
{
    public static class VoxelExperimentsSunLightIds
    {
        public static readonly int SunLightTextureId = Shader.PropertyToID("_VoxelExperimentsSunLight");

        public static RenderTargetIdentifier GetRenderTargetIdentifier()
        {
            return new RenderTargetIdentifier(SunLightTextureId);
        }
    }
}
