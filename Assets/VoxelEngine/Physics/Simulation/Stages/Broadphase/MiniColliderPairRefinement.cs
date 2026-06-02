using System.Collections.Generic;

namespace VoxelEngine.Physics.Simulation.Stages.Broadphase
{
    internal sealed class MiniColliderPairRefinement
    {
        public void Execute(
            MiniPhysicsFrameData frameData,
            List<MiniColliderPairFrame> source,
            List<MiniColliderPairFrame> destination)
        {
            var colliders = frameData.Colliders;
            for (int pairIndex = 0; pairIndex < source.Count; pairIndex++)
            {
                MiniColliderPairFrame pair = source[pairIndex];
                MiniColliderFrame colliderA = colliders[pair.ColliderAIndex];
                MiniColliderFrame colliderB = colliders[pair.ColliderBIndex];

                if (MiniObbSat.Overlaps(colliderA.WorldObb, colliderB.WorldObb))
                {
                    destination.Add(pair);
                }
            }
        }
    }
}
