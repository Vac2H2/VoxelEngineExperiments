using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineDOTS.BVH
{
    [BurstCompile]
    public struct BodySimulationJob : IJobParallelFor
    {
        public NativeArray<BodyState> Bodies;
        public float DeltaTime;
        public float WorldSize;

        public void Execute(int index)
        {
            BodySimulationUtility.SimulateBody(Bodies, index, DeltaTime, WorldSize);
        }
    }

    public static class BodySimulationUtility
    {
        public static void SimulateBody(NativeArray<BodyState> bodies, int index, float deltaTime, float worldSize)
        {
            BodyState body = bodies[index];
            body.Position += body.Velocity * deltaTime;

            float3 limit = new float3(worldSize * 0.5f) - body.HalfSize;
            limit = math.max(limit, 0.0f);

            if (math.abs(body.Position.x) > limit.x)
            {
                body.Position.x = math.clamp(body.Position.x, -limit.x, limit.x);
                body.Velocity.x *= -1.0f;
            }

            if (math.abs(body.Position.y) > limit.y)
            {
                body.Position.y = math.clamp(body.Position.y, -limit.y, limit.y);
                body.Velocity.y *= -1.0f;
            }

            if (math.abs(body.Position.z) > limit.z)
            {
                body.Position.z = math.clamp(body.Position.z, -limit.z, limit.z);
                body.Velocity.z *= -1.0f;
            }

            bodies[index] = body;
        }
    }
}
