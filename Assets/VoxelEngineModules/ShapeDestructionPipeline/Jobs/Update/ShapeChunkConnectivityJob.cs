using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapeChunkConnectivityJob : IJobParallelFor
    {
        public const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int MaxFragmentsPerChunk = ShapeChunkFragmentJob.MaxGeneratedChunksPerChunk;
        public const int MaxFragmentNodesPerShape = ChunksPerShape * MaxFragmentsPerChunk;
        public const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;
        public const int PositiveDirectionCount = ShapeChunkCheckMaskJob.PositiveDirectionCount;
        public const int PositiveXDirection = ShapeChunkCheckMaskJob.PositiveXDirection;
        public const int PositiveYDirection = ShapeChunkCheckMaskJob.PositiveYDirection;
        public const int PositiveZDirection = ShapeChunkCheckMaskJob.PositiveZDirection;

        private const int ChunkSize = ShapeDataContainer.ChunkSize;
        private const byte NegativeXMask = 1 << 0;
        private const byte PositiveXMask = 1 << 7;

        [ReadOnly]
        public NativeArray<byte> FragmentIsOccupied;

        [ReadOnly]
        public NativeArray<byte> FragmentCounts;

        [ReadOnly]
        public NativeArray<uint> FragmentCheckMasks;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> FragmentConnectionMasks;

        public void Execute(int index)
        {
            int shapeListIndex = index / MaxFragmentNodesPerShape;
            int nodeA = index - shapeListIndex * MaxFragmentNodesPerShape;
            int outputBase = CheckMaskBase(shapeListIndex, nodeA);

            FragmentConnectionMasks[outputBase + PositiveXDirection] = 0u;
            FragmentConnectionMasks[outputBase + PositiveYDirection] = 0u;
            FragmentConnectionMasks[outputBase + PositiveZDirection] = 0u;

            if (!IsValidNode(shapeListIndex, nodeA))
            {
                return;
            }

            WriteDirectionConnectionMask(shapeListIndex, nodeA, PositiveXDirection);
            WriteDirectionConnectionMask(shapeListIndex, nodeA, PositiveYDirection);
            WriteDirectionConnectionMask(shapeListIndex, nodeA, PositiveZDirection);
        }

        private void WriteDirectionConnectionMask(
            int shapeListIndex,
            int nodeA,
            int direction)
        {
            int maskIndex = CheckMaskIndex(shapeListIndex, nodeA, direction);
            uint candidateMask = FragmentCheckMasks[maskIndex];
            uint connectionMask = 0u;

            while (candidateMask != 0u)
            {
                int nodeB = math.tzcnt(candidateMask);
                candidateMask &= candidateMask - 1u;

                if (IsValidNode(shapeListIndex, nodeB) &&
                    HasFaceConnection(shapeListIndex, nodeA, nodeB, direction))
                {
                    connectionMask |= 1u << nodeB;
                }
            }

            FragmentConnectionMasks[maskIndex] = connectionMask;
        }

        private bool HasFaceConnection(
            int shapeListIndex,
            int nodeA,
            int nodeB,
            int direction)
        {
            int fragmentBaseA = FragmentMaskBase(shapeListIndex, nodeA);
            int fragmentBaseB = FragmentMaskBase(shapeListIndex, nodeB);

            if (direction == PositiveXDirection)
            {
                return HasXFaceConnection(fragmentBaseA, fragmentBaseB);
            }

            if (direction == PositiveYDirection)
            {
                return HasYFaceConnection(fragmentBaseA, fragmentBaseB);
            }

            return HasZFaceConnection(fragmentBaseA, fragmentBaseB);
        }

        private bool HasXFaceConnection(int fragmentBaseA, int fragmentBaseB)
        {
            for (int row = 0; row < BitPlaneBytesPerChunk; row++)
            {
                if ((FragmentIsOccupied[fragmentBaseA + row] & PositiveXMask) != 0 &&
                    (FragmentIsOccupied[fragmentBaseB + row] & NegativeXMask) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasYFaceConnection(int fragmentBaseA, int fragmentBaseB)
        {
            for (int z = 0; z < ChunkSize; z++)
            {
                if ((FragmentIsOccupied[fragmentBaseA + RowIndex(ChunkSize - 1, z)] &
                     FragmentIsOccupied[fragmentBaseB + RowIndex(0, z)]) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasZFaceConnection(int fragmentBaseA, int fragmentBaseB)
        {
            for (int y = 0; y < ChunkSize; y++)
            {
                if ((FragmentIsOccupied[fragmentBaseA + RowIndex(y, ChunkSize - 1)] &
                     FragmentIsOccupied[fragmentBaseB + RowIndex(y, 0)]) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsValidNode(int shapeListIndex, int node)
        {
            if ((uint)node >= MaxFragmentNodesPerShape)
            {
                return false;
            }

            int chunkSlot = node / MaxFragmentsPerChunk;
            int fragmentSlot = node - chunkSlot * MaxFragmentsPerChunk;
            int fragmentCount = GetFragmentCount(shapeListIndex, chunkSlot);
            return fragmentSlot < fragmentCount;
        }

        private int GetFragmentCount(int shapeListIndex, int chunkSlot)
        {
            int shapeChunkIndex = shapeListIndex * ChunksPerShape + chunkSlot;
            return math.min(FragmentCounts[shapeChunkIndex], MaxFragmentsPerChunk);
        }

        public static int CheckMaskIndex(int shapeListIndex, int node, int direction)
        {
            return CheckMaskBase(shapeListIndex, node) + direction;
        }

        private static int CheckMaskBase(int shapeListIndex, int node)
        {
            return (shapeListIndex * MaxFragmentNodesPerShape + node) *
                   PositiveDirectionCount;
        }

        private static int FragmentMaskBase(int shapeListIndex, int node)
        {
            return (shapeListIndex * MaxFragmentNodesPerShape + node) *
                   BitPlaneBytesPerChunk;
        }

        private static int RowIndex(int y, int z)
        {
            return y + ChunkSize * z;
        }
    }
}
