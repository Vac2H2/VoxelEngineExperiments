using System;
using System.Collections.Generic;

namespace VoxelEngine.Physics.Simulation.Stages.Broadphase
{
    public sealed class MiniPhysicsBroadphaseStage
    {
        private readonly MiniObjectBroadphase _objectBroadphase = new MiniObjectBroadphase();
        private readonly MiniColliderPairRefinement _colliderPairRefinement = new MiniColliderPairRefinement();
        private readonly List<MiniColliderPairFrame> _objectPairs = new List<MiniColliderPairFrame>();
        private readonly List<MiniColliderPairFrame> _refinedPairs = new List<MiniColliderPairFrame>();

        public void Execute(MiniPhysicsFrameData frameData)
        {
            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            frameData.ClearBroadphase();
            _objectPairs.Clear();
            _refinedPairs.Clear();

            _objectBroadphase.Execute(frameData, _objectPairs);
            _colliderPairRefinement.Execute(frameData, _objectPairs, _refinedPairs);
            for (int pairIndex = 0; pairIndex < _refinedPairs.Count; pairIndex++)
            {
                frameData.AddColliderPair(_refinedPairs[pairIndex]);
            }
        }
    }
}
