using System;
using UnityEngine;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    internal readonly struct MeshVoxelizationSettings
    {
        public MeshVoxelizationSettings(float voxelSize, byte solidVoxelValue)
        {
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize), "Voxel size must be greater than zero.");
            }

            if (solidVoxelValue == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(solidVoxelValue), "Solid voxel value must be non-zero.");
            }

            VoxelSize = voxelSize;
            SolidVoxelValue = solidVoxelValue;
        }

        public float VoxelSize { get; }

        public byte SolidVoxelValue { get; }
    }

    internal sealed class MeshVoxelizationResult
    {
        public MeshVoxelizationResult(
            byte[] occupancyBytes,
            byte[] voxelBytes,
            ModelChunkAabb[] chunkAabbs)
        {
            OccupancyBytes = occupancyBytes ?? throw new ArgumentNullException(nameof(occupancyBytes));
            VoxelBytes = voxelBytes ?? throw new ArgumentNullException(nameof(voxelBytes));
            ChunkAabbs = chunkAabbs ?? throw new ArgumentNullException(nameof(chunkAabbs));
        }

        public int ChunkCount => ChunkAabbs.Length;

        public byte[] OccupancyBytes { get; }

        public byte[] VoxelBytes { get; }

        public ModelChunkAabb[] ChunkAabbs { get; }
    }

    internal static class MeshVoxelizer
    {
        public static Vector3Int CalculateGridDimensions(Bounds bounds, float voxelSize)
        {
            if (voxelSize <= 0f)
            {
                return Vector3Int.zero;
            }

            Vector3 size = bounds.size;
            return new Vector3Int(
                Math.Max(1, Mathf.CeilToInt(size.x / voxelSize)),
                Math.Max(1, Mathf.CeilToInt(size.y / voxelSize)),
                Math.Max(1, Mathf.CeilToInt(size.z / voxelSize)));
        }

        public static Vector3Int CalculateChunkDimensions(Vector3Int gridDimensions)
        {
            return new Vector3Int(
                DivideRoundUp(gridDimensions.x, VoxelRT.Runtime.Data.VoxelChunkLayout.Dimension),
                DivideRoundUp(gridDimensions.y, VoxelRT.Runtime.Data.VoxelChunkLayout.Dimension),
                DivideRoundUp(gridDimensions.z, VoxelRT.Runtime.Data.VoxelChunkLayout.Dimension));
        }

        public static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
