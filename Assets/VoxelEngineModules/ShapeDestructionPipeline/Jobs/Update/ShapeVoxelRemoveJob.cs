using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapeVoxelRemoveJob : IJobParallelFor
    {
        public const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;

        [ReadOnly]
        public NativeArray<int> ShapeHandles;

        [ReadOnly]
        public NativeArray<byte> SourceIsOccupied;

        [ReadOnly]
        public NativeArray<byte> RemoveMasks;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> TargetIsOccupied;

        public void Execute(int shapeListIndex)
        {
            int shapeHandle = ShapeHandles[shapeListIndex];
            int shapeChunkBase = shapeHandle * ChunksPerShape;
            int localChunkBase = shapeListIndex * ChunksPerShape;

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int localChunkIndex = localChunkBase + chunkSlot;
                int chunkSlotIndex = shapeChunkBase + chunkSlot;
                int sourceBase = chunkSlotIndex * BitPlaneBytesPerChunk;
                int targetBase = localChunkIndex * BitPlaneBytesPerChunk;
                int maskBase = localChunkIndex * BitPlaneBytesPerChunk;
                ApplyRemoveMask(sourceBase, targetBase, maskBase);
            }
        }

        private void ApplyRemoveMask(int sourceBase, int targetBase, int maskBase)
        {
            for (int offset = 0; offset < BitPlaneBytesPerChunk; offset++)
            {
                TargetIsOccupied[targetBase + offset] =
                    (byte)(SourceIsOccupied[sourceBase + offset] & RemoveMasks[maskBase + offset]);
            }
        }
    }
}
