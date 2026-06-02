using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapePrefixSumComputeJob : IJob
    {
        public const int MaxBuiltShapesPerSourceShape = ShapeFragmentUnionJob.MaxFragmentNodesPerShape;

        [ReadOnly]
        public NativeArray<int> LocalShapeCounts;

        [WriteOnly]
        public NativeArray<int> BuiltShapeOffsets;

        [WriteOnly]
        public NativeArray<int> TotalBuiltShapeCount;

        public void Execute()
        {
            int sum = 0;
            for (int shapeListIndex = 0; shapeListIndex < LocalShapeCounts.Length; shapeListIndex++)
            {
                BuiltShapeOffsets[shapeListIndex] = sum;
                sum += math.clamp(
                    LocalShapeCounts[shapeListIndex],
                    0,
                    MaxBuiltShapesPerSourceShape);
            }

            TotalBuiltShapeCount[0] = sum;
        }
    }
}
