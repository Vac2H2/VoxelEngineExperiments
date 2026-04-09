using System.Runtime.InteropServices;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.ModelProceduralAabb
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ModelChunkAabb
    {
        public ModelChunkAabb(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Vector3 Min;

        public Vector3 Max;
    }
}
