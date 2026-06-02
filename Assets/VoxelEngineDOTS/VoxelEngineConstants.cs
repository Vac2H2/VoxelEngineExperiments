namespace VoxelEngineDOTS
{
    public static class VoxelEngineConstants
    {
        public const int ChunkSize = 8;
        public const int ChunkSizeLog2 = 3;
        public const int ChunkSizeMask = ChunkSize - 1;
        public const int VoxelsPerChunk = ChunkSize * ChunkSize * ChunkSize;
        public const int BytesPerChunk = VoxelsPerChunk;
        public const int WordsPerChunk = BytesPerChunk / sizeof(uint);
        public const int DefaultInitialChunksPerPhysicsBodyCapacity = 4;
    }
}
