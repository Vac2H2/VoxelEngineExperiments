using System;
using System.Collections.Generic;
using VoxelEngine.Physics.Simulation.Stages.Narrowphase.VoxelContactGeneration;

namespace VoxelEngine.Physics.Simulation.Stages.Narrowphase
{
    public sealed class MiniPhysicsNarrowphaseStage
    {
        private readonly MiniVoxelContactGenerator _voxelContactGenerator = new MiniVoxelContactGenerator();
        private readonly List<MiniVoxelContact> _voxelContacts = new List<MiniVoxelContact>();

        public void Execute(MiniPhysicsFrameData frameData)
        {
            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            frameData.ClearNarrowphase();

            var colliderPairs = frameData.ColliderPairs;
            var colliders = frameData.Colliders;
            for (int pairIndex = 0; pairIndex < colliderPairs.Count; pairIndex++)
            {
                var colliderPair = colliderPairs[pairIndex];
                MiniColliderFrame colliderA = colliders[colliderPair.ColliderAIndex];
                MiniColliderFrame colliderB = colliders[colliderPair.ColliderBIndex];

                _voxelContacts.Clear();
                _voxelContactGenerator.Generate(colliderA, colliderB, _voxelContacts);
                for (int contactIndex = 0; contactIndex < _voxelContacts.Count; contactIndex++)
                {
                    MiniVoxelContact contact = _voxelContacts[contactIndex];
                    frameData.AddContactManifold(new MiniContactManifoldFrame(
                        colliderPair.ColliderAIndex,
                        colliderPair.ColliderBIndex,
                        colliderA.BodyIndex,
                        colliderB.BodyIndex,
                        contact.Normal,
                        contact.Penetration,
                        new MiniContactPointFrame(contact.Position, contact.Penetration),
                        default,
                        default,
                        default,
                        1));
                }
            }
        }
    }
}
