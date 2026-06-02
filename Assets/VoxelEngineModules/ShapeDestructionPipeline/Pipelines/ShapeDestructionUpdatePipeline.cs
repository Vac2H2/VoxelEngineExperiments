using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    public enum ShapeDestructionUpdatePipelineStage
    {
        ShapeVoxelRemoveJob,
        ShapeChunkFragmentJob,
        ShapeChunkCheckMaskJob,
        ShapeChunkConnectivityJob,
        ShapeFragmentUnionJob,
        ShapePrefixSumComputeJob,
        ShapeBuildJob
    }

    public delegate void ShapeDestructionUpdatePipelineStageCompleted(
        ShapeDestructionUpdatePipelineStage stage,
        double elapsedMilliseconds);

    public struct ShapeDestructionUpdatePipelineInput
    {
        public NativeArray<int> AffectedShapeHandles;
        public NativeArray<byte> SourceIsOccupied;
        public NativeArray<byte> RemoveMasks;
        public NativeArray<int3> ChunkPositions;
        public NativeArray<byte> ChunkUsed;
        public int MinimalVoxelNumber;
        public Allocator OutputAllocator;
    }

    public struct ShapeDestructionUpdatePipelineOutput : IDisposable
    {
        public NativeArray<int> LocalShapeCounts;
        public NativeArray<int> BuiltShapeOffsets;
        public NativeArray<BuiltChunkMetadata> BuiltChunks;
        public NativeArray<byte> BuiltIsOccupied;
        public int BuiltShapeCount;

        public readonly bool IsCreated =>
            LocalShapeCounts.IsCreated ||
            BuiltShapeOffsets.IsCreated ||
            BuiltChunks.IsCreated ||
            BuiltIsOccupied.IsCreated;

        public void Dispose()
        {
            DisposeIfCreated(LocalShapeCounts);
            DisposeIfCreated(BuiltShapeOffsets);
            DisposeIfCreated(BuiltChunks);
            DisposeIfCreated(BuiltIsOccupied);

            LocalShapeCounts = default;
            BuiltShapeOffsets = default;
            BuiltChunks = default;
            BuiltIsOccupied = default;
            BuiltShapeCount = 0;
        }

        private static void DisposeIfCreated<T>(NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }
    }

    public sealed class ShapeDestructionUpdatePipeline
    {
        private const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        private const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;
        private const int BitPlaneBytesPerShape = ShapeDataContainer.BitPlaneBytesPerShape;

        public ShapeDestructionUpdatePipelineOutput Run(
            NativeArray<int> affectedShapeHandles,
            ShapeDataView dataView,
            NativeArray<byte> removeMasks,
            int minimalVoxelNumber)
        {
            return Run(
                affectedShapeHandles,
                dataView,
                removeMasks,
                minimalVoxelNumber,
                Allocator.TempJob);
        }

        public ShapeDestructionUpdatePipelineOutput Run(
            NativeArray<int> affectedShapeHandles,
            ShapeDataView dataView,
            NativeArray<byte> removeMasks,
            int minimalVoxelNumber,
            Allocator outputAllocator,
            ShapeDestructionUpdatePipelineStageCompleted stageCompleted = null)
        {
            return Run(
                new ShapeDestructionUpdatePipelineInput
                {
                    AffectedShapeHandles = affectedShapeHandles,
                    SourceIsOccupied = dataView.IsOccupied,
                    RemoveMasks = removeMasks,
                    ChunkPositions = dataView.ChunkPositions,
                    ChunkUsed = dataView.ChunkUsed,
                    MinimalVoxelNumber = minimalVoxelNumber,
                    OutputAllocator = outputAllocator
                },
                stageCompleted);
        }

        public ShapeDestructionUpdatePipelineOutput Run(
            ShapeDestructionUpdatePipelineInput input,
            ShapeDestructionUpdatePipelineStageCompleted stageCompleted = null)
        {
            ValidateInput(input);

            int affectedShapeCount = input.AffectedShapeHandles.Length;
            int shapeChunkCount = checked(affectedShapeCount * ChunksPerShape);
            int fragmentNodeCount = ShapeFragmentUnionJob.MaxFragmentNodesPerShape;
            int fragmentMaskCount = checked(
                affectedShapeCount *
                fragmentNodeCount *
                ShapeChunkCheckMaskJob.PositiveDirectionCount);

            NativeArray<byte> removedIsOccupied = default;
            NativeArray<int> sliceQueue = default;
            NativeArray<byte> fragmentIsOccupied = default;
            NativeArray<byte> fragmentCounts = default;
            NativeArray<uint> fragmentCheckMasks = default;
            NativeArray<uint> fragmentConnectionMasks = default;
            NativeArray<int> fragmentLocalShapeIds = default;
            NativeArray<int> localShapeCounts = default;
            NativeArray<int> builtShapeOffsets = default;
            NativeArray<int> totalBuiltShapeCount = default;
            NativeArray<BuiltChunkMetadata> builtChunks = default;
            NativeArray<byte> builtIsOccupied = default;
            bool outputTransferred = false;

            try
            {
                removedIsOccupied = new NativeArray<byte>(
                    checked(affectedShapeCount * BitPlaneBytesPerShape),
                    input.OutputAllocator,
                    NativeArrayOptions.UninitializedMemory);
                sliceQueue = new NativeArray<int>(
                    checked(shapeChunkCount * ShapeChunkFragmentJob.MaxSliceCount),
                    input.OutputAllocator,
                    NativeArrayOptions.UninitializedMemory);
                fragmentIsOccupied = new NativeArray<byte>(
                    checked(affectedShapeCount * fragmentNodeCount * BitPlaneBytesPerChunk),
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);
                fragmentCounts = new NativeArray<byte>(
                    shapeChunkCount,
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);
                fragmentCheckMasks = new NativeArray<uint>(
                    fragmentMaskCount,
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);
                fragmentConnectionMasks = new NativeArray<uint>(
                    fragmentMaskCount,
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);
                fragmentLocalShapeIds = new NativeArray<int>(
                    checked(affectedShapeCount * fragmentNodeCount),
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);
                localShapeCounts = new NativeArray<int>(
                    affectedShapeCount,
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);
                builtShapeOffsets = new NativeArray<int>(
                    affectedShapeCount,
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);
                totalBuiltShapeCount = new NativeArray<int>(
                    1,
                    input.OutputAllocator,
                    NativeArrayOptions.ClearMemory);

                long stageStartTimestamp = StartTiming(stageCompleted);
                JobHandle removeHandle = new ShapeVoxelRemoveJob
                {
                    ShapeHandles = input.AffectedShapeHandles,
                    SourceIsOccupied = input.SourceIsOccupied,
                    RemoveMasks = input.RemoveMasks,
                    TargetIsOccupied = removedIsOccupied
                }.Schedule(affectedShapeCount, 1);
                removeHandle = CompleteStageIfMeasuring(
                    removeHandle,
                    ShapeDestructionUpdatePipelineStage.ShapeVoxelRemoveJob,
                    stageStartTimestamp,
                    stageCompleted);

                stageStartTimestamp = StartTiming(stageCompleted);
                JobHandle chunkFragmentHandle = new ShapeChunkFragmentJob
                {
                    ShapeHandles = input.AffectedShapeHandles,
                    ChunkUsed = input.ChunkUsed,
                    SourceIsOccupied = removedIsOccupied,
                    SliceQueue = sliceQueue,
                    OutputIsOccupied = fragmentIsOccupied,
                    OutputGeneratedChunkCounts = fragmentCounts,
                    MinimalVoxelNumber = input.MinimalVoxelNumber
                }.Schedule(shapeChunkCount, 1, removeHandle);
                chunkFragmentHandle = CompleteStageIfMeasuring(
                    chunkFragmentHandle,
                    ShapeDestructionUpdatePipelineStage.ShapeChunkFragmentJob,
                    stageStartTimestamp,
                    stageCompleted);

                stageStartTimestamp = StartTiming(stageCompleted);
                JobHandle checkMaskHandle = new ShapeChunkCheckMaskJob
                {
                    ShapeHandles = input.AffectedShapeHandles,
                    ChunkPositions = input.ChunkPositions,
                    ChunkUsed = input.ChunkUsed,
                    FragmentCounts = fragmentCounts,
                    FragmentCheckMasks = fragmentCheckMasks
                }.Schedule(affectedShapeCount, 1, chunkFragmentHandle);
                checkMaskHandle = CompleteStageIfMeasuring(
                    checkMaskHandle,
                    ShapeDestructionUpdatePipelineStage.ShapeChunkCheckMaskJob,
                    stageStartTimestamp,
                    stageCompleted);

                stageStartTimestamp = StartTiming(stageCompleted);
                JobHandle connectivityHandle = new ShapeChunkConnectivityJob
                {
                    FragmentIsOccupied = fragmentIsOccupied,
                    FragmentCounts = fragmentCounts,
                    FragmentCheckMasks = fragmentCheckMasks,
                    FragmentConnectionMasks = fragmentConnectionMasks
                }.Schedule(
                    checked(affectedShapeCount * fragmentNodeCount),
                    fragmentNodeCount,
                    checkMaskHandle);
                connectivityHandle = CompleteStageIfMeasuring(
                    connectivityHandle,
                    ShapeDestructionUpdatePipelineStage.ShapeChunkConnectivityJob,
                    stageStartTimestamp,
                    stageCompleted);

                stageStartTimestamp = StartTiming(stageCompleted);
                JobHandle unionHandle = new ShapeFragmentUnionJob
                {
                    FragmentCounts = fragmentCounts,
                    FragmentConnectionMasks = fragmentConnectionMasks,
                    FragmentLocalShapeIds = fragmentLocalShapeIds,
                    LocalShapeCounts = localShapeCounts
                }.Schedule(affectedShapeCount, 1, connectivityHandle);
                unionHandle = CompleteStageIfMeasuring(
                    unionHandle,
                    ShapeDestructionUpdatePipelineStage.ShapeFragmentUnionJob,
                    stageStartTimestamp,
                    stageCompleted);

                stageStartTimestamp = StartTiming(stageCompleted);
                JobHandle prefixSumHandle = new ShapePrefixSumComputeJob
                {
                    LocalShapeCounts = localShapeCounts,
                    BuiltShapeOffsets = builtShapeOffsets,
                    TotalBuiltShapeCount = totalBuiltShapeCount
                }.Schedule(unionHandle);
                CompleteRequiredStage(
                    prefixSumHandle,
                    ShapeDestructionUpdatePipelineStage.ShapePrefixSumComputeJob,
                    stageStartTimestamp,
                    stageCompleted);

                int builtShapeCount = totalBuiltShapeCount[0];
                builtChunks = new NativeArray<BuiltChunkMetadata>(
                    checked(builtShapeCount * ShapeBuildJob.MaxBuiltChunksPerShape),
                    input.OutputAllocator,
                    NativeArrayOptions.UninitializedMemory);
                builtIsOccupied = new NativeArray<byte>(
                    checked(
                        builtShapeCount *
                        ShapeBuildJob.MaxBuiltChunksPerShape *
                        BitPlaneBytesPerChunk),
                    input.OutputAllocator,
                    NativeArrayOptions.UninitializedMemory);

                stageStartTimestamp = StartTiming(stageCompleted);
                JobHandle buildHandle = new ShapeBuildJob
                {
                    ShapeHandles = input.AffectedShapeHandles,
                    ChunkPositions = input.ChunkPositions,
                    FragmentIsOccupied = fragmentIsOccupied,
                    FragmentLocalShapeIds = fragmentLocalShapeIds,
                    LocalShapeCounts = localShapeCounts,
                    BuiltShapeOffsets = builtShapeOffsets,
                    BuiltChunks = builtChunks,
                    BuiltIsOccupied = builtIsOccupied
                }.Schedule(affectedShapeCount, 1);
                CompleteRequiredStage(
                    buildHandle,
                    ShapeDestructionUpdatePipelineStage.ShapeBuildJob,
                    stageStartTimestamp,
                    stageCompleted);

                outputTransferred = true;
                return new ShapeDestructionUpdatePipelineOutput
                {
                    LocalShapeCounts = localShapeCounts,
                    BuiltShapeOffsets = builtShapeOffsets,
                    BuiltChunks = builtChunks,
                    BuiltIsOccupied = builtIsOccupied,
                    BuiltShapeCount = builtShapeCount
                };
            }
            finally
            {
                DisposeIfCreated(removedIsOccupied);
                DisposeIfCreated(sliceQueue);
                DisposeIfCreated(fragmentIsOccupied);
                DisposeIfCreated(fragmentCounts);
                DisposeIfCreated(fragmentCheckMasks);
                DisposeIfCreated(fragmentConnectionMasks);
                DisposeIfCreated(fragmentLocalShapeIds);
                DisposeIfCreated(totalBuiltShapeCount);

                if (!outputTransferred)
                {
                    DisposeIfCreated(localShapeCounts);
                    DisposeIfCreated(builtShapeOffsets);
                    DisposeIfCreated(builtChunks);
                    DisposeIfCreated(builtIsOccupied);
                }
            }
        }

        private static JobHandle CompleteStageIfMeasuring(
            JobHandle handle,
            ShapeDestructionUpdatePipelineStage stage,
            long startTimestamp,
            ShapeDestructionUpdatePipelineStageCompleted stageCompleted)
        {
            if (stageCompleted == null)
            {
                return handle;
            }

            handle.Complete();
            stageCompleted(stage, ElapsedMilliseconds(startTimestamp));
            return handle;
        }

        private static void CompleteRequiredStage(
            JobHandle handle,
            ShapeDestructionUpdatePipelineStage stage,
            long startTimestamp,
            ShapeDestructionUpdatePipelineStageCompleted stageCompleted)
        {
            handle.Complete();
            if (stageCompleted != null)
            {
                stageCompleted(stage, ElapsedMilliseconds(startTimestamp));
            }
        }

        private static long StartTiming(
            ShapeDestructionUpdatePipelineStageCompleted stageCompleted)
        {
            return stageCompleted == null ? 0L : Stopwatch.GetTimestamp();
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) *
                   1000.0 /
                   Stopwatch.Frequency;
        }

        private static void ValidateInput(ShapeDestructionUpdatePipelineInput input)
        {
            if (!input.AffectedShapeHandles.IsCreated)
            {
                throw new ArgumentException(
                    "Affected shape handles must be created.",
                    nameof(input));
            }

            if (!input.SourceIsOccupied.IsCreated)
            {
                throw new ArgumentException(
                    "Source occupancy must be created.",
                    nameof(input));
            }

            if (!input.RemoveMasks.IsCreated)
            {
                throw new ArgumentException(
                    "Remove masks must be created.",
                    nameof(input));
            }

            if (!input.ChunkPositions.IsCreated)
            {
                throw new ArgumentException(
                    "Chunk positions must be created.",
                    nameof(input));
            }

            if (!input.ChunkUsed.IsCreated)
            {
                throw new ArgumentException(
                    "Chunk used flags must be created.",
                    nameof(input));
            }

            if (input.OutputAllocator != Allocator.TempJob &&
                input.OutputAllocator != Allocator.Persistent)
            {
                throw new ArgumentException(
                    "Output allocator must be TempJob or Persistent.",
                    nameof(input));
            }

            int affectedShapeCount = input.AffectedShapeHandles.Length;
            ValidateLength(
                input.RemoveMasks,
                checked(affectedShapeCount * BitPlaneBytesPerShape),
                nameof(input.RemoveMasks));
        }

        private static void ValidateLength<T>(
            NativeArray<T> array,
            int minimumLength,
            string name)
            where T : struct
        {
            if (array.Length < minimumLength)
            {
                throw new ArgumentException(
                    $"{name} length {array.Length} is less than required length {minimumLength}.");
            }
        }

        private static void DisposeIfCreated<T>(NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }
    }
}
