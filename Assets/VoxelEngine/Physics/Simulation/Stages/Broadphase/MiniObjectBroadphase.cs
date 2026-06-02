using System.Collections.Generic;

namespace VoxelEngine.Physics.Simulation.Stages.Broadphase
{
    internal sealed class MiniObjectBroadphase
    {
        public void Execute(MiniPhysicsFrameData frameData, List<MiniColliderPairFrame> destination)
        {
            var colliders = frameData.Colliders;
            for (int colliderAIndex = 0; colliderAIndex < colliders.Count; colliderAIndex++)
            {
                MiniColliderFrame colliderA = colliders[colliderAIndex];
                if (!colliderA.HasWorldBounds)
                {
                    continue;
                }

                for (int colliderBIndex = colliderAIndex + 1; colliderBIndex < colliders.Count; colliderBIndex++)
                {
                    MiniColliderFrame colliderB = colliders[colliderBIndex];
                    if (!colliderB.HasWorldBounds ||
                        !ShouldTestPair(frameData, colliderA, colliderB) ||
                        !colliderA.WorldBounds.Intersects(colliderB.WorldBounds))
                    {
                        continue;
                    }

                    destination.Add(new MiniColliderPairFrame(colliderAIndex, colliderBIndex));
                }
            }
        }

        private static bool ShouldTestPair(
            MiniPhysicsFrameData frameData,
            MiniColliderFrame colliderA,
            MiniColliderFrame colliderB)
        {
            if (colliderA.HasBody && colliderA.BodyIndex == colliderB.BodyIndex)
            {
                return false;
            }

            return !IsStatic(frameData, colliderA) || !IsStatic(frameData, colliderB);
        }

        private static bool IsStatic(MiniPhysicsFrameData frameData, MiniColliderFrame collider)
        {
            if (!collider.HasBody)
            {
                return true;
            }

            return frameData.Bodies[collider.BodyIndex].IsStatic;
        }
    }
}
