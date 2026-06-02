using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngineDOTS.BVH
{
    [DisallowMultipleComponent]
    public class TlasBvhDemoSettings : MonoBehaviour
    {
        [Header("Scene")]
        public int BodyCount = 10000;
        public float WorldSize = 100.0f;
        public float MinBodySize = 0.5f;
        public float MaxBodySize = 3.0f;
        public float MaxSpeed = 0.5f;
        public int RandomSeed = 12345;

        [Header("Distribution")]
        public BodyDistribution Distribution = BodyDistribution.Uniform;
        public int ClusterCount = 8;
        public float ClusterRadius = 8.0f;
        public float LargeBodyRatio = 0.0f;

        [Header("BVH Build")]
        public int LeafSize = 4;
        public int MortonBitsPerAxis = 10;
        public BvhBuildMode BuildMode = BvhBuildMode.MortonPairingBuild;

        [Header("Query")]
        public BvhQueryMode QueryMode = BvhQueryMode.ParallelNodePairSelfQuery;
        public int SeedMultiplier = 16;
        public int LocalStackCapacity = 512;
        public int MaxOutputPairs = 1000000;

        [Header("Jobs")]
        public bool UseJobs = true;
        public bool UseBurst = true;
        public int InnerLoopBatchCount = 64;

        [Header("Validation")]
        public bool ValidateAgainstNaive = false;
        public int MaxNaiveValidationBodies = 2000;
        public bool SortPairsBeforeValidation = true;

        [Header("Rendering")]
        public bool RenderBodies = true;
        public int MaxRenderedBodies = 5000;
        public bool MarkCollidingBodies = true;

        [Header("Benchmark")]
        public int WarmupFrames = 30;
        public int SampleFrames = 300;
        public bool AutoRunBenchmark = false;

        public void Sanitize()
        {
            BodyCount = math.max(0, BodyCount);
            WorldSize = math.max(1.0f, WorldSize);
            MinBodySize = math.max(0.01f, MinBodySize);
            MaxBodySize = math.max(MinBodySize, MaxBodySize);
            MaxSpeed = math.max(0.0f, MaxSpeed);
            ClusterCount = math.max(1, ClusterCount);
            ClusterRadius = math.max(0.01f, ClusterRadius);
            LargeBodyRatio = math.saturate(LargeBodyRatio);
            LeafSize = math.max(1, LeafSize);
            MortonBitsPerAxis = math.clamp(MortonBitsPerAxis, 1, 10);
            SeedMultiplier = math.max(1, SeedMultiplier);
            LocalStackCapacity = math.max(16, LocalStackCapacity);
            MaxOutputPairs = math.max(1, MaxOutputPairs);
            InnerLoopBatchCount = math.max(1, InnerLoopBatchCount);
            MaxNaiveValidationBodies = math.max(0, MaxNaiveValidationBodies);
            MaxRenderedBodies = math.max(0, MaxRenderedBodies);
            WarmupFrames = math.max(0, WarmupFrames);
            SampleFrames = math.max(1, SampleFrames);
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
