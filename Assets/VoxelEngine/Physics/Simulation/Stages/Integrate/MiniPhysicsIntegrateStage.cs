using System;

namespace VoxelEngine.Physics.Simulation.Stages.Integrate
{
    public sealed class MiniPhysicsIntegrateStage
    {
        public void Execute(MiniPhysicsFrameData frameData, float deltaTime)
        {
            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            var bodies = frameData.Bodies;
            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                bodies[bodyIndex].Body.IntegrateTransform(deltaTime);
            }
        }
    }
}
