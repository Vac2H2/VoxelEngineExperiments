namespace VoxelExperiments.Runtime.Rendering.ModelChunkResidency
{
    internal interface IChunkSpanAllocator
    {
        ChunkSpan Allocate(uint chunkCount);

        void Free(ChunkSpan span);

        void Reset();
    }
}
