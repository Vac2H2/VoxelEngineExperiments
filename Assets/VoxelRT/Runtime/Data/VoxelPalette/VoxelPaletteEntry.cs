using System;
using UnityEngine;

namespace VoxelRT.Runtime.Data
{
    [Serializable]
    public struct VoxelPaletteEntry
    {
        public VoxelPaletteEntry(Color32 color, byte surfaceType)
        {
            Color = color;
            SurfaceType = surfaceType;
        }

        public Color32 Color;

        [Range(0, 255)]
        public byte SurfaceType;
    }
}
