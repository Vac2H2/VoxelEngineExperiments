using UnityEngine;

namespace VoxelEngine.Physics.Simulation.Stages.Narrowphase.ContactPointGeneration
{
    internal struct MiniContactCandidate
    {
        public MiniContactCandidate(Vector3 position, float penetration)
        {
            Position = position;
            Penetration = penetration;
        }

        public Vector3 Position;

        public float Penetration;
    }
}
