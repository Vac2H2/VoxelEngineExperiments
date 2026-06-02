using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace AlgorithmTesting
{
    [BurstCompile]
    public struct ChunkFragmenterNaiveJob : IJob
    {
        [ReadOnly]
        public NativeArray<byte> SourceRows;

        public int SourceOffset;
        public NativeList<ChunkFragmenter.IslandMask> Islands;
        public NativeArray<int> Queue;

        public void Execute()
        {
            Islands.Clear();

            ChunkFragmenter.IslandMask visited = default;

            for (int z = 0; z < ChunkFragmenter.ChunkSize; z++)
            {
                for (int y = 0; y < ChunkFragmenter.ChunkSize; y++)
                {
                    byte row = SourceRows[SourceOffset + ChunkFragmenter.RowIndex(y, z)];

                    while (row != 0)
                    {
                        int x = math.tzcnt((uint)row);
                        int start = PackIndex(x, y, z);

                        if (!visited.IsVoxelSet(x, y, z))
                        {
                            Islands.AddNoResize(FloodFill(start, ref visited));
                        }

                        row = (byte)(row & (row - 1));
                    }
                }
            }
        }

        private ChunkFragmenter.IslandMask FloodFill(int start, ref ChunkFragmenter.IslandMask visited)
        {
            ChunkFragmenter.IslandMask island = default;
            int read = 0;
            int write = 0;

            Queue[write++] = start;
            MarkVisited(start, ref visited, ref island);

            while (read < write)
            {
                int current = Queue[read++];
                int x = current & 7;
                int y = (current >> 3) & 7;
                int z = current >> 6;

                TryAddNeighbor(x - 1, y, z, ref write, ref visited, ref island);
                TryAddNeighbor(x + 1, y, z, ref write, ref visited, ref island);
                TryAddNeighbor(x, y - 1, z, ref write, ref visited, ref island);
                TryAddNeighbor(x, y + 1, z, ref write, ref visited, ref island);
                TryAddNeighbor(x, y, z - 1, ref write, ref visited, ref island);
                TryAddNeighbor(x, y, z + 1, ref write, ref visited, ref island);
            }

            return island;
        }

        private void TryAddNeighbor(
            int x,
            int y,
            int z,
            ref int write,
            ref ChunkFragmenter.IslandMask visited,
            ref ChunkFragmenter.IslandMask island)
        {
            if ((uint)x >= ChunkFragmenter.ChunkSize ||
                (uint)y >= ChunkFragmenter.ChunkSize ||
                (uint)z >= ChunkFragmenter.ChunkSize)
            {
                return;
            }

            if (!IsSourceVoxelSet(x, y, z) || visited.IsVoxelSet(x, y, z))
            {
                return;
            }

            int packed = PackIndex(x, y, z);
            Queue[write++] = packed;
            MarkVisited(packed, ref visited, ref island);
        }

        private bool IsSourceVoxelSet(int x, int y, int z)
        {
            byte row = SourceRows[SourceOffset + ChunkFragmenter.RowIndex(y, z)];
            return (row & ChunkFragmenter.VoxelBit(x)) != 0;
        }

        private static void MarkVisited(
            int packed,
            ref ChunkFragmenter.IslandMask visited,
            ref ChunkFragmenter.IslandMask island)
        {
            int x = packed & 7;
            int y = (packed >> 3) & 7;
            int z = packed >> 6;

            visited.SetVoxel(x, y, z);
            island.SetVoxel(x, y, z);
        }

        private static int PackIndex(int x, int y, int z)
        {
            return x | (y << 3) | (z << 6);
        }
    }
}
