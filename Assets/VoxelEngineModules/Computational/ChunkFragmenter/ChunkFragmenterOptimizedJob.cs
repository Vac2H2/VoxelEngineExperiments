using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace AlgorithmTesting
{
    [BurstCompile]
    public struct ChunkFragmenterOptimizedJob : IJob
    {
        [ReadOnly]
        public NativeArray<byte> SourceRows;

        public int SourceOffset;
        public NativeList<ChunkFragmenter.IslandMask> Islands;
        public NativeArray<int> SliceQueue;

        public void Execute()
        {
            Islands.Clear();

            ChunkFragmenter.IslandMask visited = default;

            for (int z = 0; z < ChunkFragmenter.ChunkSize; z++)
            {
                for (int y = 0; y < ChunkFragmenter.ChunkSize; y++)
                {
                    byte sourceRow = SourceRows[SourceOffset + ChunkFragmenter.RowIndex(y, z)];
                    byte remaining = (byte)(sourceRow & ~visited.GetRow(y, z));

                    while (remaining != 0)
                    {
                        int x = math.tzcnt((uint)remaining);
                        byte runMask = GetSourceRunMaskContaining(sourceRow, x);

                        Islands.AddNoResize(FloodFillSlice(ChunkFragmenter.RowIndex(y, z), runMask, ref visited));
                        remaining = (byte)(sourceRow & ~visited.GetRow(y, z));
                    }
                }
            }
        }

        private ChunkFragmenter.IslandMask FloodFillSlice(
            int rowIndex,
            byte startRunMask,
            ref ChunkFragmenter.IslandMask visited)
        {
            ChunkFragmenter.IslandMask island = default;
            int read = 0;
            int write = 0;

            EnqueueSlice(rowIndex, startRunMask, ref write, ref visited, ref island);

            while (read < write)
            {
                int current = SliceQueue[read++];
                int currentRowIndex = SliceRowIndex(current);
                byte currentRunMask = SliceRunMask(current);
                int y = currentRowIndex & 7;
                int z = currentRowIndex >> 3;

                TryAddNeighborSlices(y - 1, z, currentRunMask, ref write, ref visited, ref island);
                TryAddNeighborSlices(y + 1, z, currentRunMask, ref write, ref visited, ref island);
                TryAddNeighborSlices(y, z - 1, currentRunMask, ref write, ref visited, ref island);
                TryAddNeighborSlices(y, z + 1, currentRunMask, ref write, ref visited, ref island);
            }

            return island;
        }

        private void TryAddNeighborSlices(
            int y,
            int z,
            byte currentRunMask,
            ref int write,
            ref ChunkFragmenter.IslandMask visited,
            ref ChunkFragmenter.IslandMask island)
        {
            if ((uint)y >= ChunkFragmenter.ChunkSize || (uint)z >= ChunkFragmenter.ChunkSize)
            {
                return;
            }

            int rowIndex = ChunkFragmenter.RowIndex(y, z);
            byte sourceRow = SourceRows[SourceOffset + rowIndex];
            byte unvisitedRow = (byte)(sourceRow & ~visited.GetRow(y, z));
            byte overlappingUnvisited = (byte)(unvisitedRow & currentRunMask);

            while (overlappingUnvisited != 0)
            {
                int x = math.tzcnt((uint)overlappingUnvisited);
                byte runMask = GetSourceRunMaskContaining(sourceRow, x);

                EnqueueSlice(rowIndex, runMask, ref write, ref visited, ref island);
                overlappingUnvisited = (byte)(overlappingUnvisited & ~runMask);
            }
        }

        private void EnqueueSlice(
            int rowIndex,
            byte runMask,
            ref int write,
            ref ChunkFragmenter.IslandMask visited,
            ref ChunkFragmenter.IslandMask island)
        {
            SliceQueue[write++] = PackSlice(rowIndex, runMask);

            int y = rowIndex & 7;
            int z = rowIndex >> 3;
            byte visitedRow = visited.GetRow(y, z);
            byte islandRow = island.GetRow(y, z);

            visited.SetRow(y, z, (byte)(visitedRow | runMask));
            island.SetRow(y, z, (byte)(islandRow | runMask));
        }

        private static byte GetSourceRunMaskContaining(byte sourceRow, int x)
        {
            uint row = sourceRow;
            uint zeroMask = (~row) & 0xFFu;

            uint leftZeros = zeroMask & ((1u << x) - 1u);
            int start = leftZeros == 0u ? 0 : math.floorlog2(leftZeros) + 1;

            uint rightZeros = zeroMask & (0xFFu & ~((1u << (x + 1)) - 1u));
            int end = rightZeros == 0u ? ChunkFragmenter.ChunkSize : math.tzcnt(rightZeros);

            return CreateRunMask(start, end);
        }

        private static byte CreateRunMask(int startInclusive, int endExclusive)
        {
            int width = endExclusive - startInclusive;
            int mask = ((1 << width) - 1) << startInclusive;
            return (byte)mask;
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
    }
}
