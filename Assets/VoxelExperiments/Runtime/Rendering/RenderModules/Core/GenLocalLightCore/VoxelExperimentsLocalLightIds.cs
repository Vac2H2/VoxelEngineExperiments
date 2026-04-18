using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RenderModules.Core
{
    public static class VoxelExperimentsLocalLightIds
    {
        public static readonly int LocalLightTextureId = Shader.PropertyToID("_VoxelExperimentsLocalLight");

        public static RenderTargetIdentifier GetRenderTargetIdentifier()
        {
            return new RenderTargetIdentifier(LocalLightTextureId);
        }
    }
}
