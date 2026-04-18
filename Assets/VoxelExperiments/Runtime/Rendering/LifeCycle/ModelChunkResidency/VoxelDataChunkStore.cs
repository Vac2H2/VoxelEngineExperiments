namespace VoxelExperiments.Runtime.Rendering.ModelChunkResidency
{
    internal sealed class VoxelDataChunkStore : FixedStrideRawChunkStoreBase, IVoxelDataChunkStore
    {
        public const uint FixedChunkStrideBytes = 512;

        public VoxelDataChunkStore()
        {
            EnsureCapacity(1u);
        }

        protected override uint FixedStrideBytes => FixedChunkStrideBytes;

        public uint ChunkStrideBytes => FixedChunkStrideBytes;
    }
}
