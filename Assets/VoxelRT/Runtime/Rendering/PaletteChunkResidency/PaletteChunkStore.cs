namespace VoxelRT.Runtime.Rendering.PaletteChunkResidency
{
    internal sealed class PaletteChunkStore : FixedStrideRawChunkStoreBase, IPaletteChunkStore
    {
        public const uint EntryCount = 256;
        public const uint EntryStrideBytes = 4;
        public const uint FixedChunkStrideBytes = EntryCount * EntryStrideBytes;

        public PaletteChunkStore()
        {
            EnsureCapacity(1u);
        }

        protected override uint FixedStrideBytes => FixedChunkStrideBytes;

        public uint ChunkStrideBytes => FixedChunkStrideBytes;
    }
}
