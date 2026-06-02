using Unity.Collections;

namespace VoxelEngineDOTS.Physics
{
    public struct VoxelWorldParallelView
    {
        public NativeArray<VoxelWorldBodyRecord> Bodies;
        public NativeArray<VoxelWorldChunkRecord> Chunks;

        public NativeParallelHashMap<VoxelWorldChunkKey, int>.ReadOnly ChunkLookup;

        public NativeArray<byte> IsSolid;
        public NativeArray<byte> IsFace;
        public NativeArray<byte> IsEdge;
        public NativeArray<byte> IsCorner;
    }
}
