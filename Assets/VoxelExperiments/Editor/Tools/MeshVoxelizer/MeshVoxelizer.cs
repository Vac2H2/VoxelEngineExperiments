using System;
using UnityEngine;
using VoxelExperiments.Runtime.Data;
using VoxelExperiments.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelExperiments.Editor.Tools.MeshVoxelizer
{
    public readonly struct MeshVoxelizationSettings
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

    public sealed class MeshVoxelizationResult
    {
        public MeshVoxelizationResult(
            VoxelMemoryLayout memoryLayout,
            byte[] occupancyBytes,
            byte[] voxelBytes,
            ModelChunkAabb[] chunkAabbs,
            Vector3Int[] chunkCoordinates)
        {
            if (!Enum.IsDefined(typeof(VoxelMemoryLayout), memoryLayout))
            {
                throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }

            MemoryLayout = memoryLayout;
            OccupancyBytes = occupancyBytes ?? throw new ArgumentNullException(nameof(occupancyBytes));
            VoxelBytes = voxelBytes ?? throw new ArgumentNullException(nameof(voxelBytes));
            ChunkAabbs = chunkAabbs ?? throw new ArgumentNullException(nameof(chunkAabbs));
            ChunkCoordinates = chunkCoordinates ?? throw new ArgumentNullException(nameof(chunkCoordinates));

            if (ChunkCoordinates.Length != ChunkAabbs.Length)
            {
                throw new ArgumentException("Chunk coordinate count must match chunk AABB count.", nameof(chunkCoordinates));
            }
        }

        public int ChunkCount => ChunkAabbs.Length;

        public VoxelMemoryLayout MemoryLayout { get; }

        public byte[] OccupancyBytes { get; }

        public byte[] VoxelBytes { get; }

        public ModelChunkAabb[] ChunkAabbs { get; }

        public Vector3Int[] ChunkCoordinates { get; }
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
