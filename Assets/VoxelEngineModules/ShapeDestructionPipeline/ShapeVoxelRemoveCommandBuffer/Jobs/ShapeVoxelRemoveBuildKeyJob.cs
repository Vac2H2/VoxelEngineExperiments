using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace VoxelEngineModules.Shape
{
    public struct ShapeVoxelRemoveCommandKey : IComparable<ShapeVoxelRemoveCommandKey>
    {
        public int ShapeHandle;
        public byte ChunkSlot;
        public int CommandIndex;

        public int CompareTo(ShapeVoxelRemoveCommandKey other)
        {
            if (ShapeHandle < other.ShapeHandle)
            {
                return -1;
            }

            if (ShapeHandle > other.ShapeHandle)
            {
                return 1;
            }

            if (ChunkSlot < other.ChunkSlot)
            {
                return -1;
            }

            if (ChunkSlot > other.ChunkSlot)
            {
                return 1;
            }

            if (CommandIndex < other.CommandIndex)
            {
                return -1;
            }

            if (CommandIndex > other.CommandIndex)
            {
                return 1;
            }

            return 0;
        }
    }

    [BurstCompile]
    public struct ShapeVoxelRemoveBuildKeyJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> ShapeHandles;

        [ReadOnly]
        public NativeArray<byte> ChunkSlots;

        [WriteOnly]
        public NativeArray<ShapeVoxelRemoveCommandKey> CommandKeys;

        public void Execute(int commandIndex)
        {
            CommandKeys[commandIndex] = new ShapeVoxelRemoveCommandKey
            {
                ShapeHandle = ShapeHandles[commandIndex],
                ChunkSlot = ChunkSlots[commandIndex],
                CommandIndex = commandIndex
            };
        }

        public static ShapeVoxelRemoveBuildKeyJob Create(
            ShapeVoxelRemoveCommandBuffer commandBuffer,
            NativeArray<ShapeVoxelRemoveCommandKey> commandKeys)
        {
            return new ShapeVoxelRemoveBuildKeyJob
            {
                ShapeHandles = commandBuffer.ShapeHandles,
                ChunkSlots = commandBuffer.ChunkSlots,
                CommandKeys = commandKeys
            };
        }
    }
}
