using System;

namespace VoxelEngine.Physics.Simulation.Stages.Broadphase
{
    internal readonly struct MiniColliderPairFrame
    {
        public MiniColliderPairFrame(int colliderAIndex, int colliderBIndex)
        {
            if (colliderAIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(colliderAIndex), "Collider A index must be non-negative.");
            }

            if (colliderBIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(colliderBIndex), "Collider B index must be non-negative.");
            }

            ColliderAIndex = colliderAIndex;
            ColliderBIndex = colliderBIndex;
        }

        public int ColliderAIndex { get; }

        public int ColliderBIndex { get; }
    }
}
