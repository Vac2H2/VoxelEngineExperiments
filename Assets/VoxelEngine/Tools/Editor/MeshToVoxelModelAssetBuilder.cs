using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;
using VoxelEngine.Editor.Importer;
using VoxelExperiments.Editor.Tools.MeshVoxelizer;
using VoxelExperiments.Runtime.Data;
using EngineVoxelModel = VoxelEngine.Data.Voxel.VoxelModel;

namespace VoxelEngine.Editor.Tools
{
    internal readonly struct MeshToVoxelModelAssetBuildOptions
    {
        public MeshToVoxelModelAssetBuildOptions(float voxelSize, byte solidVoxelValue, int maxAabbsPerChunk)
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
            MaxAabbsPerChunk = Mathf.Clamp(maxAabbsPerChunk, 1, VoxelVolume.MaxAabbsPerChunk);
        }

        public float VoxelSize { get; }

        public byte SolidVoxelValue { get; }

        public int MaxAabbsPerChunk { get; }
    }

    internal readonly struct MeshToVoxelModelAssetBuildResult
    {
        public MeshToVoxelModelAssetBuildResult(byte[] serializedBytes, int chunkCount, int occupiedVoxelCount, int fallbackChunkCount)
        {
            SerializedBytes = serializedBytes ?? throw new ArgumentNullException(nameof(serializedBytes));
            ChunkCount = chunkCount;
            OccupiedVoxelCount = occupiedVoxelCount;
            FallbackChunkCount = fallbackChunkCount;
        }

        public byte[] SerializedBytes { get; }

        public int ChunkCount { get; }

        public int OccupiedVoxelCount { get; }

        public int FallbackChunkCount { get; }
    }

    internal static class MeshToVoxelModelAssetBuilder
    {
        public static MeshToVoxelModelAssetBuildResult Build(Mesh sourceMesh, MeshToVoxelModelAssetBuildOptions options)
        {
            if (sourceMesh == null)
            {
                throw new ArgumentNullException(nameof(sourceMesh));
            }

            MeshVoxelizationSettings settings = new MeshVoxelizationSettings(
                options.VoxelSize,
                options.SolidVoxelValue,
                VoxelMemoryLayout.Linear);
            MeshVoxelizationResult voxelizationResult = MeshVoxelizerGpu.Voxelize(sourceMesh, settings);
            ValidateVoxelizationResult(voxelizationResult);

            using EngineVoxelModel model = EngineVoxelModel.Create(Math.Max(1, voxelizationResult.ChunkCount), 1, Allocator.Temp);
            int occupiedVoxelCount = PopulateOpaqueVolume(model.OpaqueVolume, voxelizationResult, out Dictionary<int3, ChunkBuildState> chunkStates);
            int fallbackChunkCount = AllocateChunkAabbs(model.OpaqueVolume, chunkStates, options.MaxAabbsPerChunk);
            byte[] serializedBytes = VoxelModelSerializer.SerializeToBytes(model);

            return new MeshToVoxelModelAssetBuildResult(
                serializedBytes,
                chunkStates.Count,
                occupiedVoxelCount,
                fallbackChunkCount);
        }

        private static void ValidateVoxelizationResult(MeshVoxelizationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.MemoryLayout != VoxelMemoryLayout.Linear)
            {
                throw new InvalidOperationException($"Expected a linear voxelization result, but received {result.MemoryLayout}.");
            }

            if (result.ChunkCount <= 0)
            {
                throw new InvalidOperationException("Voxelization produced no chunks.");
            }

            if (result.ChunkCoordinates == null || result.ChunkCoordinates.Length != result.ChunkCount)
            {
                throw new InvalidOperationException("Voxelization chunk coordinates are missing or do not match the chunk count.");
            }

            int expectedOccupancyBytes = checked(result.ChunkCount * VoxelChunkLayout.OccupancyByteCount);
            if (result.OccupancyBytes == null || result.OccupancyBytes.Length != expectedOccupancyBytes)
            {
                throw new InvalidOperationException(
                    $"Voxelization occupancy bytes must be {expectedOccupancyBytes} for {result.ChunkCount} chunks.");
            }

            int expectedVoxelBytes = checked(result.ChunkCount * VoxelChunkLayout.VoxelDataByteCount);
            if (result.VoxelBytes == null || result.VoxelBytes.Length != expectedVoxelBytes)
            {
                throw new InvalidOperationException(
                    $"Voxelization voxel bytes must be {expectedVoxelBytes} for {result.ChunkCount} chunks.");
            }
        }

        private static int PopulateOpaqueVolume(
            VoxelVolume volume,
            MeshVoxelizationResult voxelizationResult,
            out Dictionary<int3, ChunkBuildState> chunkStates)
        {
            chunkStates = new Dictionary<int3, ChunkBuildState>(voxelizationResult.ChunkCount);
            int occupiedVoxelCount = 0;

            for (int chunkOrdinal = 0; chunkOrdinal < voxelizationResult.ChunkCount; chunkOrdinal++)
            {
                if (ShouldUpdateChunkProgress(chunkOrdinal, voxelizationResult.ChunkCount))
                {
                    int displayedChunkIndex = chunkOrdinal + 1;
                    bool canceled = EditorUtility.DisplayCancelableProgressBar(
                        "Mesh To VoxelModel Asset",
                        $"Packing chunk data {displayedChunkIndex:N0} / {voxelizationResult.ChunkCount:N0}",
                        Mathf.Clamp01(displayedChunkIndex / (float)voxelizationResult.ChunkCount));
                    if (canceled)
                    {
                        throw new OperationCanceledException("Mesh voxel packing was canceled.");
                    }
                }

                int3 chunkCoordinate = ToInt3(voxelizationResult.ChunkCoordinates[chunkOrdinal]);
                if (!volume.TryAllocateChunk(chunkCoordinate, out int chunkIndex))
                {
                    throw new InvalidOperationException($"Chunk {chunkCoordinate} was allocated more than once.");
                }

                var chunkState = new ChunkBuildState(chunkIndex);
                int occupancyBaseIndex = chunkOrdinal * VoxelChunkLayout.OccupancyByteCount;
                int voxelBaseIndex = chunkOrdinal * VoxelChunkLayout.VoxelDataByteCount;

                for (int z = 0; z < VoxelVolume.ChunkDimension; z++)
                {
                    for (int y = 0; y < VoxelVolume.ChunkDimension; y++)
                    {
                        for (int x = 0; x < VoxelVolume.ChunkDimension; x++)
                        {
                            int occupancyByteIndex = occupancyBaseIndex + VoxelChunkLayout.ComputeOccupancyByteIndex(VoxelMemoryLayout.Linear, x, y, z);
                            byte occupancyMask = VoxelChunkLayout.ComputeOccupancyBitMask(VoxelMemoryLayout.Linear, x, y, z);
                            if ((voxelizationResult.OccupancyBytes[occupancyByteIndex] & occupancyMask) == 0)
                            {
                                continue;
                            }

                            int voxelIndex = voxelBaseIndex + VoxelChunkLayout.FlattenVoxelDataIndex(VoxelMemoryLayout.Linear, x, y, z);
                            byte voxelValue = voxelizationResult.VoxelBytes[voxelIndex];
                            if (voxelValue == 0)
                            {
                                throw new InvalidOperationException(
                                    $"Chunk {chunkCoordinate} contains an occupied voxel with value 0 at ({x}, {y}, {z}).");
                            }

                            volume.SetVoxel(chunkIndex, x, y, z, voxelValue);
                            chunkState.IncludeVoxel(new int3(x, y, z));
                            occupiedVoxelCount++;
                        }
                    }
                }

                if (!chunkState.HasVoxels)
                {
                    throw new InvalidOperationException($"Chunk {chunkCoordinate} contains no occupied voxels.");
                }

                chunkStates.Add(chunkCoordinate, chunkState);
            }

            if (chunkStates.Count == 0)
            {
                throw new InvalidOperationException("Voxelization produced no occupied chunks.");
            }

            return occupiedVoxelCount;
        }

        private static int AllocateChunkAabbs(
            VoxelVolume volume,
            IReadOnlyDictionary<int3, ChunkBuildState> chunkStates,
            int maxAabbsPerChunk)
        {
            int fallbackChunkCount = 0;
            int processedChunkCount = 0;
            List<VoxelChunkAabb> allocatedAabbs = new List<VoxelChunkAabb>(VoxelVolume.MaxAabbsPerChunk);

            foreach (ChunkBuildState chunkState in chunkStates.Values)
            {
                processedChunkCount++;
                if (ShouldUpdateChunkProgress(processedChunkCount, chunkStates.Count))
                {
                    bool canceled = EditorUtility.DisplayCancelableProgressBar(
                        "Mesh To VoxelModel Asset",
                        $"Building chunk AABBs {processedChunkCount:N0} / {chunkStates.Count:N0}",
                        Mathf.Clamp01(processedChunkCount / (float)chunkStates.Count));
                    if (canceled)
                    {
                        throw new OperationCanceledException("Chunk AABB generation was canceled.");
                    }
                }

                VoxelChunkAabbOptimizer.BuildChunkAabbs(
                    chunkState,
                    maxAabbsPerChunk,
                    allocatedAabbs,
                    out bool usedFallbackBounds);
                if (allocatedAabbs.Count == 0)
                {
                    throw new InvalidOperationException($"Chunk {chunkState.ChunkIndex} produced no AABBs.");
                }

                for (int i = 0; i < allocatedAabbs.Count; i++)
                {
                    if (!volume.TryAllocateAabbSlot(chunkState.ChunkIndex, out int aabbIndex))
                    {
                        throw new InvalidOperationException($"Chunk {chunkState.ChunkIndex} has no free AABB slots.");
                    }

                    VoxelChunkAabb aabb = allocatedAabbs[i];
                    volume.SetAabb(chunkState.ChunkIndex, aabbIndex, aabb.Min, aabb.Max);
                }

                if (usedFallbackBounds)
                {
                    fallbackChunkCount++;
                }
            }

            return fallbackChunkCount;
        }

        private static bool ShouldUpdateChunkProgress(int processedCount, int totalCount)
        {
            if (totalCount <= 0)
            {
                return true;
            }

            if (processedCount <= 1 || processedCount >= totalCount)
            {
                return true;
            }

            return (processedCount % 32) == 0;
        }

        private static int3 ToInt3(Vector3Int value)
        {
            return new int3(value.x, value.y, value.z);
        }
    }
}
