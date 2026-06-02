using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapeChunkCheckMaskJob : IJobParallelFor
    {
        public const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int MaxFragmentsPerChunk = ShapeChunkFragmentJob.MaxGeneratedChunksPerChunk;
        public const int MaxFragmentNodesPerShape = ChunksPerShape * MaxFragmentsPerChunk;
        public const int PositiveDirectionCount = 3;
        public const int PositiveXDirection = 0;
        public const int PositiveYDirection = 1;
        public const int PositiveZDirection = 2;
        public const int InvalidChunkSlot = -1;

        [ReadOnly]
        public NativeArray<int> ShapeHandles;

        [ReadOnly]
        public NativeArray<int3> ChunkPositions;

        [ReadOnly]
        public NativeArray<byte> ChunkUsed;

        [ReadOnly]
        public NativeArray<byte> FragmentCounts;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> FragmentCheckMasks;

        public void Execute(int shapeListIndex)
        {
            int shapeHandle = ShapeHandles[shapeListIndex];
            int outputBase = shapeListIndex * MaxFragmentNodesPerShape * PositiveDirectionCount;
            InitializeOutput(outputBase);

            ChunkNeighborSlots neighborSlots = BuildNeighborSlots(shapeHandle);

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = shapeHandle * ChunksPerShape + chunkSlot;
                if (ChunkUsed[chunkMetadataIndex] == 0)
                {
                    continue;
                }

                int fragmentCount = GetFragmentCount(shapeListIndex, chunkSlot);
                if (fragmentCount == 0)
                {
                    continue;
                }

                int3 positiveNeighbors = neighborSlots.Get(chunkSlot);
                uint positiveXMask = CreateNeighborFragmentMask(shapeListIndex, positiveNeighbors.x);
                uint positiveYMask = CreateNeighborFragmentMask(shapeListIndex, positiveNeighbors.y);
                uint positiveZMask = CreateNeighborFragmentMask(shapeListIndex, positiveNeighbors.z);

                for (int fragmentSlot = 0; fragmentSlot < fragmentCount; fragmentSlot++)
                {
                    int node = NodeIndex(chunkSlot, fragmentSlot);
                    int nodeOutputBase = outputBase + node * PositiveDirectionCount;
                    FragmentCheckMasks[nodeOutputBase + PositiveXDirection] = positiveXMask;
                    FragmentCheckMasks[nodeOutputBase + PositiveYDirection] = positiveYMask;
                    FragmentCheckMasks[nodeOutputBase + PositiveZDirection] = positiveZMask;
                }
            }
        }

        private void InitializeOutput(int outputBase)
        {
            for (int i = 0; i < MaxFragmentNodesPerShape * PositiveDirectionCount; i++)
            {
                FragmentCheckMasks[outputBase + i] = 0u;
            }
        }

        private ChunkNeighborSlots BuildNeighborSlots(int shapeHandle)
        {
            ChunkNeighborSlots result = default;
            int chunkBase = shapeHandle * ChunksPerShape;

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = chunkBase + chunkSlot;
                if (ChunkUsed[chunkMetadataIndex] == 0)
                {
                    result.Set(
                        chunkSlot,
                        new int3(InvalidChunkSlot, InvalidChunkSlot, InvalidChunkSlot));
                    continue;
                }

                int3 chunkPosition = ChunkPositions[chunkMetadataIndex];
                result.Set(
                    chunkSlot,
                    new int3(
                        FindChunkSlot(chunkBase, chunkPosition + new int3(1, 0, 0)),
                        FindChunkSlot(chunkBase, chunkPosition + new int3(0, 1, 0)),
                        FindChunkSlot(chunkBase, chunkPosition + new int3(0, 0, 1))));
            }

            return result;
        }

        private int FindChunkSlot(int chunkBase, int3 chunkPosition)
        {
            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = chunkBase + chunkSlot;
                if (ChunkUsed[chunkMetadataIndex] != 0 &&
                    math.all(ChunkPositions[chunkMetadataIndex] == chunkPosition))
                {
                    return chunkSlot;
                }
            }

            return InvalidChunkSlot;
        }

        private uint CreateNeighborFragmentMask(int shapeListIndex, int chunkSlot)
        {
            if ((uint)chunkSlot >= ChunksPerShape)
            {
                return 0u;
            }

            uint mask = 0u;
            int fragmentCount = GetFragmentCount(shapeListIndex, chunkSlot);
            for (int fragmentSlot = 0; fragmentSlot < fragmentCount; fragmentSlot++)
            {
                mask |= 1u << NodeIndex(chunkSlot, fragmentSlot);
            }

            return mask;
        }

        private int GetFragmentCount(int shapeListIndex, int chunkSlot)
        {
            int shapeChunkIndex = shapeListIndex * ChunksPerShape + chunkSlot;
            return math.min(FragmentCounts[shapeChunkIndex], MaxFragmentsPerChunk);
        }

        public static int CheckMaskIndex(int shapeListIndex, int node, int direction)
        {
            return (shapeListIndex * MaxFragmentNodesPerShape + node) *
                   PositiveDirectionCount +
                   direction;
        }

        public static int NodeIndex(int chunkSlot, int fragmentSlot)
        {
            return chunkSlot * MaxFragmentsPerChunk + fragmentSlot;
        }

        private struct ChunkNeighborSlots
        {
            private int3 _slot0;
            private int3 _slot1;
            private int3 _slot2;
            private int3 _slot3;
            private int3 _slot4;
            private int3 _slot5;
            private int3 _slot6;
            private int3 _slot7;

            public int3 Get(int chunkSlot)
            {
                switch (chunkSlot)
                {
                    case 0:
                        return _slot0;
                    case 1:
                        return _slot1;
                    case 2:
                        return _slot2;
                    case 3:
                        return _slot3;
                    case 4:
                        return _slot4;
                    case 5:
                        return _slot5;
                    case 6:
                        return _slot6;
                    default:
                        return _slot7;
                }
            }

            public void Set(int chunkSlot, int3 value)
            {
                switch (chunkSlot)
                {
                    case 0:
                        _slot0 = value;
                        break;
                    case 1:
                        _slot1 = value;
                        break;
                    case 2:
                        _slot2 = value;
                        break;
                    case 3:
                        _slot3 = value;
                        break;
                    case 4:
                        _slot4 = value;
                        break;
                    case 5:
                        _slot5 = value;
                        break;
                    case 6:
                        _slot6 = value;
                        break;
                    default:
                        _slot7 = value;
                        break;
                }
            }
        }
    }
}
