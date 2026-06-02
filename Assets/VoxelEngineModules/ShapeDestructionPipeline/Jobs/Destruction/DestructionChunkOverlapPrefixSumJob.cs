using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct DestructionChunkOverlapPrefixSumJob : IJob
    {
        [ReadOnly]
        public NativeArray<int> ChunkOverlapCounts;

        [WriteOnly]
        public NativeArray<int> ChunkOverlapOffsets;

        [WriteOnly]
        public NativeArray<int> TotalVoxelMaskBlockCount;

        public void Execute()
        {
            int runningTotal = 0;
            for (int i = 0; i < ChunkOverlapCounts.Length; i++)
            {
                ChunkOverlapOffsets[i] = runningTotal;
                runningTotal += ChunkOverlapCounts[i];
            }

            TotalVoxelMaskBlockCount[0] = runningTotal;
        }
    }
}
