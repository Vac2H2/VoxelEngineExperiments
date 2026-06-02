using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngineModules.DestructionShape;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct DestructionShapeChunkOverlapJob : IJobParallelFor
    {
        public const int TargetChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int DestructionChunksPerShape = DestructionDataContainer.ChunksPerShape;
        public const int ChunkOverlapMaskBytesPerPair = TargetChunksPerShape;

        [ReadOnly]
        public NativeArray<int> DestructionShapeHandles;

        [ReadOnly]
        public NativeArray<int> TargetShapeHandles;

        [ReadOnly]
        public NativeArray<int> TargetShapeRangeOffsets;

        [ReadOnly]
        public NativeArray<float4x4> DestructionShapeLocalToWorlds;

        [ReadOnly]
        public NativeArray<float4x4> TargetShapeLocalToWorlds;

        [ReadOnly]
        public NativeArray<int3> DestructionChunkPositions;

        [ReadOnly]
        public NativeArray<byte> DestructionChunkUsed;

        [ReadOnly]
        public NativeArray<int3> TargetChunkPositions;

        [ReadOnly]
        public NativeArray<byte> TargetChunkUsed;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<int> PairDestructionShapeHandles;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> ChunkOverlapMasks;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<int> ChunkOverlapCounts;

        public void Execute(int destructionShapeListIndex)
        {
            int destructionShapeHandle = DestructionShapeHandles[destructionShapeListIndex];
            int targetStart = TargetShapeRangeOffsets[destructionShapeListIndex];
            int targetEnd = TargetShapeRangeOffsets[destructionShapeListIndex + 1];
            int destructionChunkBase = destructionShapeHandle * DestructionChunksPerShape;
            float4x4 destructionLocalToWorld = DestructionShapeLocalToWorlds[destructionShapeHandle];

            for (int pairIndex = targetStart; pairIndex < targetEnd; pairIndex++)
            {
                PairDestructionShapeHandles[pairIndex] = destructionShapeHandle;

                int targetShapeHandle = TargetShapeHandles[pairIndex];
                int targetChunkBase = targetShapeHandle * TargetChunksPerShape;
                float4x4 targetLocalToWorld = TargetShapeLocalToWorlds[targetShapeHandle];
                int outputBase = pairIndex * ChunkOverlapMaskBytesPerPair;

                for (int targetChunkSlot = 0; targetChunkSlot < TargetChunksPerShape; targetChunkSlot++)
                {
                    int targetChunkIndex = targetChunkBase + targetChunkSlot;
                    byte overlapMask = 0;

                    if (TargetChunkUsed[targetChunkIndex] != 0)
                    {
                        Obb targetObb = CreateChunkObb(
                            targetLocalToWorld,
                            TargetChunkPositions[targetChunkIndex]);
                        overlapMask = BuildDestructionChunkMask(
                            destructionChunkBase,
                            destructionLocalToWorld,
                            targetObb);
                    }

                    int outputIndex = outputBase + targetChunkSlot;
                    ChunkOverlapMasks[outputIndex] = overlapMask;
                    ChunkOverlapCounts[outputIndex] = CountBits(overlapMask);
                }
            }
        }

        private byte BuildDestructionChunkMask(
            int destructionChunkBase,
            float4x4 destructionLocalToWorld,
            Obb targetObb)
        {
            byte overlapMask = 0;
            for (int destructionChunkSlot = 0; destructionChunkSlot < DestructionChunksPerShape; destructionChunkSlot++)
            {
                int destructionChunkIndex = destructionChunkBase + destructionChunkSlot;
                if (DestructionChunkUsed[destructionChunkIndex] == 0)
                {
                    continue;
                }

                Obb destructionObb = CreateChunkObb(
                    destructionLocalToWorld,
                    DestructionChunkPositions[destructionChunkIndex]);
                if (ObbsOverlap(targetObb, destructionObb))
                {
                    overlapMask = (byte)(overlapMask | (1 << destructionChunkSlot));
                }
            }

            return overlapMask;
        }

        private static int CountBits(byte value)
        {
            int count = 0;
            int bits = value;
            while (bits != 0)
            {
                bits &= bits - 1;
                count++;
            }

            return count;
        }

        private static Obb CreateChunkObb(
            float4x4 localToWorld,
            int3 chunkPosition)
        {
            float3 localMin = new float3(chunkPosition * ShapeDataContainer.ChunkSize);
            float3 localMax = localMin + new float3(ShapeDataContainer.ChunkSize);
            return CreateObb(localToWorld, localMin, localMax);
        }

        private static Obb CreateObb(
            float4x4 localToWorld,
            float3 localMin,
            float3 localMax)
        {
            float3 localCenter = (localMin + localMax) * 0.5f;
            float3 localExtents = (localMax - localMin) * 0.5f;
            float3 axisX = new float3(localToWorld.c0.x, localToWorld.c0.y, localToWorld.c0.z);
            float3 axisY = new float3(localToWorld.c1.x, localToWorld.c1.y, localToWorld.c1.z);
            float3 axisZ = new float3(localToWorld.c2.x, localToWorld.c2.y, localToWorld.c2.z);
            float lengthX = math.length(axisX);
            float lengthY = math.length(axisY);
            float lengthZ = math.length(axisZ);

            return new Obb
            {
                Center = math.transform(localToWorld, localCenter),
                AxisX = NormalizeOrFallback(axisX, new float3(1.0f, 0.0f, 0.0f)),
                AxisY = NormalizeOrFallback(axisY, new float3(0.0f, 1.0f, 0.0f)),
                AxisZ = NormalizeOrFallback(axisZ, new float3(0.0f, 0.0f, 1.0f)),
                Extents = new float3(
                    localExtents.x * lengthX,
                    localExtents.y * lengthY,
                    localExtents.z * lengthZ)
            };
        }

        private static bool ObbsOverlap(Obb a, Obb b)
        {
            if (IsSeparatingAxis(a.AxisX, a, b) ||
                IsSeparatingAxis(a.AxisY, a, b) ||
                IsSeparatingAxis(a.AxisZ, a, b) ||
                IsSeparatingAxis(b.AxisX, a, b) ||
                IsSeparatingAxis(b.AxisY, a, b) ||
                IsSeparatingAxis(b.AxisZ, a, b))
            {
                return false;
            }

            if (IsSeparatingAxis(math.cross(a.AxisX, b.AxisX), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisX, b.AxisY), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisX, b.AxisZ), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisY, b.AxisX), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisY, b.AxisY), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisY, b.AxisZ), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisZ, b.AxisX), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisZ, b.AxisY), a, b) ||
                IsSeparatingAxis(math.cross(a.AxisZ, b.AxisZ), a, b))
            {
                return false;
            }

            return true;
        }

        private static bool IsSeparatingAxis(float3 axis, Obb a, Obb b)
        {
            if (math.lengthsq(axis) <= 0.000000000001f)
            {
                return false;
            }

            float distance = math.abs(math.dot(b.Center - a.Center, axis));
            float radiusA =
                a.Extents.x * math.abs(math.dot(a.AxisX, axis)) +
                a.Extents.y * math.abs(math.dot(a.AxisY, axis)) +
                a.Extents.z * math.abs(math.dot(a.AxisZ, axis));
            float radiusB =
                b.Extents.x * math.abs(math.dot(b.AxisX, axis)) +
                b.Extents.y * math.abs(math.dot(b.AxisY, axis)) +
                b.Extents.z * math.abs(math.dot(b.AxisZ, axis));

            return distance > radiusA + radiusB + 0.00001f;
        }

        private static float3 NormalizeOrFallback(float3 value, float3 fallback)
        {
            float length = math.length(value);
            return length > 0.000001f ? value / length : fallback;
        }

        private struct Obb
        {
            public float3 Center;
            public float3 AxisX;
            public float3 AxisY;
            public float3 AxisZ;
            public float3 Extents;
        }
    }
}
