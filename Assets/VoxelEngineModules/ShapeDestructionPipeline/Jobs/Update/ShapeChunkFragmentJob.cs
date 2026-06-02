using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapeChunkFragmentJob : IJobParallelFor
    {
        public const int MaxGeneratedChunksPerChunk = 4;
        public const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;
        public const int MaxSliceCount = BitPlaneBytesPerChunk * (ShapeDataContainer.ChunkSize / 2);

        private const int ChunkSize = ShapeDataContainer.ChunkSize;
        private const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;

        [ReadOnly]
        public NativeArray<int> ShapeHandles;

        [ReadOnly]
        public NativeArray<byte> ChunkUsed;

        [ReadOnly]
        public NativeArray<byte> SourceIsOccupied;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> SliceQueue;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> OutputIsOccupied;

        [WriteOnly]
        public NativeArray<byte> OutputGeneratedChunkCounts;

        public int MinimalVoxelNumber;

        public void Execute(int index)
        {
            int shapeListIndex = index / ChunksPerShape;
            int chunkIndexInShape = index - shapeListIndex * ChunksPerShape;
            int shapeHandle = ShapeHandles[shapeListIndex];
            int chunkMetadataIndex = shapeHandle * ChunksPerShape + chunkIndexInShape;

            if (ChunkUsed[chunkMetadataIndex] == 0)
            {
                OutputGeneratedChunkCounts[index] = 0;
                return;
            }

            int sourceBase = index * BitPlaneBytesPerChunk;
            int sliceQueueBase = index * MaxSliceCount;
            ChunkBitMask visited = default;

            ChunkBitMask fragment0 = default;
            ChunkBitMask fragment1 = default;
            ChunkBitMask fragment2 = default;
            ChunkBitMask fragment3 = default;
            int voxelCount0 = -1;
            int voxelCount1 = -1;
            int voxelCount2 = -1;
            int voxelCount3 = -1;
            int generatedChunkCount = 0;

            for (int z = 0; z < ChunkSize; z++)
            {
                for (int y = 0; y < ChunkSize; y++)
                {
                    int rowIndex = RowIndex(y, z);
                    byte sourceRow = SourceIsOccupied[sourceBase + rowIndex];
                    byte remaining = (byte)(sourceRow & ~visited.GetRow(y, z));

                    while (remaining != 0)
                    {
                        int x = math.tzcnt((uint)remaining);
                        byte runMask = GetSourceRunMaskContaining(sourceRow, x);
                        ChunkBitMask fragment = FloodFillSlice(
                            sourceBase,
                            sliceQueueBase,
                            rowIndex,
                            runMask,
                            ref visited,
                            out int voxelCount);

                        if (voxelCount >= MinimalVoxelNumber)
                        {
                            InsertGeneratedChunk(
                                fragment,
                                voxelCount,
                                ref fragment0,
                                ref fragment1,
                                ref fragment2,
                                ref fragment3,
                                ref voxelCount0,
                                ref voxelCount1,
                                ref voxelCount2,
                                ref voxelCount3,
                                ref generatedChunkCount);
                        }

                        remaining = (byte)(sourceRow & ~visited.GetRow(y, z));
                    }
                }
            }

            WriteGeneratedChunks(
                index,
                fragment0,
                fragment1,
                fragment2,
                fragment3,
                generatedChunkCount);
        }


        #region Helpers

        private void WriteGeneratedChunks(
            int inputChunkIndex,
            ChunkBitMask fragment0,
            ChunkBitMask fragment1,
            ChunkBitMask fragment2,
            ChunkBitMask fragment3,
            int generatedChunkCount)
        {
            int outputChunkBase = inputChunkIndex * MaxGeneratedChunksPerChunk;

            OutputGeneratedChunkCounts[inputChunkIndex] = (byte)generatedChunkCount;
            WriteGeneratedChunk(outputChunkBase, fragment0);
            WriteGeneratedChunk(outputChunkBase + 1, fragment1);
            WriteGeneratedChunk(outputChunkBase + 2, fragment2);
            WriteGeneratedChunk(outputChunkBase + 3, fragment3);
        }

        private void WriteGeneratedChunk(
            int outputChunkIndex,
            ChunkBitMask fragment)
        {
            int outputBase = outputChunkIndex * BitPlaneBytesPerChunk;

            fragment.CopyRowsTo(OutputIsOccupied, outputBase);
        }

        private static void InsertGeneratedChunk(
            ChunkBitMask fragment,
            int voxelCount,
            ref ChunkBitMask fragment0,
            ref ChunkBitMask fragment1,
            ref ChunkBitMask fragment2,
            ref ChunkBitMask fragment3,
            ref int voxelCount0,
            ref int voxelCount1,
            ref int voxelCount2,
            ref int voxelCount3,
            ref int generatedChunkCount)
        {
            int nextGeneratedChunkCount = math.min(
                generatedChunkCount + 1,
                MaxGeneratedChunksPerChunk);

            if (voxelCount > voxelCount0)
            {
                fragment3 = fragment2;
                voxelCount3 = voxelCount2;
                fragment2 = fragment1;
                voxelCount2 = voxelCount1;
                fragment1 = fragment0;
                voxelCount1 = voxelCount0;
                fragment0 = fragment;
                voxelCount0 = voxelCount;
                generatedChunkCount = nextGeneratedChunkCount;
                return;
            }

            if (voxelCount > voxelCount1)
            {
                fragment3 = fragment2;
                voxelCount3 = voxelCount2;
                fragment2 = fragment1;
                voxelCount2 = voxelCount1;
                fragment1 = fragment;
                voxelCount1 = voxelCount;
                generatedChunkCount = nextGeneratedChunkCount;
                return;
            }

            if (voxelCount > voxelCount2)
            {
                fragment3 = fragment2;
                voxelCount3 = voxelCount2;
                fragment2 = fragment;
                voxelCount2 = voxelCount;
                generatedChunkCount = nextGeneratedChunkCount;
                return;
            }

            if (voxelCount > voxelCount3)
            {
                fragment3 = fragment;
                voxelCount3 = voxelCount;
                generatedChunkCount = nextGeneratedChunkCount;
            }
        }

        private ChunkBitMask FloodFillSlice(
            int sourceBase,
            int sliceQueueBase,
            int rowIndex,
            byte startRunMask,
            ref ChunkBitMask visited,
            out int voxelCount)
        {
            ChunkBitMask fragment = default;
            int read = 0;
            int write = 0;
            voxelCount = 0;

            EnqueueSlice(
                sliceQueueBase,
                rowIndex,
                startRunMask,
                ref write,
                ref visited,
                ref fragment,
                ref voxelCount);

            while (read < write)
            {
                int current = SliceQueue[sliceQueueBase + read++];
                int currentRowIndex = SliceRowIndex(current);
                byte currentRunMask = SliceRunMask(current);
                int y = currentRowIndex & 7;
                int z = currentRowIndex >> 3;

                TryAddNeighborSlices(
                    sourceBase,
                    sliceQueueBase,
                    y - 1,
                    z,
                    currentRunMask,
                    ref write,
                    ref visited,
                    ref fragment,
                    ref voxelCount);

                TryAddNeighborSlices(
                    sourceBase,
                    sliceQueueBase,
                    y + 1,
                    z,
                    currentRunMask,
                    ref write,
                    ref visited,
                    ref fragment,
                    ref voxelCount);

                TryAddNeighborSlices(
                    sourceBase,
                    sliceQueueBase,
                    y,
                    z - 1,
                    currentRunMask,
                    ref write,
                    ref visited,
                    ref fragment,
                    ref voxelCount);

                TryAddNeighborSlices(
                    sourceBase,
                    sliceQueueBase,
                    y,
                    z + 1,
                    currentRunMask,
                    ref write,
                    ref visited,
                    ref fragment,
                    ref voxelCount);
            }

            return fragment;
        }

        private void TryAddNeighborSlices(
            int sourceBase,
            int sliceQueueBase,
            int y,
            int z,
            byte currentRunMask,
            ref int write,
            ref ChunkBitMask visited,
            ref ChunkBitMask fragment,
            ref int voxelCount)
        {
            if ((uint)y >= ChunkSize || (uint)z >= ChunkSize)
            {
                return;
            }

            int rowIndex = RowIndex(y, z);
            byte sourceRow = SourceIsOccupied[sourceBase + rowIndex];
            byte unvisitedRow = (byte)(sourceRow & ~visited.GetRow(y, z));
            byte overlappingUnvisited = (byte)(unvisitedRow & currentRunMask);

            while (overlappingUnvisited != 0)
            {
                int x = math.tzcnt((uint)overlappingUnvisited);
                byte runMask = GetSourceRunMaskContaining(sourceRow, x);

                EnqueueSlice(
                    sliceQueueBase,
                    rowIndex,
                    runMask,
                    ref write,
                    ref visited,
                    ref fragment,
                    ref voxelCount);

                overlappingUnvisited = (byte)(overlappingUnvisited & ~runMask);
            }
        }

        private void EnqueueSlice(
            int sliceQueueBase,
            int rowIndex,
            byte runMask,
            ref int write,
            ref ChunkBitMask visited,
            ref ChunkBitMask fragment,
            ref int voxelCount)
        {
            int y = rowIndex & 7;
            int z = rowIndex >> 3;
            byte visitedRow = visited.GetRow(y, z);
            byte fragmentRow = fragment.GetRow(y, z);
            byte newBits = (byte)(runMask & ~visitedRow);

            if (newBits == 0)
            {
                return;
            }

            SliceQueue[sliceQueueBase + write++] = PackSlice(rowIndex, runMask);

            visited.SetRow(y, z, (byte)(visitedRow | runMask));
            fragment.SetRow(y, z, (byte)(fragmentRow | runMask));
            voxelCount += math.countbits((uint)newBits);
        }

        private static byte GetSourceRunMaskContaining(byte sourceRow, int x)
        {
            uint row = sourceRow;
            uint zeroMask = (~row) & 0xFFu;

            uint leftZeros = zeroMask & ((1u << x) - 1u);
            int start = leftZeros == 0u ? 0 : math.floorlog2(leftZeros) + 1;

            uint rightZeros = zeroMask & (0xFFu & ~((1u << (x + 1)) - 1u));
            int end = rightZeros == 0u ? ChunkSize : math.tzcnt(rightZeros);

            return CreateRunMask(start, end);
        }

        private static byte CreateRunMask(int startInclusive, int endExclusive)
        {
            int width = endExclusive - startInclusive;
            int mask = ((1 << width) - 1) << startInclusive;
            return (byte)mask;
        }

        private static int RowIndex(int y, int z)
        {
            return y + ChunkSize * z;
        }

        private static int PackSlice(int rowIndex, byte runMask)
        {
            return (rowIndex << 8) | runMask;
        }

        private static int SliceRowIndex(int packed)
        {
            return packed >> 8;
        }

        private static byte SliceRunMask(int packed)
        {
            return (byte)packed;
        }

        #endregion


        private struct ChunkBitMask
        {
            public ulong Z0;
            public ulong Z1;
            public ulong Z2;
            public ulong Z3;
            public ulong Z4;
            public ulong Z5;
            public ulong Z6;
            public ulong Z7;

            public byte GetRow(int y, int z)
            {
                return (byte)(GetLayer(z) >> (y * ChunkSize));
            }

            public void SetRow(int y, int z, byte value)
            {
                ulong clearMask = ~(0xFFUL << (y * ChunkSize));
                ulong layer = (GetLayer(z) & clearMask) | ((ulong)value << (y * ChunkSize));
                SetLayer(z, layer);
            }

            public void CopyRowsTo(
                NativeArray<byte> destinationRows,
                int destinationOffset)
            {
                for (int z = 0; z < ChunkSize; z++)
                {
                    for (int y = 0; y < ChunkSize; y++)
                    {
                        destinationRows[destinationOffset + y + ChunkSize * z] = GetRow(y, z);
                    }
                }
            }

            private ulong GetLayer(int z)
            {
                switch (z)
                {
                    case 0:
                        return Z0;
                    case 1:
                        return Z1;
                    case 2:
                        return Z2;
                    case 3:
                        return Z3;
                    case 4:
                        return Z4;
                    case 5:
                        return Z5;
                    case 6:
                        return Z6;
                    default:
                        return Z7;
                }
            }

            private void SetLayer(int z, ulong value)
            {
                switch (z)
                {
                    case 0:
                        Z0 = value;
                        break;
                    case 1:
                        Z1 = value;
                        break;
                    case 2:
                        Z2 = value;
                        break;
                    case 3:
                        Z3 = value;
                        break;
                    case 4:
                        Z4 = value;
                        break;
                    case 5:
                        Z5 = value;
                        break;
                    case 6:
                        Z6 = value;
                        break;
                    default:
                        Z7 = value;
                        break;
                }
            }
        }
    }
}
