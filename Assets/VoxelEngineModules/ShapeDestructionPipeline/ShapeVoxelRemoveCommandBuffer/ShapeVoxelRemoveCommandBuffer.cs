using Unity.Collections;

namespace VoxelEngineModules.Shape
{
    public struct ShapeVoxelRemoveCommandBuffer
    {
        public const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int MaskBytesPerCommand = ShapeDataContainer.BitPlaneBytesPerChunk;

        public NativeArray<int> ShapeHandles;
        public NativeArray<byte> ChunkSlots;
        public NativeArray<byte> Masks;

        public int CommandCount;
        public int CommandCapacity;
    }
}
