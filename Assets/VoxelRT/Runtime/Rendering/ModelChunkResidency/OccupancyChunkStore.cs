namespace VoxelRT.Runtime.Rendering.ModelChunkResidency
{
    internal sealed class OccupancyChunkStore : FixedStrideRawChunkStoreBase, IOccupancyChunkStore
    {
        public const uint FixedChunkStrideBytes = 64;

        protected override uint FixedStrideBytes => FixedChunkStrideBytes;

        public uint ChunkStrideBytes => FixedChunkStrideBytes;
    }
}
