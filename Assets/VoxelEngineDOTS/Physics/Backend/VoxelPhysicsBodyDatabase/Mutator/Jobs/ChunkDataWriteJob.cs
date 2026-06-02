using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngineDOTS.Physics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public readonly struct ChunkWriteCommand
    {
        public ChunkWriteCommand(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            BodyHandle = bodyHandle;
            ChunkPosition = chunkPosition;
        }

        public VoxelPhysicsBodyHandle BodyHandle { get; }
        public int3 ChunkPosition { get; }
    }

    public struct ChunkDataWriteJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> ChunkDataIds;

        [ReadOnly]
        public NativeArray<byte> Data;

        public ChunkDataContainer<byte>.Writer ChunkDataWriter;
        public int ChunkSize;

        public void Execute(int index)
        {
            int chunkDataId = ChunkDataIds[index];
            int sourceOffset = index * ChunkSize;
            NativeSlice<byte> source = new NativeSlice<byte>(
                Data,
                sourceOffset,
                ChunkSize);

            ChunkDataWriter.OverwriteChunk(chunkDataId, source);
        }
    }
}
