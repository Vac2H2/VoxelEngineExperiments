using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RenderModules.Core
{
    public static class VoxelRtLocalLightIds
    {
        public static readonly int LocalLightTextureId = Shader.PropertyToID("_VoxelRtLocalLight");

        public static RenderTargetIdentifier GetRenderTargetIdentifier()
        {
            return new RenderTargetIdentifier(LocalLightTextureId);
        }
    }
}
