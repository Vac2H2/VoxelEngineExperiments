using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapeVoxelRemovePackJob : IJobParallelFor
    {
        public const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int MaskBytesPerCommand = ShapeVoxelRemoveCommandBuffer.MaskBytesPerCommand;
        public const int MaskBytesPerShape = ChunksPerShape * MaskBytesPerCommand;

        [ReadOnly]
        public NativeArray<byte> CommandMasks;

        [ReadOnly]
        public NativeArray<ShapeVoxelRemoveCommandKey> SortedKeys;

        [ReadOnly]
        public NativeArray<ShapeVoxelRemoveCommandRange> ShapeRanges;

        [NativeDisableParallelForRestriction]
        public NativeArray<byte> RemoveMasks;

        public void Execute(int shapeListIndex)
        {
            InitializeShapeRemoveMask(shapeListIndex);

            ShapeVoxelRemoveCommandRange range = ShapeRanges[shapeListIndex];
            int endKeyIndex = range.StartKeyIndex + range.KeyCount;
            for (int keyIndex = range.StartKeyIndex; keyIndex < endKeyIndex; keyIndex++)
            {
                ShapeVoxelRemoveCommandKey key = SortedKeys[keyIndex];
                if ((uint)key.ChunkSlot >= ChunksPerShape)
                {
                    continue;
                }

                ApplyCommandMask(shapeListIndex, key.ChunkSlot, key.CommandIndex);
            }
        }

        private void InitializeShapeRemoveMask(int shapeListIndex)
        {
            int removeMaskBase = shapeListIndex * MaskBytesPerShape;
            for (int i = 0; i < MaskBytesPerShape; i++)
            {
                RemoveMasks[removeMaskBase + i] = 0xFF;
            }
        }

        private void ApplyCommandMask(
            int shapeListIndex,
            int chunkSlot,
            int commandIndex)
        {
            int sourceBase = commandIndex * MaskBytesPerCommand;
            int targetBase =
                shapeListIndex * MaskBytesPerShape +
                chunkSlot * MaskBytesPerCommand;

            for (int i = 0; i < MaskBytesPerCommand; i++)
            {
                RemoveMasks[targetBase + i] =
                    (byte)(RemoveMasks[targetBase + i] & CommandMasks[sourceBase + i]);
            }
        }

        public static ShapeVoxelRemovePackJob Create(
            ShapeVoxelRemoveCommandBuffer commandBuffer,
            NativeArray<ShapeVoxelRemoveCommandKey> sortedKeys,
            NativeArray<ShapeVoxelRemoveCommandRange> shapeRanges,
            NativeArray<byte> removeMasks)
        {
            return new ShapeVoxelRemovePackJob
            {
                CommandMasks = commandBuffer.Masks,
                SortedKeys = sortedKeys,
                ShapeRanges = shapeRanges,
                RemoveMasks = removeMasks
            };
        }
    }
}
