using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    public static class VoxelRtSunLightIds
    {
        public static readonly int SunLightTextureId = Shader.PropertyToID("_VoxelRtSunLight");

        public static RenderTargetIdentifier GetRenderTargetIdentifier()
        {
            return new RenderTargetIdentifier(SunLightTextureId);
        }
    }
}
