using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineDOTS.BVH
{
    public static class BodyGenerator
    {
        public static void Generate(NativeArray<BodyState> bodies, TlasBvhDemoSettings settings)
        {
            uint seed = (uint)math.max(1, settings.RandomSeed);
            Random random = new Random(seed);
            float worldSize = math.max(1.0f, settings.WorldSize);
            float halfWorld = worldSize * 0.5f;
            int clusterCount = math.max(1, settings.ClusterCount);
            float3[] clusterCenters = CreateClusterCenters(ref random, clusterCount, halfWorld);

            for (int i = 0; i < bodies.Length; i++)
            {
                float3 halfSize = NextHalfSize(ref random, settings, i);
                float3 position = NextPosition(ref random, settings, clusterCenters, halfWorld, halfSize);
                float3 velocity = NextVelocity(ref random, settings.MaxSpeed);

                bodies[i] = new BodyState
                {
                    BodyId = i,
                    Position = position,
                    HalfSize = halfSize,
                    Velocity = velocity
                };
            }
        }

        private static float3[] CreateClusterCenters(ref Random random, int clusterCount, float halfWorld)
        {
            float3[] centers = new float3[clusterCount];
            for (int i = 0; i < centers.Length; i++)
            {
                centers[i] = random.NextFloat3(-halfWorld * 0.65f, halfWorld * 0.65f);
            }

            return centers;
        }

        private static float3 NextHalfSize(ref Random random, TlasBvhDemoSettings settings, int index)
        {
            float minSize = math.max(0.01f, math.min(settings.MinBodySize, settings.MaxBodySize));
            float maxSize = math.max(minSize, math.max(settings.MinBodySize, settings.MaxBodySize));
            float size = random.NextFloat(minSize, maxSize);

            if (settings.Distribution == BodyDistribution.LargeBodiesStress)
            {
                float largeRatio = math.saturate(settings.LargeBodyRatio);
                if (largeRatio <= 0.0f)
                {
                    largeRatio = 0.1f;
                }

                if (random.NextFloat() < largeRatio)
                {
                    size = random.NextFloat(maxSize * 2.0f, math.max(maxSize * 2.0f, settings.WorldSize * 0.16f));
                }
            }

            float anisotropy = random.NextFloat(0.65f, 1.45f);
            float3 axisScale = new float3(
                random.NextFloat(0.75f, 1.25f),
                random.NextFloat(0.75f, 1.25f),
                random.NextFloat(0.75f, 1.25f));

            if (settings.Distribution == BodyDistribution.Plane)
            {
                axisScale.y *= 0.45f;
            }
            else if (settings.Distribution == BodyDistribution.Line)
            {
                axisScale.y *= 0.55f;
                axisScale.z *= 0.55f;
            }

            return math.max(new float3(size * 0.5f * anisotropy) * axisScale, new float3(0.01f));
        }

        private static float3 NextPosition(
            ref Random random,
            TlasBvhDemoSettings settings,
            float3[] clusterCenters,
            float halfWorld,
            float3 halfSize)
        {
            float3 limit = math.max(new float3(halfWorld) - halfSize, new float3(0.0f));
            float3 position;

            switch (settings.Distribution)
            {
                case BodyDistribution.Clustered:
                {
                    int clusterIndex = random.NextInt(0, clusterCenters.Length);
                    float radius = math.max(0.01f, settings.ClusterRadius);
                    position = clusterCenters[clusterIndex] + RandomInsideUnitSphere(ref random) * radius;
                    break;
                }
                case BodyDistribution.DenseCenter:
                    position = RandomInsideUnitSphere(ref random) * math.max(1.0f, settings.WorldSize * 0.16f);
                    break;
                case BodyDistribution.Line:
                    position = new float3(
                        random.NextFloat(-limit.x, limit.x),
                        random.NextFloat(-math.min(limit.y, settings.ClusterRadius), math.min(limit.y, settings.ClusterRadius)),
                        random.NextFloat(-math.min(limit.z, settings.ClusterRadius), math.min(limit.z, settings.ClusterRadius)));
                    break;
                case BodyDistribution.Plane:
                    position = new float3(
                        random.NextFloat(-limit.x, limit.x),
                        random.NextFloat(-math.min(limit.y, settings.ClusterRadius * 0.25f), math.min(limit.y, settings.ClusterRadius * 0.25f)),
                        random.NextFloat(-limit.z, limit.z));
                    break;
                default:
                    position = random.NextFloat3(-limit, limit);
                    break;
            }

            return math.clamp(position, -limit, limit);
        }

        private static float3 NextVelocity(ref Random random, float maxSpeed)
        {
            float speed = random.NextFloat(0.0f, math.max(0.0f, maxSpeed));
            if (speed <= 0.0f)
            {
                return float3.zero;
            }

            float3 direction = RandomInsideUnitSphere(ref random);
            float lengthSq = math.lengthsq(direction);
            if (lengthSq < 0.0001f)
            {
                direction = new float3(1.0f, 0.0f, 0.0f);
            }
            else
            {
                direction *= math.rsqrt(lengthSq);
            }

            return direction * speed;
        }

        private static float3 RandomInsideUnitSphere(ref Random random)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float3 p = random.NextFloat3(-1.0f, 1.0f);
                if (math.lengthsq(p) <= 1.0f)
                {
                    return p;
                }
            }

            return math.normalize(random.NextFloat3Direction());
        }
    }
}
