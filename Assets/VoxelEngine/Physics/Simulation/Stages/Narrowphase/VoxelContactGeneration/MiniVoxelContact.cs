using UnityEngine;

namespace VoxelEngine.Physics.Simulation.Stages.Narrowphase.VoxelContactGeneration
{
    internal readonly struct MiniVoxelContact
    {
        public MiniVoxelContact(
            Vector3 position,
            Vector3 normal,
            float penetration,
            MiniVoxelContactFeature featureA,
            MiniVoxelContactFeature featureB)
        {
            Position = position;
            Normal = normal;
            Penetration = penetration;
            FeatureA = featureA;
            FeatureB = featureB;
        }

        public Vector3 Position { get; }

        public Vector3 Normal { get; }

        public float Penetration { get; }

        public MiniVoxelContactFeature FeatureA { get; }

        public MiniVoxelContactFeature FeatureB { get; }
    }
}
