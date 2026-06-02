using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using VoxelEngineModules.Shape.Debugger;

namespace VoxelEngineModules.ShapeDestructionPipeline.PerformanceTests
{
    public sealed class ShapeDestructionPipelinePerformanceTests
    {
        private const float DestructionRadiusInVoxels = 4.0f;
        private const int MinimalVoxelNumber = 2;
        private const int WarmupCount = 6;
        private const int MeasurementCount = 30;

        private static long _lastChecksum;

        [Test, Performance]
        [Category("Performance")]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        [TestCase(64)]
        [TestCase(128)]
        [TestCase(256)]
        [TestCase(512)]
        [TestCase(1000)]
        public void SyntheticFullShapes(
            int shapeCount)
        {
            Assert.That(
                ShapeDestructionPipelineBenchmark.RunIteration(
                    shapeCount,
                    DestructionRadiusInVoxels,
                    MinimalVoxelNumber,
                    out ShapeDestructionPipelineBenchmark.BenchmarkSample sample),
                Is.True);
            Assert.That(sample.AffectedShapeCount, Is.EqualTo(shapeCount));
            Assert.That(sample.CommandCount, Is.GreaterThan(0));
            Assert.That(sample.BuiltShapeCount, Is.GreaterThan(0));

            RecordMetadata(shapeCount, sample);

            Measure.Method(() =>
                {
                    if (!ShapeDestructionPipelineBenchmark.RunIteration(
                            shapeCount,
                            DestructionRadiusInVoxels,
                            MinimalVoxelNumber,
                            out ShapeDestructionPipelineBenchmark.BenchmarkSample measured))
                    {
                        throw new InvalidOperationException(
                            "Shape destruction performance iteration produced no destructive work.");
                    }

                    Consume(measured);
                })
                .SampleGroup($"ShapeDestructionPipeline.{shapeCount}.Total")
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        private static void RecordMetadata(
            int shapeCount,
            ShapeDestructionPipelineBenchmark.BenchmarkSample sample)
        {
            ShapeDestructionPipelineBenchmark.BenchmarkStageTimings timings = sample.Timings;
            Measure.Custom(new SampleGroup("ShapeCount", SampleUnit.Undefined, true), shapeCount);
            Measure.Custom(new SampleGroup("AffectedShapeCount", SampleUnit.Undefined, true), sample.AffectedShapeCount);
            Measure.Custom(new SampleGroup("BuiltShapeCount", SampleUnit.Undefined, true), sample.BuiltShapeCount);
            Measure.Custom(new SampleGroup("CommandCount", SampleUnit.Undefined, true), sample.CommandCount);
            Measure.Custom(new SampleGroup("MeasuredStagesSubtotalMs", SampleUnit.Millisecond, false), timings.TotalMeasuredMilliseconds);
            Measure.Custom(new SampleGroup("OuterOverheadMs", SampleUnit.Millisecond, false), Math.Max(0.0, sample.TotalMilliseconds - timings.TotalMeasuredMilliseconds));
            Measure.Custom(new SampleGroup("DestructionShapeChunkOverlapJobMs", SampleUnit.Millisecond, false), timings.DestructionShapeChunkOverlapJob);
            Measure.Custom(new SampleGroup("DestructionChunkOverlapPrefixSumJobMs", SampleUnit.Millisecond, false), timings.DestructionChunkOverlapPrefixSumJob);
            Measure.Custom(new SampleGroup("DestructionMaskGenerationJobMs", SampleUnit.Millisecond, false), timings.DestructionMaskGenerationJob);
            Measure.Custom(new SampleGroup("ShapeVoxelRemoveBuildKeyJobMs", SampleUnit.Millisecond, false), timings.ShapeVoxelRemoveBuildKeyJob);
            Measure.Custom(new SampleGroup("CommandKeySortMs", SampleUnit.Millisecond, false), timings.CommandKeySort);
            Measure.Custom(new SampleGroup("ShapeVoxelRemoveBuildRangeJobMs", SampleUnit.Millisecond, false), timings.ShapeVoxelRemoveBuildRangeJob);
            Measure.Custom(new SampleGroup("ShapeVoxelRemovePackJobMs", SampleUnit.Millisecond, false), timings.ShapeVoxelRemovePackJob);
            Measure.Custom(new SampleGroup("ShapeVoxelRemoveJobMs", SampleUnit.Millisecond, false), timings.ShapeVoxelRemoveJob);
            Measure.Custom(new SampleGroup("ShapeChunkFragmentJobMs", SampleUnit.Millisecond, false), timings.ShapeChunkFragmentJob);
            Measure.Custom(new SampleGroup("ShapeChunkCheckMaskJobMs", SampleUnit.Millisecond, false), timings.ShapeChunkCheckMaskJob);
            Measure.Custom(new SampleGroup("ShapeChunkConnectivityJobMs", SampleUnit.Millisecond, false), timings.ShapeChunkConnectivityJob);
            Measure.Custom(new SampleGroup("ShapeFragmentUnionJobMs", SampleUnit.Millisecond, false), timings.ShapeFragmentUnionJob);
            Measure.Custom(new SampleGroup("ShapePrefixSumComputeJobMs", SampleUnit.Millisecond, false), timings.ShapePrefixSumComputeJob);
            Measure.Custom(new SampleGroup("ShapeBuildJobMs", SampleUnit.Millisecond, false), timings.ShapeBuildJob);
        }

        private static void Consume(
            ShapeDestructionPipelineBenchmark.BenchmarkSample sample)
        {
            _lastChecksum =
                sample.ShapeCount ^
                (sample.AffectedShapeCount << 8) ^
                (sample.BuiltShapeCount << 16) ^
                (sample.CommandCount << 24) ^
                (long)sample.Timings.TotalMeasuredMilliseconds;
        }
    }
}
