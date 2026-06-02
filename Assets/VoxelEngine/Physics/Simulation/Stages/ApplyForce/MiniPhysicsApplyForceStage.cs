using System;
using UnityEngine;

namespace VoxelEngine.Physics.Simulation.Stages.ApplyForce
{
    public sealed class MiniPhysicsApplyForceStage
    {
        public void Execute(MiniPhysicsFrameData frameData, Vector3 gravity, float deltaTime)
        {
            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            var bodies = frameData.Bodies;
            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                bodies[bodyIndex].Body.IntegrateForces(gravity, deltaTime);
            }
        }
    }
}
