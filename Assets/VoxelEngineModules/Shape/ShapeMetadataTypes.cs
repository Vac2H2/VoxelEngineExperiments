using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    public struct BuiltChunkMetadata
    {
        public int SourceShapeHandle;
        public int3 ChunkPosition;
        public byte IsUsed;

        public readonly bool Used => IsUsed != 0;
    }

    public struct ShapeMetadata
    {
        public int BodyHandle;
        public byte IsUsed;

        public readonly bool Used => IsUsed != 0;
    }
}
