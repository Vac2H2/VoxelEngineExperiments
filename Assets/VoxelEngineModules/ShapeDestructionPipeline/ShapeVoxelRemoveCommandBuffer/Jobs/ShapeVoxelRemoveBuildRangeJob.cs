using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace VoxelEngineModules.Shape
{
    public struct ShapeVoxelRemoveCommandRange
    {
        public int ShapeHandle;
        public int StartKeyIndex;
        public int KeyCount;
    }

    [BurstCompile]
    public struct ShapeVoxelRemoveBuildRangeJob : IJob
    {
        [ReadOnly]
        public NativeArray<ShapeVoxelRemoveCommandKey> SortedKeys;

        [WriteOnly]
        public NativeArray<int> AffectedShapeHandles;

        [WriteOnly]
        public NativeArray<ShapeVoxelRemoveCommandRange> ShapeRanges;

        [WriteOnly]
        public NativeArray<int> UniqueShapeCount;

        public int KeyCount;

        public void Execute()
        {
            int rangeCount = 0;
            int startKeyIndex = 0;

            while (startKeyIndex < KeyCount)
            {
                int shapeHandle = SortedKeys[startKeyIndex].ShapeHandle;
                int endKeyIndex = startKeyIndex + 1;

                while (endKeyIndex < KeyCount &&
                       SortedKeys[endKeyIndex].ShapeHandle == shapeHandle)
                {
                    endKeyIndex++;
                }

                AffectedShapeHandles[rangeCount] = shapeHandle;
                ShapeRanges[rangeCount] = new ShapeVoxelRemoveCommandRange
                {
                    ShapeHandle = shapeHandle,
                    StartKeyIndex = startKeyIndex,
                    KeyCount = endKeyIndex - startKeyIndex
                };

                rangeCount++;
                startKeyIndex = endKeyIndex;
            }

            UniqueShapeCount[0] = rangeCount;
        }
    }
}
