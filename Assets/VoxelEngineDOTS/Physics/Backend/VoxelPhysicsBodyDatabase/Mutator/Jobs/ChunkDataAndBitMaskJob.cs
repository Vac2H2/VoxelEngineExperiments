using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using VoxelEngineDOTS.Physics;

namespace VoxelEngineDOTS.Physics.Backend
{
    [BurstCompile]
    public struct ChunkDataAndBitMaskJob : IJobParallelFor
    {
        public const int RowBitCount = 8;

        [ReadOnly]
        public NativeArray<int> ChunkDataIds;

        [ReadOnly]
        public NativeArray<byte> Data;

        public ChunkDataContainer<byte>.Writer ChunkDataWriter;
        public int BitMaskBytesPerChunk;

        public void Execute(int index)
        {
            int chunkDataId = ChunkDataIds[index];
            int sourceOffset = index * BitMaskBytesPerChunk;
            NativeSlice<ulong> chunkRows =
                ChunkDataWriter.GetChunkSlice(chunkDataId).SliceConvert<ulong>();

            for (int rowIndex = 0; rowIndex < BitMaskBytesPerChunk; rowIndex++)
            {
                byte rowMask = Data[sourceOffset + rowIndex];

                if (rowMask == byte.MaxValue)
                {
                    continue;
                }

                chunkRows[rowIndex] &= ExpandBitMaskToByteLanes(rowMask);
            }
        }

        private static ulong ExpandBitMaskToByteLanes(byte bitMask)
        {
            ulong lanes = bitMask;

            lanes = (lanes | (lanes << 28)) & 0x0000000F0000000FUL;
            lanes = (lanes | (lanes << 14)) & 0x0003000300030003UL;
            lanes = (lanes | (lanes << 7)) & 0x0101010101010101UL;

            return lanes * byte.MaxValue;
        }
    }
}
