using System;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VoxelEngineModules.Shape.Debugger
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Shape/Shape Destruction Pipeline Benchmark")]
    public sealed class ShapeDestructionPipelineBenchmark : MonoBehaviour
    {
        private const int ChunkSize = ShapeDataContainer.ChunkSize;
        private const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        private const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;
        private const int BitPlaneBytesPerShape = ShapeDataContainer.BitPlaneBytesPerShape;

        [SerializeField] private bool _runOnStart = true;
        [SerializeField] private int[] _shapeCounts = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1000 };
        [SerializeField] private int _warmupIterations = 2;
        [SerializeField] private int _measurementIterations = 8;
        [SerializeField] private float _destructionRadiusInVoxels = 4.0f;
        [SerializeField] private int _minimalVoxelNumber = 2;
        [SerializeField] private bool _logStageBreakdown = true;
        [SerializeField] private bool _logEachIteration;

        public void Configure(
            int[] shapeCounts,
            int warmupIterations,
            int measurementIterations,
            float destructionRadiusInVoxels,
            int minimalVoxelNumber,
            bool logStageBreakdown,
            bool logEachIteration,
            bool runOnStart)
        {
            _shapeCounts = shapeCounts == null ? Array.Empty<int>() : (int[])shapeCounts.Clone();
            _warmupIterations = math.max(0, warmupIterations);
            _measurementIterations = math.max(1, measurementIterations);
            _destructionRadiusInVoxels = math.max(0.0f, destructionRadiusInVoxels);
            _minimalVoxelNumber = math.max(2, minimalVoxelNumber);
            _logStageBreakdown = logStageBreakdown;
            _logEachIteration = logEachIteration;
            _runOnStart = runOnStart;
        }

        private void Start()
        {
            if (_runOnStart)
            {
                RunBenchmark();
            }
        }

        [ContextMenu("Run Benchmark")]
        public void RunBenchmark()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Shape destruction benchmark should be run in Play Mode.", this);
                return;
            }

            int[] shapeCounts = SanitizeShapeCounts(_shapeCounts);
            if (shapeCounts.Length == 0)
            {
                Debug.LogWarning("Shape destruction benchmark has no valid shape counts.", this);
                return;
            }

            int warmupIterations = math.max(0, _warmupIterations);
            int measurementIterations = math.max(1, _measurementIterations);
            int minimalVoxelNumber = math.max(2, _minimalVoxelNumber);

            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine("Shape destruction pipeline benchmark");
            builder.AppendLine(
                $"warmup: {warmupIterations}, iterations: {measurementIterations}, " +
                $"radius: {_destructionRadiusInVoxels:F2}, minimal voxels: {minimalVoxelNumber}");

            for (int caseIndex = 0; caseIndex < shapeCounts.Length; caseIndex++)
            {
                int shapeCount = shapeCounts[caseIndex];
                for (int warmup = 0; warmup < warmupIterations; warmup++)
                {
                    RunBenchmarkIteration(shapeCount, minimalVoxelNumber, out _);
                }

                BenchmarkAggregate aggregate = default;
                for (int iteration = 0; iteration < measurementIterations; iteration++)
                {
                    if (!RunBenchmarkIteration(shapeCount, minimalVoxelNumber, out BenchmarkSample sample))
                    {
                        builder.AppendLine($"{shapeCount} shapes: skipped, no destructive work generated.");
                        break;
                    }

                    aggregate.Accumulate(sample);
                    if (_logEachIteration)
                    {
                        Debug.Log(FormatSample(sample, iteration), this);
                    }
                }

                if (aggregate.SampleCount > 0)
                {
                    AppendAggregate(builder, shapeCount, aggregate, _logStageBreakdown);
                }
            }

            Debug.Log(builder.ToString(), this);
        }

        public static bool RunIteration(
            int shapeCount,
            float destructionRadiusInVoxels,
            int minimalVoxelNumber,
            out BenchmarkSample sample)
        {
            return RunIteration(
                shapeCount,
                destructionRadiusInVoxels,
                minimalVoxelNumber,
                out sample,
                logContext: null);
        }

        private bool RunBenchmarkIteration(
            int shapeCount,
            int minimalVoxelNumber,
            out BenchmarkSample sample)
        {
            return RunIteration(
                shapeCount,
                _destructionRadiusInVoxels,
                minimalVoxelNumber,
                out sample,
                this);
        }

        private static bool RunIteration(
            int shapeCount,
            float destructionRadiusInVoxels,
            int minimalVoxelNumber,
            out BenchmarkSample sample,
            UnityEngine.Object logContext)
        {
            sample = default;
            ShapeDataStorage storage = null;
            NativeArray<int> destructionShapeHandles = default;
            NativeArray<float4x4> destructionShapeLocalToWorlds = default;
            NativeArray<byte> destructionMasks = default;
            NativeArray<int3> destructionChunkPositions = default;
            NativeArray<byte> destructionChunkUsed = default;
            NativeArray<int> targetShapeHandles = default;
            NativeArray<int> targetShapeRangeOffsets = default;
            NativeArray<float4x4> targetShapeLocalToWorlds = default;
            NativeArray<int> pairDestructionShapeHandles = default;
            NativeArray<byte> chunkOverlapMasks = default;
            NativeArray<int> chunkOverlapCounts = default;
            NativeArray<int> chunkOverlapOffsets = default;
            NativeArray<int> totalVoxelMaskBlockCount = default;
            NativeArray<int> commandShapeHandles = default;
            NativeArray<byte> commandChunkSlots = default;
            NativeArray<byte> commandMasks = default;
            NativeArray<ShapeVoxelRemoveCommandKey> commandKeys = default;
            NativeArray<int> affectedShapeHandleScratch = default;
            NativeArray<ShapeVoxelRemoveCommandRange> shapeRanges = default;
            NativeArray<int> uniqueShapeCount = default;
            NativeArray<int> affectedShapeHandles = default;
            NativeArray<byte> removeMasks = default;
            ShapeDestructionUpdatePipelineOutput updateOutput = default;

            try
            {
                storage = new ShapeDataStorage(shapeCount, Allocator.TempJob);
                targetShapeHandles = CreateFullSourceShapes(storage, shapeCount);
                ShapeDataView dataView = storage.GetShapeDataView(targetShapeHandles[0]);

                if (!CreateBenchmarkDestructionShape(
                        destructionRadiusInVoxels,
                        logContext,
                        out destructionShapeHandles,
                        out destructionShapeLocalToWorlds,
                        out destructionMasks,
                        out destructionChunkPositions,
                        out destructionChunkUsed))
                {
                    return false;
                }

                targetShapeRangeOffsets = new NativeArray<int>(
                    2,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                targetShapeRangeOffsets[0] = 0;
                targetShapeRangeOffsets[1] = targetShapeHandles.Length;

                targetShapeLocalToWorlds = new NativeArray<float4x4>(
                    storage.ShapeSlotCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                for (int i = 0; i < targetShapeHandles.Length; i++)
                {
                    targetShapeLocalToWorlds[targetShapeHandles[i]] = float4x4.identity;
                }

                int pairCount = targetShapeHandles.Length;
                int chunkOverlapCount = pairCount * ChunksPerShape;
                pairDestructionShapeHandles = new NativeArray<int>(
                    pairCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                chunkOverlapMasks = new NativeArray<byte>(
                    chunkOverlapCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                chunkOverlapCounts = new NativeArray<int>(
                    chunkOverlapCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                chunkOverlapOffsets = new NativeArray<int>(
                    chunkOverlapCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                totalVoxelMaskBlockCount = new NativeArray<int>(
                    1,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);

                BenchmarkStageTimings timings = default;
                long totalStartTimestamp = Stopwatch.GetTimestamp();
                long stageStartTimestamp = totalStartTimestamp;

                JobHandle overlapHandle = new DestructionShapeChunkOverlapJob
                {
                    DestructionShapeHandles = destructionShapeHandles,
                    TargetShapeHandles = targetShapeHandles,
                    TargetShapeRangeOffsets = targetShapeRangeOffsets,
                    DestructionShapeLocalToWorlds = destructionShapeLocalToWorlds,
                    TargetShapeLocalToWorlds = targetShapeLocalToWorlds,
                    DestructionChunkPositions = destructionChunkPositions,
                    DestructionChunkUsed = destructionChunkUsed,
                    TargetChunkPositions = dataView.ChunkPositions,
                    TargetChunkUsed = dataView.ChunkUsed,
                    PairDestructionShapeHandles = pairDestructionShapeHandles,
                    ChunkOverlapMasks = chunkOverlapMasks,
                    ChunkOverlapCounts = chunkOverlapCounts
                }.Schedule(destructionShapeHandles.Length, 1);
                timings.DestructionShapeChunkOverlapJob =
                    CompleteAndMeasure(overlapHandle, stageStartTimestamp);

                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle overlapPrefixHandle = new DestructionChunkOverlapPrefixSumJob
                {
                    ChunkOverlapCounts = chunkOverlapCounts,
                    ChunkOverlapOffsets = chunkOverlapOffsets,
                    TotalVoxelMaskBlockCount = totalVoxelMaskBlockCount
                }.Schedule(overlapHandle);
                timings.DestructionChunkOverlapPrefixSumJob =
                    CompleteAndMeasure(overlapPrefixHandle, stageStartTimestamp);

                int commandCount = totalVoxelMaskBlockCount[0];
                if (commandCount == 0)
                {
                    return false;
                }

                commandShapeHandles = new NativeArray<int>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                commandChunkSlots = new NativeArray<byte>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                commandMasks = new NativeArray<byte>(
                    commandCount * BitPlaneBytesPerChunk,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                ShapeVoxelRemoveCommandBuffer commandBuffer = new ShapeVoxelRemoveCommandBuffer
                {
                    ShapeHandles = commandShapeHandles,
                    ChunkSlots = commandChunkSlots,
                    Masks = commandMasks,
                    CommandCount = commandCount,
                    CommandCapacity = commandCount
                };

                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle maskGenerationHandle = DestructionMaskGenerationJob.Create(
                    pairDestructionShapeHandles,
                    targetShapeHandles,
                    chunkOverlapMasks,
                    chunkOverlapOffsets,
                    destructionShapeLocalToWorlds,
                    targetShapeLocalToWorlds,
                    destructionMasks,
                    destructionChunkPositions,
                    dataView.IsOccupied,
                    dataView.ChunkPositions,
                    commandBuffer).Schedule(chunkOverlapCount, ChunksPerShape);
                timings.DestructionMaskGenerationJob =
                    CompleteAndMeasure(maskGenerationHandle, stageStartTimestamp);

                commandKeys = new NativeArray<ShapeVoxelRemoveCommandKey>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle keyHandle = ShapeVoxelRemoveBuildKeyJob.Create(
                    commandBuffer,
                    commandKeys).Schedule(commandCount, 64, maskGenerationHandle);
                timings.ShapeVoxelRemoveBuildKeyJob =
                    CompleteAndMeasure(keyHandle, stageStartTimestamp);

                stageStartTimestamp = Stopwatch.GetTimestamp();
                commandKeys.Sort();
                timings.CommandKeySort = ElapsedMilliseconds(stageStartTimestamp);

                affectedShapeHandleScratch = new NativeArray<int>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                shapeRanges = new NativeArray<ShapeVoxelRemoveCommandRange>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                uniqueShapeCount = new NativeArray<int>(
                    1,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);

                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle buildRangeHandle = new ShapeVoxelRemoveBuildRangeJob
                {
                    SortedKeys = commandKeys,
                    AffectedShapeHandles = affectedShapeHandleScratch,
                    ShapeRanges = shapeRanges,
                    UniqueShapeCount = uniqueShapeCount,
                    KeyCount = commandCount
                }.Schedule();
                timings.ShapeVoxelRemoveBuildRangeJob =
                    CompleteAndMeasure(buildRangeHandle, stageStartTimestamp);

                int affectedShapeCount = uniqueShapeCount[0];
                if (affectedShapeCount == 0)
                {
                    return false;
                }

                affectedShapeHandles = new NativeArray<int>(
                    affectedShapeCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                NativeArray<int>.Copy(
                    affectedShapeHandleScratch,
                    affectedShapeHandles,
                    affectedShapeCount);

                removeMasks = new NativeArray<byte>(
                    affectedShapeCount * BitPlaneBytesPerShape,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle packHandle = ShapeVoxelRemovePackJob.Create(
                    commandBuffer,
                    commandKeys,
                    shapeRanges,
                    removeMasks).Schedule(affectedShapeCount, 1);
                timings.ShapeVoxelRemovePackJob =
                    CompleteAndMeasure(packHandle, stageStartTimestamp);

                updateOutput = new ShapeDestructionUpdatePipeline().Run(
                    affectedShapeHandles,
                    dataView,
                    removeMasks,
                    minimalVoxelNumber,
                    Allocator.TempJob,
                    (stage, elapsedMilliseconds) =>
                        timings = RecordUpdatePipelineTiming(
                            timings,
                            stage,
                            elapsedMilliseconds));

                double totalMilliseconds = ElapsedMilliseconds(totalStartTimestamp);
                sample = new BenchmarkSample
                {
                    ShapeCount = shapeCount,
                    AffectedShapeCount = affectedShapeCount,
                    BuiltShapeCount = updateOutput.BuiltShapeCount,
                    CommandCount = commandCount,
                    TotalMilliseconds = totalMilliseconds,
                    Timings = timings
                };
                return true;
            }
            finally
            {
                storage?.Dispose();
                DisposeIfCreated(destructionShapeHandles);
                DisposeIfCreated(destructionShapeLocalToWorlds);
                DisposeIfCreated(destructionMasks);
                DisposeIfCreated(destructionChunkPositions);
                DisposeIfCreated(destructionChunkUsed);
                DisposeIfCreated(targetShapeHandles);
                DisposeIfCreated(targetShapeRangeOffsets);
                DisposeIfCreated(targetShapeLocalToWorlds);
                DisposeIfCreated(pairDestructionShapeHandles);
                DisposeIfCreated(chunkOverlapMasks);
                DisposeIfCreated(chunkOverlapCounts);
                DisposeIfCreated(chunkOverlapOffsets);
                DisposeIfCreated(totalVoxelMaskBlockCount);
                DisposeIfCreated(commandShapeHandles);
                DisposeIfCreated(commandChunkSlots);
                DisposeIfCreated(commandMasks);
                DisposeIfCreated(commandKeys);
                DisposeIfCreated(affectedShapeHandleScratch);
                DisposeIfCreated(shapeRanges);
                DisposeIfCreated(uniqueShapeCount);
                DisposeIfCreated(affectedShapeHandles);
                DisposeIfCreated(removeMasks);
                updateOutput.Dispose();
            }
        }

        private static NativeArray<int> CreateFullSourceShapes(
            ShapeDataStorage storage,
            int shapeCount)
        {
            NativeArray<int> shapeHandles = new NativeArray<int>(
                shapeCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
            {
                int shapeHandle = storage.Acquire(shapeIndex + 1);
                if (shapeHandle == ShapeDataStorage.InvalidHandle)
                {
                    throw new InvalidOperationException("Shape benchmark storage ran out of shape handles.");
                }

                shapeHandles[shapeIndex] = shapeHandle;
                ShapeDataView dataView = storage.GetShapeDataView(shapeHandle);
                WriteFullBenchmarkShape(dataView, shapeHandle, shapeIndex + 1);
            }

            return shapeHandles;
        }

        private static void WriteFullBenchmarkShape(
            ShapeDataView dataView,
            int shapeHandle,
            int bodyHandle)
        {
            NativeArray<ShapeMetadata> shapes = dataView.Shapes;
            NativeArray<int3> chunkPositions = dataView.ChunkPositions;
            NativeArray<byte> chunkUsed = dataView.ChunkUsed;
            shapes[shapeHandle] = new ShapeMetadata
            {
                BodyHandle = bodyHandle,
                IsUsed = 1
            };

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkIndex = shapeHandle * ChunksPerShape + chunkSlot;
                WriteFullChunk(dataView.IsOccupied, chunkIndex);
                chunkPositions[chunkIndex] = BenchmarkChunkPosition(chunkSlot);
                chunkUsed[chunkIndex] = 1;
            }
        }

        private static bool CreateBenchmarkDestructionShape(
            float destructionRadiusInVoxels,
            UnityEngine.Object logContext,
            out NativeArray<int> destructionShapeHandles,
            out NativeArray<float4x4> destructionShapeLocalToWorlds,
            out NativeArray<byte> destructionMasks,
            out NativeArray<int3> destructionChunkPositions,
            out NativeArray<byte> destructionChunkUsed)
        {
            destructionShapeHandles = default;
            destructionShapeLocalToWorlds = default;
            destructionMasks = default;
            destructionChunkPositions = default;
            destructionChunkUsed = default;

            int maskDimension = CreateDestructionMaskDimension(destructionRadiusInVoxels);
            int3 maskDimensions = new int3(maskDimension, maskDimension, maskDimension);
            int chunksX = (maskDimensions.x + ChunkSize - 1) / ChunkSize;
            int chunksY = (maskDimensions.y + ChunkSize - 1) / ChunkSize;
            int chunksZ = (maskDimensions.z + ChunkSize - 1) / ChunkSize;
            int destructionChunkCount = chunksX * chunksY * chunksZ;
            if (destructionChunkCount > ChunksPerShape)
            {
                Debug.LogWarning(
                    "Shape destruction benchmark radius is too large for one 8-chunk destruction shape.",
                    logContext);
                return false;
            }

            destructionShapeHandles = new NativeArray<int>(
                1,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            destructionShapeHandles[0] = 0;

            float3 maskCenter = new float3(maskDimensions) * 0.5f;
            Matrix4x4 destructionLocalToWorld =
                Matrix4x4.Translate(new Vector3(8.0f, 8.0f, 8.0f) - (Vector3)maskCenter);
            destructionShapeLocalToWorlds = new NativeArray<float4x4>(
                1,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            destructionShapeLocalToWorlds[0] = ToFloat4x4(destructionLocalToWorld);

            destructionMasks = new NativeArray<byte>(
                ChunksPerShape * BitPlaneBytesPerChunk,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            destructionChunkPositions = new NativeArray<int3>(
                ChunksPerShape,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            destructionChunkUsed = new NativeArray<byte>(
                ChunksPerShape,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            WriteDestructionChunkSlots(
                chunksX,
                chunksY,
                chunksZ,
                destructionChunkPositions,
                destructionChunkUsed);
            WriteSphereDestructionMask(
                destructionRadiusInVoxels,
                maskDimensions,
                chunksX,
                chunksY,
                destructionMasks);
            return true;
        }

        private static void WriteDestructionChunkSlots(
            int chunksX,
            int chunksY,
            int chunksZ,
            NativeArray<int3> destructionChunkPositions,
            NativeArray<byte> destructionChunkUsed)
        {
            int chunkSlot = 0;
            for (int z = 0; z < chunksZ; z++)
            {
                for (int y = 0; y < chunksY; y++)
                {
                    for (int x = 0; x < chunksX; x++)
                    {
                        destructionChunkPositions[chunkSlot] = new int3(x, y, z);
                        destructionChunkUsed[chunkSlot] = 1;
                        chunkSlot++;
                    }
                }
            }
        }

        private static void WriteSphereDestructionMask(
            float destructionRadiusInVoxels,
            int3 maskDimensions,
            int chunksX,
            int chunksY,
            NativeArray<byte> destructionMasks)
        {
            float radius = Mathf.Max(0.0f, destructionRadiusInVoxels);
            float radiusSquared = radius * radius;
            float3 center = new float3(maskDimensions) * 0.5f;

            for (int z = 0; z < maskDimensions.z; z++)
            {
                for (int y = 0; y < maskDimensions.y; y++)
                {
                    for (int x = 0; x < maskDimensions.x; x++)
                    {
                        float3 voxelCenter = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
                        if (math.lengthsq(voxelCenter - center) > radiusSquared)
                        {
                            continue;
                        }

                        int chunkX = x / ChunkSize;
                        int chunkY = y / ChunkSize;
                        int chunkZ = z / ChunkSize;
                        int chunkSlot = (chunkZ * chunksY + chunkY) * chunksX + chunkX;
                        int rowIndex = RowIndex(
                            y & (ChunkSize - 1),
                            z & (ChunkSize - 1));
                        int maskIndex = chunkSlot * BitPlaneBytesPerChunk + rowIndex;
                        destructionMasks[maskIndex] =
                            (byte)(destructionMasks[maskIndex] | (1 << (x & (ChunkSize - 1))));
                    }
                }
            }
        }

        private static void AppendAggregate(
            StringBuilder builder,
            int shapeCount,
            BenchmarkAggregate aggregate,
            bool includeStageBreakdown)
        {
            BenchmarkStageTimings averageTimings = aggregate.AverageTimings;
            double averageTotal = aggregate.AverageTotalMilliseconds;
            double averageMeasured = averageTimings.TotalMeasuredMilliseconds;
            double averageOverhead = Math.Max(0.0, averageTotal - averageMeasured);
            builder.AppendLine(
                $"{shapeCount} shapes: avg {averageTotal:F3} ms, " +
                $"min {aggregate.MinTotalMilliseconds:F3} ms, " +
                $"max {aggregate.MaxTotalMilliseconds:F3} ms, " +
                $"measured {averageMeasured:F3} ms, overhead {averageOverhead:F3} ms, " +
                $"affected avg {aggregate.AverageAffectedShapeCount:F1}, " +
                $"built avg {aggregate.AverageBuiltShapeCount:F1}, " +
                $"commands avg {aggregate.AverageCommandCount:F1}");

            if (!includeStageBreakdown)
            {
                return;
            }

            builder.AppendLine($"  DestructionShapeChunkOverlapJob: {averageTimings.DestructionShapeChunkOverlapJob:F3} ms");
            builder.AppendLine($"  DestructionChunkOverlapPrefixSumJob: {averageTimings.DestructionChunkOverlapPrefixSumJob:F3} ms");
            builder.AppendLine($"  DestructionMaskGenerationJob: {averageTimings.DestructionMaskGenerationJob:F3} ms");
            builder.AppendLine($"  ShapeVoxelRemoveBuildKeyJob: {averageTimings.ShapeVoxelRemoveBuildKeyJob:F3} ms");
            builder.AppendLine($"  CommandKeySort: {averageTimings.CommandKeySort:F3} ms");
            builder.AppendLine($"  ShapeVoxelRemoveBuildRangeJob: {averageTimings.ShapeVoxelRemoveBuildRangeJob:F3} ms");
            builder.AppendLine($"  ShapeVoxelRemovePackJob: {averageTimings.ShapeVoxelRemovePackJob:F3} ms");
            builder.AppendLine($"  ShapeVoxelRemoveJob: {averageTimings.ShapeVoxelRemoveJob:F3} ms");
            builder.AppendLine($"  ShapeChunkFragmentJob: {averageTimings.ShapeChunkFragmentJob:F3} ms");
            builder.AppendLine($"  ShapeChunkCheckMaskJob: {averageTimings.ShapeChunkCheckMaskJob:F3} ms");
            builder.AppendLine($"  ShapeChunkConnectivityJob: {averageTimings.ShapeChunkConnectivityJob:F3} ms");
            builder.AppendLine($"  ShapeFragmentUnionJob: {averageTimings.ShapeFragmentUnionJob:F3} ms");
            builder.AppendLine($"  ShapePrefixSumComputeJob: {averageTimings.ShapePrefixSumComputeJob:F3} ms");
            builder.AppendLine($"  ShapeBuildJob: {averageTimings.ShapeBuildJob:F3} ms");
        }

        private static string FormatSample(BenchmarkSample sample, int iteration)
        {
            double measured = sample.Timings.TotalMeasuredMilliseconds;
            double overhead = Math.Max(0.0, sample.TotalMilliseconds - measured);
            return
                $"Shape destruction benchmark iteration {iteration + 1}, " +
                $"{sample.ShapeCount} shapes: total {sample.TotalMilliseconds:F3} ms, " +
                $"measured {measured:F3} ms, overhead {overhead:F3} ms, " +
                $"affected {sample.AffectedShapeCount}, built {sample.BuiltShapeCount}, " +
                $"commands {sample.CommandCount}.";
        }

        private static int[] SanitizeShapeCounts(int[] shapeCounts)
        {
            if (shapeCounts == null || shapeCounts.Length == 0)
            {
                return Array.Empty<int>();
            }

            int validCount = 0;
            for (int i = 0; i < shapeCounts.Length; i++)
            {
                if (shapeCounts[i] > 0)
                {
                    validCount++;
                }
            }

            int[] result = new int[validCount];
            int writeIndex = 0;
            for (int i = 0; i < shapeCounts.Length; i++)
            {
                if (shapeCounts[i] > 0)
                {
                    result[writeIndex++] = shapeCounts[i];
                }
            }

            return result;
        }

        private static double CompleteAndMeasure(JobHandle handle, long startTimestamp)
        {
            handle.Complete();
            return ElapsedMilliseconds(startTimestamp);
        }

        private static BenchmarkStageTimings RecordUpdatePipelineTiming(
            BenchmarkStageTimings timings,
            ShapeDestructionUpdatePipelineStage stage,
            double elapsedMilliseconds)
        {
            switch (stage)
            {
                case ShapeDestructionUpdatePipelineStage.ShapeVoxelRemoveJob:
                    timings.ShapeVoxelRemoveJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeChunkFragmentJob:
                    timings.ShapeChunkFragmentJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeChunkCheckMaskJob:
                    timings.ShapeChunkCheckMaskJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeChunkConnectivityJob:
                    timings.ShapeChunkConnectivityJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeFragmentUnionJob:
                    timings.ShapeFragmentUnionJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapePrefixSumComputeJob:
                    timings.ShapePrefixSumComputeJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeBuildJob:
                    timings.ShapeBuildJob = elapsedMilliseconds;
                    break;
            }

            return timings;
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            return TicksToMilliseconds(Stopwatch.GetTimestamp() - startTimestamp);
        }

        private static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static void DisposeIfCreated<T>(NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private static void WriteFullChunk(NativeArray<byte> isOccupied, int chunkIndex)
        {
            int chunkBase = chunkIndex * BitPlaneBytesPerChunk;
            for (int row = 0; row < BitPlaneBytesPerChunk; row++)
            {
                isOccupied[chunkBase + row] = 0xFF;
            }
        }

        private static int3 BenchmarkChunkPosition(int chunkSlot)
        {
            return new int3(
                chunkSlot & 1,
                (chunkSlot >> 1) & 1,
                (chunkSlot >> 2) & 1);
        }

        private static int CreateDestructionMaskDimension(float radiusInVoxels)
        {
            int dimension = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.0f, radiusInVoxels) * 2.0f) + 3);
            return (dimension & 1) == 0 ? dimension + 1 : dimension;
        }

        private static float4x4 ToFloat4x4(Matrix4x4 matrix)
        {
            return new float4x4(
                new float4(matrix.m00, matrix.m10, matrix.m20, matrix.m30),
                new float4(matrix.m01, matrix.m11, matrix.m21, matrix.m31),
                new float4(matrix.m02, matrix.m12, matrix.m22, matrix.m32),
                new float4(matrix.m03, matrix.m13, matrix.m23, matrix.m33));
        }

        private static int RowIndex(int y, int z)
        {
            return y + ChunkSize * z;
        }

        public struct BenchmarkSample
        {
            public int ShapeCount;
            public int AffectedShapeCount;
            public int BuiltShapeCount;
            public int CommandCount;
            public double TotalMilliseconds;
            public BenchmarkStageTimings Timings;
        }

        private struct BenchmarkAggregate
        {
            private double _totalMilliseconds;
            private double _minTotalMilliseconds;
            private double _maxTotalMilliseconds;
            private int _affectedShapeCount;
            private int _builtShapeCount;
            private int _commandCount;
            private BenchmarkStageTimings _timings;

            public int SampleCount { get; private set; }

            public double AverageTotalMilliseconds =>
                SampleCount == 0 ? 0.0 : _totalMilliseconds / SampleCount;

            public double MinTotalMilliseconds => SampleCount == 0 ? 0.0 : _minTotalMilliseconds;

            public double MaxTotalMilliseconds => SampleCount == 0 ? 0.0 : _maxTotalMilliseconds;

            public double AverageAffectedShapeCount =>
                SampleCount == 0 ? 0.0 : (double)_affectedShapeCount / SampleCount;

            public double AverageBuiltShapeCount =>
                SampleCount == 0 ? 0.0 : (double)_builtShapeCount / SampleCount;

            public double AverageCommandCount =>
                SampleCount == 0 ? 0.0 : (double)_commandCount / SampleCount;

            public BenchmarkStageTimings AverageTimings =>
                SampleCount == 0 ? default : _timings.Divide(SampleCount);

            public void Accumulate(BenchmarkSample sample)
            {
                if (SampleCount == 0)
                {
                    _minTotalMilliseconds = sample.TotalMilliseconds;
                    _maxTotalMilliseconds = sample.TotalMilliseconds;
                }
                else
                {
                    _minTotalMilliseconds = Math.Min(_minTotalMilliseconds, sample.TotalMilliseconds);
                    _maxTotalMilliseconds = Math.Max(_maxTotalMilliseconds, sample.TotalMilliseconds);
                }

                SampleCount++;
                _totalMilliseconds += sample.TotalMilliseconds;
                _affectedShapeCount += sample.AffectedShapeCount;
                _builtShapeCount += sample.BuiltShapeCount;
                _commandCount += sample.CommandCount;
                _timings.Add(sample.Timings);
            }
        }

        public struct BenchmarkStageTimings
        {
            public double DestructionShapeChunkOverlapJob;
            public double DestructionChunkOverlapPrefixSumJob;
            public double DestructionMaskGenerationJob;
            public double ShapeVoxelRemoveBuildKeyJob;
            public double CommandKeySort;
            public double ShapeVoxelRemoveBuildRangeJob;
            public double ShapeVoxelRemovePackJob;
            public double ShapeVoxelRemoveJob;
            public double ShapeChunkFragmentJob;
            public double ShapeChunkCheckMaskJob;
            public double ShapeChunkConnectivityJob;
            public double ShapeFragmentUnionJob;
            public double ShapePrefixSumComputeJob;
            public double ShapeBuildJob;

            public double TotalMeasuredMilliseconds =>
                DestructionShapeChunkOverlapJob +
                DestructionChunkOverlapPrefixSumJob +
                DestructionMaskGenerationJob +
                ShapeVoxelRemoveBuildKeyJob +
                CommandKeySort +
                ShapeVoxelRemoveBuildRangeJob +
                ShapeVoxelRemovePackJob +
                ShapeVoxelRemoveJob +
                ShapeChunkFragmentJob +
                ShapeChunkCheckMaskJob +
                ShapeChunkConnectivityJob +
                ShapeFragmentUnionJob +
                ShapePrefixSumComputeJob +
                ShapeBuildJob;

            public void Add(BenchmarkStageTimings other)
            {
                DestructionShapeChunkOverlapJob += other.DestructionShapeChunkOverlapJob;
                DestructionChunkOverlapPrefixSumJob += other.DestructionChunkOverlapPrefixSumJob;
                DestructionMaskGenerationJob += other.DestructionMaskGenerationJob;
                ShapeVoxelRemoveBuildKeyJob += other.ShapeVoxelRemoveBuildKeyJob;
                CommandKeySort += other.CommandKeySort;
                ShapeVoxelRemoveBuildRangeJob += other.ShapeVoxelRemoveBuildRangeJob;
                ShapeVoxelRemovePackJob += other.ShapeVoxelRemovePackJob;
                ShapeVoxelRemoveJob += other.ShapeVoxelRemoveJob;
                ShapeChunkFragmentJob += other.ShapeChunkFragmentJob;
                ShapeChunkCheckMaskJob += other.ShapeChunkCheckMaskJob;
                ShapeChunkConnectivityJob += other.ShapeChunkConnectivityJob;
                ShapeFragmentUnionJob += other.ShapeFragmentUnionJob;
                ShapePrefixSumComputeJob += other.ShapePrefixSumComputeJob;
                ShapeBuildJob += other.ShapeBuildJob;
            }

            public BenchmarkStageTimings Divide(double divisor)
            {
                if (divisor <= 0.0)
                {
                    return default;
                }

                return new BenchmarkStageTimings
                {
                    DestructionShapeChunkOverlapJob = DestructionShapeChunkOverlapJob / divisor,
                    DestructionChunkOverlapPrefixSumJob = DestructionChunkOverlapPrefixSumJob / divisor,
                    DestructionMaskGenerationJob = DestructionMaskGenerationJob / divisor,
                    ShapeVoxelRemoveBuildKeyJob = ShapeVoxelRemoveBuildKeyJob / divisor,
                    CommandKeySort = CommandKeySort / divisor,
                    ShapeVoxelRemoveBuildRangeJob = ShapeVoxelRemoveBuildRangeJob / divisor,
                    ShapeVoxelRemovePackJob = ShapeVoxelRemovePackJob / divisor,
                    ShapeVoxelRemoveJob = ShapeVoxelRemoveJob / divisor,
                    ShapeChunkFragmentJob = ShapeChunkFragmentJob / divisor,
                    ShapeChunkCheckMaskJob = ShapeChunkCheckMaskJob / divisor,
                    ShapeChunkConnectivityJob = ShapeChunkConnectivityJob / divisor,
                    ShapeFragmentUnionJob = ShapeFragmentUnionJob / divisor,
                    ShapePrefixSumComputeJob = ShapePrefixSumComputeJob / divisor,
                    ShapeBuildJob = ShapeBuildJob / divisor
                };
            }
        }
    }
}
