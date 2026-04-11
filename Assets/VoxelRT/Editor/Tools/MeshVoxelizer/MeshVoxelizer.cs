using System;
using UnityEngine;
using VoxelRT.Runtime.Data;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    internal readonly struct MeshVoxelizationSettings
    {
        public MeshVoxelizationSettings(float voxelSize, byte solidVoxelValue, VoxelMemoryLayout memoryLayout)
        {
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize), "Voxel size must be greater than zero.");
            }

            if (solidVoxelValue == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(solidVoxelValue), "Solid voxel value must be non-zero.");
            }

            if (!Enum.IsDefined(typeof(VoxelMemoryLayout), memoryLayout))
            {
                throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }

            VoxelSize = voxelSize;
            SolidVoxelValue = solidVoxelValue;
            MemoryLayout = memoryLayout;
        }

        public float VoxelSize { get; }

        public byte SolidVoxelValue { get; }

        public VoxelMemoryLayout MemoryLayout { get; }
    }

    internal sealed class MeshVoxelizationResult
    {
        public MeshVoxelizationResult(
            VoxelMemoryLayout memoryLayout,
            byte[] occupancyBytes,
            byte[] voxelBytes,
            ModelChunkAabb[] chunkAabbs)
        {
            if (!Enum.IsDefined(typeof(VoxelMemoryLayout), memoryLayout))
            {
                throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }

            MemoryLayout = memoryLayout;
            OccupancyBytes = occupancyBytes ?? throw new ArgumentNullException(nameof(occupancyBytes));
            VoxelBytes = voxelBytes ?? throw new ArgumentNullException(nameof(voxelBytes));
            ChunkAabbs = chunkAabbs ?? throw new ArgumentNullException(nameof(chunkAabbs));
        }

        public int ChunkCount => ChunkAabbs.Length;

        public VoxelMemoryLayout MemoryLayout { get; }

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
                DivideRoundUp(gridDimensions.x, VoxelChunkLayout.Dimension),
                DivideRoundUp(gridDimensions.y, VoxelChunkLayout.Dimension),
                DivideRoundUp(gridDimensions.z, VoxelChunkLayout.Dimension));
        }

        public static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
