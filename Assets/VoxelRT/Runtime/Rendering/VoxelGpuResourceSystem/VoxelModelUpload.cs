using Unity.Collections;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;

namespace VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem
{
    public readonly struct VoxelModelUpload
    {
        public VoxelModelUpload(
            uint chunkCount,
            NativeArray<byte> occupancyBytes,
            NativeArray<byte> voxelBytes,
            NativeArray<ModelChunkAabb> chunkAabbs)
        {
            ChunkCount = chunkCount;
            OccupancyBytes = occupancyBytes;
            VoxelBytes = voxelBytes;
            ChunkAabbs = chunkAabbs;
        }

        public uint ChunkCount { get; }

        public NativeArray<byte> OccupancyBytes { get; }

        public NativeArray<byte> VoxelBytes { get; }

        public NativeArray<ModelChunkAabb> ChunkAabbs { get; }
    }
}
