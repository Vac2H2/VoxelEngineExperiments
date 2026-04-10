using UnityEngine;

namespace VoxelRT.Runtime.Rendering.VoxelRenderer
{
    internal static class VoxelMaterialPropertyIds
    {
        public static readonly int ModelResidencyId = Shader.PropertyToID("_VoxelModelResidencyId");
        public static readonly int PaletteResidencyId = Shader.PropertyToID("_VoxelPaletteResidencyId");
    }
}
