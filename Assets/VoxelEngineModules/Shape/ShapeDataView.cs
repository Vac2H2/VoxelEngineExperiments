using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    public readonly struct ShapeDataView
    {
        public ShapeDataView(
            NativeArray<byte> isOccupied,
            NativeArray<byte> isFace,
            NativeArray<byte> isEdge,
            NativeArray<byte> isCorner,
            NativeArray<int3> chunkPositions,
            NativeArray<byte> chunkUsed,
            NativeArray<ShapeMetadata> shapes)
        {
            IsOccupied = isOccupied;
            IsFace = isFace;
            IsEdge = isEdge;
            IsCorner = isCorner;
            ChunkPositions = chunkPositions;
            ChunkUsed = chunkUsed;
            Shapes = shapes;
        }

        public NativeArray<byte> IsOccupied { get; }
        public NativeArray<byte> IsFace { get; }
        public NativeArray<byte> IsEdge { get; }
        public NativeArray<byte> IsCorner { get; }
        public NativeArray<int3> ChunkPositions { get; }
        public NativeArray<byte> ChunkUsed { get; }
        public NativeArray<ShapeMetadata> Shapes { get; }
    }
}
