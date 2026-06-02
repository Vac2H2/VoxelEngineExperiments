using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.DestructionShape
{
    public readonly struct DestructionShapeDataView
    {
        public DestructionShapeDataView(
            NativeArray<byte> destructionMasks,
            NativeArray<int3> chunkPositions,
            NativeArray<byte> chunkUsed,
            NativeArray<DestructionShapeMetadata> shapes)
        {
            DestructionMasks = destructionMasks;
            ChunkPositions = chunkPositions;
            ChunkUsed = chunkUsed;
            Shapes = shapes;
        }

        public NativeArray<byte> DestructionMasks { get; }
        public NativeArray<int3> ChunkPositions { get; }
        public NativeArray<byte> ChunkUsed { get; }
        public NativeArray<DestructionShapeMetadata> Shapes { get; }
    }
}
