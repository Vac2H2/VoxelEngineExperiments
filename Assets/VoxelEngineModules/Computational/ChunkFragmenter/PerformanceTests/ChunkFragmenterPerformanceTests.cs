using AlgorithmTesting;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace AlgorithmTesting.PerformanceTests
{
    public sealed class ChunkFragmenterPerformanceTests
    {
        private const int ChunkCount = 8192;
        private const int MeasurementCount = 30;
        private const int WarmupCount = 6;

        private NativeArray<byte> _runHeavyRows;
        private NativeArray<byte> _fragmentedRows;
        private NativeArray<byte> _mixedCaveRows;

        public enum IslandAlgorithm
        {
            VoxelBfs,
            SliceBfs
        }

        public enum DatasetKind
        {
            RunHeavy,
            Fragmented,
            MixedCaves
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _runHeavyRows = new NativeArray<byte>(ChunkCount * ChunkFragmenter.RowCount, Allocator.Persistent);
            _fragmentedRows = new NativeArray<byte>(ChunkCount * ChunkFragmenter.RowCount, Allocator.Persistent);
            _mixedCaveRows = new NativeArray<byte>(ChunkCount * ChunkFragmenter.RowCount, Allocator.Persistent);

            FillRunHeavyDataset(_runHeavyRows);
            FillFragmentedDataset(_fragmentedRows);
            FillMixedCaveDataset(_mixedCaveRows);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            DisposeIfCreated(ref _runHeavyRows);
            DisposeIfCreated(ref _fragmentedRows);
            DisposeIfCreated(ref _mixedCaveRows);
        }

        [Test]
        public void SliceBfsMatchesVoxelBfs(
            [Values(DatasetKind.RunHeavy, DatasetKind.Fragmented, DatasetKind.MixedCaves)] DatasetKind dataset)
        {
            NativeArray<byte> rows = GetRows(dataset);

            using NativeList<ChunkFragmenter.IslandMask> bfsIslands =
                new NativeList<ChunkFragmenter.IslandMask>(ChunkFragmenter.MaxIslandCount, Allocator.TempJob);
            using NativeList<ChunkFragmenter.IslandMask> sliceIslands =
                new NativeList<ChunkFragmenter.IslandMask>(ChunkFragmenter.MaxIslandCount, Allocator.TempJob);
            using NativeArray<int> bfsQueue = new NativeArray<int>(ChunkFragmenter.VoxelCount, Allocator.TempJob);
            using NativeArray<int> sliceQueue = new NativeArray<int>(ChunkFragmenter.MaxSliceCount, Allocator.TempJob);

            for (int chunkIndex = 0; chunkIndex < ChunkCount; chunkIndex++)
            {
                int sourceOffset = chunkIndex * ChunkFragmenter.RowCount;

                new ChunkFragmenterNaiveJob
                {
                    SourceRows = rows,
                    SourceOffset = sourceOffset,
                    Islands = bfsIslands,
                    Queue = bfsQueue
                }.Execute();

                new ChunkFragmenterOptimizedJob
                {
                    SourceRows = rows,
                    SourceOffset = sourceOffset,
                    Islands = sliceIslands,
                    SliceQueue = sliceQueue
                }.Execute();

                Assert.That(
                    sliceIslands.Length,
                    Is.EqualTo(bfsIslands.Length),
                    $"{dataset} chunk {chunkIndex} island count mismatch.");

                for (int islandIndex = 0; islandIndex < bfsIslands.Length; islandIndex++)
                {
                    AssertMasksEqual(
                        bfsIslands[islandIndex],
                        sliceIslands[islandIndex],
                        $"{dataset} chunk {chunkIndex} island {islandIndex}");
                }
            }
        }

        [Test, Performance]
        [Category("Performance")]
        [TestCase(IslandAlgorithm.VoxelBfs, DatasetKind.RunHeavy)]
        [TestCase(IslandAlgorithm.SliceBfs, DatasetKind.RunHeavy)]
        [TestCase(IslandAlgorithm.VoxelBfs, DatasetKind.Fragmented)]
        [TestCase(IslandAlgorithm.SliceBfs, DatasetKind.Fragmented)]
        [TestCase(IslandAlgorithm.VoxelBfs, DatasetKind.MixedCaves)]
        [TestCase(IslandAlgorithm.SliceBfs, DatasetKind.MixedCaves)]
        public void FindIslandsGeneratedDatasetPerformance(IslandAlgorithm algorithm, DatasetKind dataset)
        {
            NativeArray<byte> rows = GetRows(dataset);

            using NativeList<ChunkFragmenter.IslandMask> islands =
                new NativeList<ChunkFragmenter.IslandMask>(ChunkFragmenter.MaxIslandCount, Allocator.Persistent);
            using NativeArray<int> queue = new NativeArray<int>(ChunkFragmenter.VoxelCount, Allocator.Persistent);
            using NativeArray<ulong> result = new NativeArray<ulong>(2, Allocator.Persistent);

            RunDataset(algorithm, rows, islands, queue, result);
            Assert.That(result[0], Is.GreaterThan(0UL));

            Measure.Custom(new SampleGroup("Dataset.Chunks", SampleUnit.Undefined, true), ChunkCount);
            Measure.Custom(new SampleGroup("Result.TotalIslands", SampleUnit.Undefined, false), result[0]);

            Measure.Method(() => RunDataset(algorithm, rows, islands, queue, result))
                .SampleGroup($"{algorithm}.{dataset}.Time")
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        private NativeArray<byte> GetRows(DatasetKind dataset)
        {
            switch (dataset)
            {
                case DatasetKind.RunHeavy:
                    return _runHeavyRows;
                case DatasetKind.Fragmented:
                    return _fragmentedRows;
                default:
                    return _mixedCaveRows;
            }
        }

        private static void RunDataset(
            IslandAlgorithm algorithm,
            NativeArray<byte> rows,
            NativeList<ChunkFragmenter.IslandMask> islands,
            NativeArray<int> queue,
            NativeArray<ulong> result)
        {
            new RunChunkFragmenterDatasetJob
            {
                Algorithm = algorithm == IslandAlgorithm.VoxelBfs ? 0 : 1,
                SourceRows = rows,
                ChunkCount = ChunkCount,
                Islands = islands,
                Queue = queue,
                Result = result
            }.Schedule().Complete();
        }

        private static void FillRunHeavyDataset(NativeArray<byte> rows)
        {
            XorShift32 random = new XorShift32(0x9E3779B9u);

            for (int chunkIndex = 0; chunkIndex < ChunkCount; chunkIndex++)
            {
                int sourceOffset = chunkIndex * ChunkFragmenter.RowCount;
                ClearChunk(rows, sourceOffset);

                int boxCount = 2 + random.Range(0, 4);
                for (int boxIndex = 0; boxIndex < boxCount; boxIndex++)
                {
                    int x0 = random.Range(0, 6);
                    int x1 = random.Range(x0 + 1, ChunkFragmenter.ChunkSize + 1);
                    int y0 = random.Range(0, 7);
                    int y1 = random.Range(y0 + 1, ChunkFragmenter.ChunkSize + 1);
                    int z0 = random.Range(0, 7);
                    int z1 = random.Range(z0 + 1, ChunkFragmenter.ChunkSize + 1);

                    FillBox(rows, sourceOffset, x0, x1, y0, y1, z0, z1);
                }

                for (int bridgeIndex = 0; bridgeIndex < 3; bridgeIndex++)
                {
                    int y = random.Range(0, ChunkFragmenter.ChunkSize);
                    int z = random.Range(0, ChunkFragmenter.ChunkSize);
                    int x0 = random.Range(0, 5);
                    int x1 = random.Range(x0 + 2, ChunkFragmenter.ChunkSize + 1);
                    OrRow(rows, sourceOffset, y, z, CreateRunMask(x0, x1));
                }
            }
        }

        private static void FillFragmentedDataset(NativeArray<byte> rows)
        {
            XorShift32 random = new XorShift32(0xC2B2AE35u);

            for (int chunkIndex = 0; chunkIndex < ChunkCount; chunkIndex++)
            {
                int sourceOffset = chunkIndex * ChunkFragmenter.RowCount;

                for (int z = 0; z < ChunkFragmenter.ChunkSize; z++)
                {
                    for (int y = 0; y < ChunkFragmenter.ChunkSize; y++)
                    {
                        byte row = 0;

                        for (int x = 0; x < ChunkFragmenter.ChunkSize; x++)
                        {
                            bool paritySlot = ((x + y + z + chunkIndex) & 1) == 0;
                            if (paritySlot && random.Range(0, 100) < 58)
                            {
                                row |= ChunkFragmenter.VoxelBit(x);
                            }
                        }

                        rows[sourceOffset + ChunkFragmenter.RowIndex(y, z)] = row;
                    }
                }
            }
        }

        private static void FillMixedCaveDataset(NativeArray<byte> rows)
        {
            byte[] current = new byte[ChunkFragmenter.RowCount];
            byte[] next = new byte[ChunkFragmenter.RowCount];

            for (int chunkIndex = 0; chunkIndex < ChunkCount; chunkIndex++)
            {
                for (int rowIndex = 0; rowIndex < ChunkFragmenter.RowCount; rowIndex++)
                {
                    current[rowIndex] = 0;
                    next[rowIndex] = 0;
                }

                for (int z = 0; z < ChunkFragmenter.ChunkSize; z++)
                {
                    for (int y = 0; y < ChunkFragmenter.ChunkSize; y++)
                    {
                        byte row = 0;

                        for (int x = 0; x < ChunkFragmenter.ChunkSize; x++)
                        {
                            uint hash = Hash((uint)chunkIndex, (uint)x, (uint)y, (uint)z);
                            if ((hash & 255u) < 118u)
                            {
                                row |= ChunkFragmenter.VoxelBit(x);
                            }
                        }

                        current[ChunkFragmenter.RowIndex(y, z)] = row;
                    }
                }

                for (int pass = 0; pass < 2; pass++)
                {
                    SmoothRows(current, next);
                    byte[] swap = current;
                    current = next;
                    next = swap;
                }

                int bridgeY = chunkIndex & 7;
                int bridgeZ = (chunkIndex >> 3) & 7;
                current[ChunkFragmenter.RowIndex(bridgeY, bridgeZ)] |= CreateRunMask(1, 7);

                int sourceOffset = chunkIndex * ChunkFragmenter.RowCount;
                for (int rowIndex = 0; rowIndex < ChunkFragmenter.RowCount; rowIndex++)
                {
                    rows[sourceOffset + rowIndex] = current[rowIndex];
                }
            }
        }

        private static void SmoothRows(byte[] source, byte[] destination)
        {
            for (int z = 0; z < ChunkFragmenter.ChunkSize; z++)
            {
                for (int y = 0; y < ChunkFragmenter.ChunkSize; y++)
                {
                    byte row = 0;

                    for (int x = 0; x < ChunkFragmenter.ChunkSize; x++)
                    {
                        int neighborCount = CountFaceNeighbors(source, x, y, z);
                        bool occupied = IsSet(source, x, y, z);

                        if (neighborCount >= 4 || (occupied && neighborCount >= 2))
                        {
                            row |= ChunkFragmenter.VoxelBit(x);
                        }
                    }

                    destination[ChunkFragmenter.RowIndex(y, z)] = row;
                }
            }
        }

        private static int CountFaceNeighbors(byte[] rows, int x, int y, int z)
        {
            int count = 0;

            count += IsSet(rows, x - 1, y, z) ? 1 : 0;
            count += IsSet(rows, x + 1, y, z) ? 1 : 0;
            count += IsSet(rows, x, y - 1, z) ? 1 : 0;
            count += IsSet(rows, x, y + 1, z) ? 1 : 0;
            count += IsSet(rows, x, y, z - 1) ? 1 : 0;
            count += IsSet(rows, x, y, z + 1) ? 1 : 0;

            return count;
        }

        private static bool IsSet(byte[] rows, int x, int y, int z)
        {
            if ((uint)x >= ChunkFragmenter.ChunkSize || (uint)y >= ChunkFragmenter.ChunkSize || (uint)z >= ChunkFragmenter.ChunkSize)
            {
                return false;
            }

            return (rows[ChunkFragmenter.RowIndex(y, z)] & ChunkFragmenter.VoxelBit(x)) != 0;
        }

        private static void FillBox(
            NativeArray<byte> rows,
            int sourceOffset,
            int x0,
            int x1,
            int y0,
            int y1,
            int z0,
            int z1)
        {
            byte xMask = CreateRunMask(x0, x1);

            for (int z = z0; z < z1; z++)
            {
                for (int y = y0; y < y1; y++)
                {
                    OrRow(rows, sourceOffset, y, z, xMask);
                }
            }
        }

        private static void OrRow(NativeArray<byte> rows, int sourceOffset, int y, int z, byte mask)
        {
            int rowIndex = sourceOffset + ChunkFragmenter.RowIndex(y, z);
            rows[rowIndex] = (byte)(rows[rowIndex] | mask);
        }

        private static void ClearChunk(NativeArray<byte> rows, int sourceOffset)
        {
            for (int rowIndex = 0; rowIndex < ChunkFragmenter.RowCount; rowIndex++)
            {
                rows[sourceOffset + rowIndex] = 0;
            }
        }

        private static byte CreateRunMask(int startInclusive, int endExclusive)
        {
            int width = endExclusive - startInclusive;
            return (byte)(((1 << width) - 1) << startInclusive);
        }

        private static uint Hash(uint chunkIndex, uint x, uint y, uint z)
        {
            uint hash = chunkIndex * 0x9E3779B9u;
            hash ^= x * 0x85EBCA6Bu;
            hash ^= y * 0xC2B2AE35u;
            hash ^= z * 0x27D4EB2Fu;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash;
        }

        private static void AssertMasksEqual(
            ChunkFragmenter.IslandMask expected,
            ChunkFragmenter.IslandMask actual,
            string context)
        {
            Assert.That(actual.Z0, Is.EqualTo(expected.Z0), $"{context} Z0 mismatch.");
            Assert.That(actual.Z1, Is.EqualTo(expected.Z1), $"{context} Z1 mismatch.");
            Assert.That(actual.Z2, Is.EqualTo(expected.Z2), $"{context} Z2 mismatch.");
            Assert.That(actual.Z3, Is.EqualTo(expected.Z3), $"{context} Z3 mismatch.");
            Assert.That(actual.Z4, Is.EqualTo(expected.Z4), $"{context} Z4 mismatch.");
            Assert.That(actual.Z5, Is.EqualTo(expected.Z5), $"{context} Z5 mismatch.");
            Assert.That(actual.Z6, Is.EqualTo(expected.Z6), $"{context} Z6 mismatch.");
            Assert.That(actual.Z7, Is.EqualTo(expected.Z7), $"{context} Z7 mismatch.");
        }

        private static void DisposeIfCreated(ref NativeArray<byte> array)
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private struct XorShift32
        {
            private uint _state;

            public XorShift32(uint seed)
            {
                _state = seed == 0u ? 1u : seed;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                uint range = (uint)(maxExclusive - minInclusive);
                return minInclusive + (int)(Next() % range);
            }

            private uint Next()
            {
                uint value = _state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                _state = value;
                return value;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct RunChunkFragmenterDatasetJob : IJob
        {
            [ReadOnly]
            public NativeArray<byte> SourceRows;

            public int Algorithm;
            public int ChunkCount;
            public NativeList<ChunkFragmenter.IslandMask> Islands;
            public NativeArray<int> Queue;
            public NativeArray<ulong> Result;

            public void Execute()
            {
                ulong totalIslands = 0UL;
                ulong checksum = 1469598103934665603UL;

                for (int chunkIndex = 0; chunkIndex < ChunkCount; chunkIndex++)
                {
                    int sourceOffset = chunkIndex * ChunkFragmenter.RowCount;

                    if (Algorithm == 0)
                    {
                        new ChunkFragmenterNaiveJob
                        {
                            SourceRows = SourceRows,
                            SourceOffset = sourceOffset,
                            Islands = Islands,
                            Queue = Queue
                        }.Execute();
                    }
                    else
                    {
                        new ChunkFragmenterOptimizedJob
                        {
                            SourceRows = SourceRows,
                            SourceOffset = sourceOffset,
                            Islands = Islands,
                            SliceQueue = Queue
                        }.Execute();
                    }

                    totalIslands += (uint)Islands.Length;
                    checksum = Mix(checksum, (uint)Islands.Length);

                    for (int islandIndex = 0; islandIndex < Islands.Length; islandIndex++)
                    {
                        ChunkFragmenter.IslandMask island = Islands[islandIndex];
                        checksum = Mix(checksum, island.Z0);
                        checksum = Mix(checksum, island.Z1);
                        checksum = Mix(checksum, island.Z2);
                        checksum = Mix(checksum, island.Z3);
                        checksum = Mix(checksum, island.Z4);
                        checksum = Mix(checksum, island.Z5);
                        checksum = Mix(checksum, island.Z6);
                        checksum = Mix(checksum, island.Z7);
                    }
                }

                Result[0] = totalIslands;
                Result[1] = checksum;
            }

            private static ulong Mix(ulong hash, ulong value)
            {
                hash ^= value;
                hash *= 1099511628211UL;
                return hash;
            }
        }
    }
}
