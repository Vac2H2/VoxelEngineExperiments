using System;
using Unity.Collections;
using Unity.Jobs;

namespace AlgorithmTesting
{
    public static class ChunkFragmenter
    {
        public const int ChunkSize = 8;
        public const int RowCount = ChunkSize * ChunkSize;
        public const int VoxelCount = ChunkSize * ChunkSize * ChunkSize;
        public const int MaxSliceCount = RowCount * (ChunkSize / 2);
        public const int MaxIslandCount = VoxelCount / 2;

        public static void FindIslands(
            NativeArray<byte> sourceRows,
            NativeList<IslandMask> islands)
        {
            ValidateInputs(sourceRows, islands);
            EnsureIslandCapacity(islands);

            using NativeArray<int> queue = new NativeArray<int>(VoxelCount, Allocator.TempJob);

            new ChunkFragmenterNaiveJob
            {
                SourceRows = sourceRows,
                Islands = islands,
                Queue = queue
            }.Schedule().Complete();
        }

        public static void FindIslandsBySlices(
            NativeArray<byte> sourceRows,
            NativeList<IslandMask> islands)
        {
            ValidateInputs(sourceRows, islands);
            EnsureIslandCapacity(islands);

            using NativeArray<int> queue = new NativeArray<int>(MaxSliceCount, Allocator.TempJob);

            new ChunkFragmenterOptimizedJob
            {
                SourceRows = sourceRows,
                Islands = islands,
                SliceQueue = queue
            }.Schedule().Complete();
        }

        public static JobHandle ScheduleFindIslands(
            NativeArray<byte> sourceRows,
            NativeList<IslandMask> islands,
            NativeArray<int> queue,
            JobHandle dependency = default)
        {
            ValidateInputs(sourceRows, islands);
            ValidateQueue(queue, VoxelCount, nameof(queue));
            EnsureIslandCapacity(islands);

            return new ChunkFragmenterNaiveJob
            {
                SourceRows = sourceRows,
                Islands = islands,
                Queue = queue
            }.Schedule(dependency);
        }

        public static JobHandle ScheduleFindIslandsBySlices(
            NativeArray<byte> sourceRows,
            NativeList<IslandMask> islands,
            NativeArray<int> sliceQueue,
            JobHandle dependency = default)
        {
            ValidateInputs(sourceRows, islands);
            ValidateQueue(sliceQueue, MaxSliceCount, nameof(sliceQueue));
            EnsureIslandCapacity(islands);

            return new ChunkFragmenterOptimizedJob
            {
                SourceRows = sourceRows,
                Islands = islands,
                SliceQueue = sliceQueue
            }.Schedule(dependency);
        }

        public static int RowIndex(int y, int z)
        {
            return z * ChunkSize + y;
        }

        public static byte VoxelBit(int x)
        {
            return (byte)(1 << x);
        }

        private static void ValidateInputs(NativeArray<byte> sourceRows, NativeList<IslandMask> islands)
        {
            if (!sourceRows.IsCreated)
            {
                throw new ArgumentException("Source rows must be created.", nameof(sourceRows));
            }

            if (sourceRows.Length < RowCount)
            {
                throw new ArgumentException($"Source rows must have at least {RowCount} bytes.", nameof(sourceRows));
            }

            if (!islands.IsCreated)
            {
                throw new ArgumentException("Island output list must be created.", nameof(islands));
            }
        }

        private static void ValidateQueue(NativeArray<int> queue, int requiredLength, string parameterName)
        {
            if (!queue.IsCreated)
            {
                throw new ArgumentException("Queue must be created.", parameterName);
            }

            if (queue.Length < requiredLength)
            {
                throw new ArgumentException($"Queue must have at least {requiredLength} entries.", parameterName);
            }
        }

        private static void EnsureIslandCapacity(NativeList<IslandMask> islands)
        {
            if (islands.Capacity < MaxIslandCount)
            {
                islands.Capacity = MaxIslandCount;
            }
        }

        public struct IslandMask
        {
            public ulong Z0;
            public ulong Z1;
            public ulong Z2;
            public ulong Z3;
            public ulong Z4;
            public ulong Z5;
            public ulong Z6;
            public ulong Z7;

            public bool IsEmpty =>
                (Z0 | Z1 | Z2 | Z3 | Z4 | Z5 | Z6 | Z7) == 0UL;

            public bool IsVoxelSet(int x, int y, int z)
            {
                return (GetLayer(z) & VoxelMask(x, y)) != 0UL;
            }

            public byte GetRow(int y, int z)
            {
                return (byte)(GetLayer(z) >> (y * ChunkSize));
            }

            public void SetRow(int y, int z, byte value)
            {
                ulong clearMask = ~(0xFFUL << (y * ChunkSize));
                ulong layer = (GetLayer(z) & clearMask) | ((ulong)value << (y * ChunkSize));
                SetLayer(z, layer);
            }

            public void SetVoxel(int x, int y, int z)
            {
                SetLayer(z, GetLayer(z) | VoxelMask(x, y));
            }

            public void ClearVoxel(int x, int y, int z)
            {
                SetLayer(z, GetLayer(z) & ~VoxelMask(x, y));
            }

            public void CopyRowsTo(NativeArray<byte> destinationRows, int destinationOffset = 0)
            {
                for (int z = 0; z < ChunkSize; z++)
                {
                    for (int y = 0; y < ChunkSize; y++)
                    {
                        destinationRows[destinationOffset + z * ChunkSize + y] = GetRow(y, z);
                    }
                }
            }

            private static ulong VoxelMask(int x, int y)
            {
                return 1UL << (y * ChunkSize + x);
            }

            private ulong GetLayer(int z)
            {
                switch (z)
                {
                    case 0:
                        return Z0;
                    case 1:
                        return Z1;
                    case 2:
                        return Z2;
                    case 3:
                        return Z3;
                    case 4:
                        return Z4;
                    case 5:
                        return Z5;
                    case 6:
                        return Z6;
                    default:
                        return Z7;
                }
            }

            private void SetLayer(int z, ulong value)
            {
                switch (z)
                {
                    case 0:
                        Z0 = value;
                        break;
                    case 1:
                        Z1 = value;
                        break;
                    case 2:
                        Z2 = value;
                        break;
                    case 3:
                        Z3 = value;
                        break;
                    case 4:
                        Z4 = value;
                        break;
                    case 5:
                        Z5 = value;
                        break;
                    case 6:
                        Z6 = value;
                        break;
                    default:
                        Z7 = value;
                        break;
                }
            }
        }
    }
}
