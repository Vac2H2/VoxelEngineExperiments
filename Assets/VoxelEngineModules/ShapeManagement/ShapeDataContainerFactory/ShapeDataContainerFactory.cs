using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace VoxelEngineModules.ShapeManagement
{
    public sealed class ShapeDataContainerFactory
    {
        public ShapeDataContainer Acquire(
            NativeArray<byte> chunkData,
            NativeArray<int3> chunkPositions)
        {
            ValidateInputs(chunkData, chunkPositions);

            ShapeDataContainer container =
                new ShapeDataContainer(chunkPositions.Length);
            try
            {
                InitializeChunks(container.Chunks, chunkData, chunkPositions);
                return container;
            }
            catch
            {
                container.Dispose();
                throw;
            }
        }

        public void Release(ShapeDataContainer container)
        {
            if (container == null)
            {
                return;
            }

            container.Dispose();
        }


        #region Helpers

        private static void InitializeChunks(
            ChunkDataContainer chunks,
            NativeArray<byte> chunkData,
            NativeArray<int3> chunkPositions)
        {
            NativeArray<byte> isOccupied = chunks.IsOccupied;
            NativeArray<int3> positions = chunks.Positions;
            NativeArray<byte> used = chunks.Used;
            NativeParallelHashMap<int3, int> indexByPosition =
                chunks.IndexByPosition;

            NativeArray<byte>.Copy(
                chunkData,
                0,
                isOccupied,
                0,
                chunkData.Length);
            NativeArray<int3>.Copy(
                chunkPositions,
                0,
                positions,
                0,
                chunkPositions.Length);
            MarkChunksUsed(used, chunkPositions.Length);

            for (int chunkIndex = 0; chunkIndex < chunkPositions.Length; chunkIndex++)
            {
                int3 chunkPosition = chunkPositions[chunkIndex];
                if (!indexByPosition.TryAdd(chunkPosition, chunkIndex))
                {
                    throw new InvalidOperationException(
                        "Chunk positions must be unique inside one ShapeDataContainer.");
                }
            }
        }

        private static unsafe void MarkChunksUsed(
            NativeArray<byte> used,
            int chunkCount)
        {
            UnsafeUtility.MemSet(
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(used),
                1,
                chunkCount);
        }

        private static void ValidateInputs(
            NativeArray<byte> chunkData,
            NativeArray<int3> chunkPositions)
        {
            if (!chunkData.IsCreated)
            {
                throw new ArgumentException(
                    "Chunk data must be created.",
                    nameof(chunkData));
            }

            if (!chunkPositions.IsCreated)
            {
                throw new ArgumentException(
                    "Chunk positions must be created.",
                    nameof(chunkPositions));
            }

            ShapeDataContainer.ValidateChunkCapacity(chunkPositions.Length);

            int expectedChunkDataLength = checked(
                chunkPositions.Length *
                ShapeDataContainer.BitPlaneBytesPerChunk);
            if (chunkData.Length != expectedChunkDataLength)
            {
                throw new ArgumentException(
                    "Chunk data length must equal chunk position count times bit-plane bytes per chunk.",
                    nameof(chunkData));
            }
        }

        #endregion
    }
}
