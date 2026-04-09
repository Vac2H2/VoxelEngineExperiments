namespace VoxelRT.Runtime.Rendering.ModelChunkResidency
{
    internal sealed class VoxelDataChunkStore : FixedStrideRawChunkStoreBase, IVoxelDataChunkStore
    {
        public const uint FixedChunkStrideBytes = 512;

        protected override uint FixedStrideBytes => FixedChunkStrideBytes;

        public uint ChunkStrideBytes => FixedChunkStrideBytes;
    }
}
