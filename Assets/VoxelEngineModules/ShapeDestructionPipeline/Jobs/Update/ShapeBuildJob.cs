using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapeBuildJob : IJobParallelFor
    {
        public const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int MaxFragmentsPerChunk = ShapeChunkFragmentJob.MaxGeneratedChunksPerChunk;
        public const int MaxBuiltShapesPerSourceShape = ShapeFragmentUnionJob.MaxFragmentNodesPerShape;
        public const int MaxBuiltChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;

        [ReadOnly]
        public NativeArray<int> ShapeHandles;

        [ReadOnly]
        public NativeArray<int3> ChunkPositions;

        [ReadOnly]
        public NativeArray<byte> FragmentIsOccupied;

        [ReadOnly]
        public NativeArray<int> FragmentLocalShapeIds;

        [ReadOnly]
        public NativeArray<int> LocalShapeCounts;

        [ReadOnly]
        public NativeArray<int> BuiltShapeOffsets;

        [NativeDisableParallelForRestriction]
        public NativeArray<BuiltChunkMetadata> BuiltChunks;

        [NativeDisableParallelForRestriction]
        public NativeArray<byte> BuiltIsOccupied;

        public void Execute(int shapeListIndex)
        {
            int sourceShapeHandle = ShapeHandles[shapeListIndex];
            int builtShapeCount = math.clamp(
                LocalShapeCounts[shapeListIndex],
                0,
                MaxBuiltShapesPerSourceShape);
            int builtShapeOffset = BuiltShapeOffsets[shapeListIndex];

            InitializeBuiltOutputs(builtShapeOffset, builtShapeCount);

            for (int node = 0; node < MaxBuiltShapesPerSourceShape; node++)
            {
                int localShapeId =
                    FragmentLocalShapeIds[shapeListIndex * MaxBuiltShapesPerSourceShape + node];

                if ((uint)localShapeId >= (uint)builtShapeCount)
                {
                    continue;
                }

                int sourceChunkSlot = node / MaxFragmentsPerChunk;
                int sourceChunkMetadataIndex = sourceShapeHandle * ChunksPerShape + sourceChunkSlot;
                int builtChunkIndex = BuiltChunkIndex(builtShapeOffset, localShapeId, sourceChunkSlot);

                BuiltChunkMetadata builtChunk = BuiltChunks[builtChunkIndex];
                if (!builtChunk.Used)
                {
                    BuiltChunks[builtChunkIndex] = new BuiltChunkMetadata
                    {
                        SourceShapeHandle = sourceShapeHandle,
                        ChunkPosition = ChunkPositions[sourceChunkMetadataIndex],
                        IsUsed = 1
                    };
                }

                OrFragmentIntoBuiltChunk(shapeListIndex, node, builtChunkIndex);
            }
        }

        private void InitializeBuiltOutputs(
            int builtShapeOffset,
            int builtShapeCount)
        {
            int builtChunkBase = builtShapeOffset * MaxBuiltChunksPerShape;
            int builtChunkCount = builtShapeCount * MaxBuiltChunksPerShape;

            for (int chunkSlot = 0; chunkSlot < builtChunkCount; chunkSlot++)
            {
                int builtChunkIndex = builtChunkBase + chunkSlot;
                BuiltChunks[builtChunkIndex] = default;
                ClearBuiltChunk(builtChunkIndex);
            }
        }

        private void ClearBuiltChunk(int builtChunkIndex)
        {
            int builtBase = builtChunkIndex * BitPlaneBytesPerChunk;
            for (int i = 0; i < BitPlaneBytesPerChunk; i++)
            {
                BuiltIsOccupied[builtBase + i] = 0;
            }
        }

        private void OrFragmentIntoBuiltChunk(
            int shapeListIndex,
            int node,
            int builtChunkIndex)
        {
            int fragmentBase = FragmentMaskBase(shapeListIndex, node);
            int builtBase = builtChunkIndex * BitPlaneBytesPerChunk;

            for (int i = 0; i < BitPlaneBytesPerChunk; i++)
            {
                BuiltIsOccupied[builtBase + i] =
                    (byte)(BuiltIsOccupied[builtBase + i] | FragmentIsOccupied[fragmentBase + i]);
            }
        }

        public static int BuiltShapeIndex(int builtShapeOffset, int localShapeId)
        {
            return builtShapeOffset + localShapeId;
        }

        public static int BuiltChunkIndex(
            int builtShapeOffset,
            int localShapeId,
            int chunkSlot)
        {
            return BuiltShapeIndex(builtShapeOffset, localShapeId) *
                   MaxBuiltChunksPerShape +
                   chunkSlot;
        }

        private static int FragmentMaskBase(int shapeListIndex, int node)
        {
            return (shapeListIndex * MaxBuiltShapesPerSourceShape + node) *
                   BitPlaneBytesPerChunk;
        }
    }
}
