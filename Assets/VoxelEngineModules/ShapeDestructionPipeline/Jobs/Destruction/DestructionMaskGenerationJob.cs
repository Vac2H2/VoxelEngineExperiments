using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngineModules.DestructionShape;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct DestructionMaskGenerationJob : IJobParallelFor
    {
        public const int TargetChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int TargetChunkSize = ShapeDataContainer.ChunkSize;
        public const int DestructionChunksPerShape = DestructionDataContainer.ChunksPerShape;
        public const int DestructionChunkSize = DestructionDataContainer.ChunkSize;
        public const int MaskBytesPerCommand = ShapeVoxelRemoveCommandBuffer.MaskBytesPerCommand;

        private const float BoundsEpsilon = 0.00001f;
        private const float DestructionVoxelSearchHalfExtent = 0.5f;

        [ReadOnly]
        public NativeArray<int> PairDestructionShapeHandles;

        [ReadOnly]
        public NativeArray<int> TargetShapeHandles;

        [ReadOnly]
        public NativeArray<byte> ChunkOverlapMasks;

        [ReadOnly]
        public NativeArray<int> ChunkOverlapOffsets;

        [ReadOnly]
        public NativeArray<float4x4> DestructionShapeLocalToWorlds;

        [ReadOnly]
        public NativeArray<float4x4> TargetShapeLocalToWorlds;

        [ReadOnly]
        public NativeArray<byte> DestructionMasks;

        [ReadOnly]
        public NativeArray<int3> DestructionChunkPositions;

        [ReadOnly]
        public NativeArray<byte> TargetIsOccupied;

        [ReadOnly]
        public NativeArray<int3> TargetChunkPositions;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<int> CommandShapeHandles;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> CommandChunkSlots;

        [NativeDisableParallelForRestriction]
        public NativeArray<byte> CommandMasks;

        public void Execute(int chunkOverlapIndex)
        {
            byte destructionChunkMask = ChunkOverlapMasks[chunkOverlapIndex];
            if (destructionChunkMask == 0)
            {
                return;
            }

            int pairIndex = chunkOverlapIndex / TargetChunksPerShape;
            int targetChunkSlot = chunkOverlapIndex - pairIndex * TargetChunksPerShape;
            int commandIndex = ChunkOverlapOffsets[chunkOverlapIndex];

            int destructionShapeHandle = PairDestructionShapeHandles[pairIndex];
            int targetShapeHandle = TargetShapeHandles[pairIndex];
            int destructionChunkBase = destructionShapeHandle * DestructionChunksPerShape;
            int targetChunkIndex = targetShapeHandle * TargetChunksPerShape + targetChunkSlot;

            float4x4 targetLocalToWorld = TargetShapeLocalToWorlds[targetShapeHandle];
            float4x4 destructionWorldToLocal = math.inverse(DestructionShapeLocalToWorlds[destructionShapeHandle]);
            float4x4 targetLocalToDestructionLocal = math.mul(
                destructionWorldToLocal,
                targetLocalToWorld);

            FixedList32Bytes<byte> destructionChunkSlots = default;
            for (int destructionChunkSlot = 0; destructionChunkSlot < DestructionChunksPerShape; destructionChunkSlot++)
            {
                if ((destructionChunkMask & (1 << destructionChunkSlot)) == 0)
                {
                    continue;
                }

                destructionChunkSlots.Add((byte)destructionChunkSlot);
            }

            WriteCommandMasks(
                commandIndex,
                targetShapeHandle,
                targetChunkSlot,
                targetChunkIndex,
                destructionChunkBase,
                destructionChunkSlots,
                targetLocalToDestructionLocal);
        }

        private void WriteCommandMasks(
            int firstCommandIndex,
            int targetShapeHandle,
            int targetChunkSlot,
            int targetChunkIndex,
            int destructionChunkBase,
            FixedList32Bytes<byte> destructionChunkSlots,
            float4x4 targetLocalToDestructionLocal)
        {
            int targetMaskBase = targetChunkIndex * ShapeDataContainer.BitPlaneBytesPerChunk;
            int3 targetChunkPosition = TargetChunkPositions[targetChunkIndex];
            int3 targetChunkOrigin = targetChunkPosition * TargetChunkSize;
            int commandCount = destructionChunkSlots.Length;

            for (int commandOffset = 0; commandOffset < commandCount; commandOffset++)
            {
                int outputCommandIndex = firstCommandIndex + commandOffset;
                CommandShapeHandles[outputCommandIndex] = targetShapeHandle;
                CommandChunkSlots[outputCommandIndex] = (byte)targetChunkSlot;
            }

            for (int z = 0; z < TargetChunkSize; z++)
            {
                for (int y = 0; y < TargetChunkSize; y++)
                {
                    int rowIndex = RowIndex(y, z);
                    byte targetRow = TargetIsOccupied[targetMaskBase + rowIndex];

                    for (int commandOffset = 0; commandOffset < commandCount; commandOffset++)
                    {
                        int outputCommandIndex = firstCommandIndex + commandOffset;
                        CommandMasks[outputCommandIndex * MaskBytesPerCommand + rowIndex] = 0xFF;
                    }

                    if (targetRow != 0)
                    {
                        for (int x = 0; x < TargetChunkSize; x++)
                        {
                            byte bit = (byte)(1 << x);
                            if ((targetRow & bit) == 0)
                            {
                                continue;
                            }

                            int3 targetVoxelMin = targetChunkOrigin + new int3(x, y, z);
                            float3 destructionLocalPoint = math.transform(
                                targetLocalToDestructionLocal,
                                new float3(targetVoxelMin) + new float3(0.5f, 0.5f, 0.5f));

                            for (int commandOffset = 0; commandOffset < commandCount; commandOffset++)
                            {
                                int destructionChunkSlot = destructionChunkSlots[commandOffset];
                                int destructionChunkIndex = destructionChunkBase + destructionChunkSlot;
                                int destructionMaskBase =
                                    destructionChunkIndex * DestructionDataContainer.MaskBytesPerChunk;

                                if (!DestructionPointOverlapsMask(
                                        destructionLocalPoint,
                                        DestructionChunkPositions[destructionChunkIndex],
                                        destructionMaskBase))
                                {
                                    continue;
                                }

                                int outputCommandIndex = firstCommandIndex + commandOffset;
                                int outputRowIndex = outputCommandIndex * MaskBytesPerCommand + rowIndex;
                                CommandMasks[outputRowIndex] =
                                    (byte)(CommandMasks[outputRowIndex] & ~bit);
                            }
                        }
                    }
                }
            }
        }

        private bool DestructionPointOverlapsMask(
            float3 destructionLocalPoint,
            int3 destructionChunkPosition,
            int destructionMaskBase)
        {
            float3 destructionChunkOrigin = new float3(destructionChunkPosition * DestructionChunkSize);
            float3 destructionChunkLocal = destructionLocalPoint - destructionChunkOrigin;
            float3 searchMin = destructionChunkLocal - new float3(DestructionVoxelSearchHalfExtent);
            float3 searchMax = destructionChunkLocal + new float3(DestructionVoxelSearchHalfExtent);

            if (math.any(searchMax < float3.zero) ||
                math.any(searchMin >= new float3(DestructionChunkSize)))
            {
                return false;
            }

            int3 minVoxel = math.clamp(
                new int3(
                    (int)math.floor(searchMin.x + BoundsEpsilon),
                    (int)math.floor(searchMin.y + BoundsEpsilon),
                    (int)math.floor(searchMin.z + BoundsEpsilon)),
                int3.zero,
                new int3(DestructionChunkSize - 1));
            int3 maxVoxel = math.clamp(
                new int3(
                    (int)math.floor(searchMax.x - BoundsEpsilon),
                    (int)math.floor(searchMax.y - BoundsEpsilon),
                    (int)math.floor(searchMax.z - BoundsEpsilon)),
                int3.zero,
                new int3(DestructionChunkSize - 1));

            if (math.any(maxVoxel < minVoxel))
            {
                return false;
            }

            for (int z = minVoxel.z; z <= maxVoxel.z; z++)
            {
                for (int y = minVoxel.y; y <= maxVoxel.y; y++)
                {
                    int rowIndex = RowIndex(y, z);
                    byte destructionRow = DestructionMasks[destructionMaskBase + rowIndex];
                    if (destructionRow == 0)
                    {
                        continue;
                    }

                    for (int x = minVoxel.x; x <= maxVoxel.x; x++)
                    {
                        byte bit = (byte)(1 << x);
                        if ((destructionRow & bit) != 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static int RowIndex(int y, int z)
        {
            return y + TargetChunkSize * z;
        }

        public static DestructionMaskGenerationJob Create(
            NativeArray<int> pairDestructionShapeHandles,
            NativeArray<int> targetShapeHandles,
            NativeArray<byte> chunkOverlapMasks,
            NativeArray<int> chunkOverlapOffsets,
            NativeArray<float4x4> destructionShapeLocalToWorlds,
            NativeArray<float4x4> targetShapeLocalToWorlds,
            NativeArray<byte> destructionMasks,
            NativeArray<int3> destructionChunkPositions,
            NativeArray<byte> targetIsOccupied,
            NativeArray<int3> targetChunkPositions,
            ShapeVoxelRemoveCommandBuffer commandBuffer)
        {
            return new DestructionMaskGenerationJob
            {
                PairDestructionShapeHandles = pairDestructionShapeHandles,
                TargetShapeHandles = targetShapeHandles,
                ChunkOverlapMasks = chunkOverlapMasks,
                ChunkOverlapOffsets = chunkOverlapOffsets,
                DestructionShapeLocalToWorlds = destructionShapeLocalToWorlds,
                TargetShapeLocalToWorlds = targetShapeLocalToWorlds,
                DestructionMasks = destructionMasks,
                DestructionChunkPositions = destructionChunkPositions,
                TargetIsOccupied = targetIsOccupied,
                TargetChunkPositions = targetChunkPositions,
                CommandShapeHandles = commandBuffer.ShapeHandles,
                CommandChunkSlots = commandBuffer.ChunkSlots,
                CommandMasks = commandBuffer.Masks
            };
        }
    }
}
