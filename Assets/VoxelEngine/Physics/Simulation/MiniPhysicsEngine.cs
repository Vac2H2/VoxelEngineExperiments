using System;
using UnityEngine;
using VoxelEngine.Physics.Simulation.Stages.ApplyForce;
using VoxelEngine.Physics.Simulation.Stages.Broadphase;
using VoxelEngine.Physics.Simulation.Stages.Collect;
using VoxelEngine.Physics.Simulation.Stages.Integrate;
using VoxelEngine.Physics.Simulation.Stages.Narrowphase;
using VoxelEngine.Physics.Simulation.Stages.Solver;
using VoxelEngine.Physics.World;

namespace VoxelEngine.Physics.Simulation
{
    public sealed class MiniPhysicsEngine
    {
        private readonly MiniPhysicsFrameData _frameData = new MiniPhysicsFrameData();
        private readonly MiniPhysicsCollectStage _collectStage = new MiniPhysicsCollectStage();
        private readonly MiniPhysicsApplyForceStage _applyForceStage = new MiniPhysicsApplyForceStage();
        private readonly MiniPhysicsBroadphaseStage _broadphaseStage = new MiniPhysicsBroadphaseStage();
        private readonly MiniPhysicsNarrowphaseStage _narrowphaseStage = new MiniPhysicsNarrowphaseStage();
        private readonly MiniPhysicsSolverStage _solverStage = new MiniPhysicsSolverStage();
        private readonly MiniPhysicsIntegrateStage _integrateStage = new MiniPhysicsIntegrateStage();

        public MiniPhysicsFrameData FrameData => _frameData;

        public Vector3 Gravity { get; set; } = new Vector3(0.0f, -9.81f, 0.0f);

        public MiniPhysicsSolverStage Solver => _solverStage;

        public void Tick(MiniPhysicsWorld world, float deltaTime)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (deltaTime < 0.0f || !float.IsFinite(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be finite and non-negative.");
            }

            Collect(world);
            ApplyForces(deltaTime);
            Broadphase();
            Narrowphase();
            Solve(deltaTime);
            Integrate(deltaTime);
        }

        private void Collect(MiniPhysicsWorld world)
        {
            _collectStage.Execute(world, _frameData);
        }

        private void ApplyForces(float deltaTime)
        {
            _applyForceStage.Execute(_frameData, Gravity, deltaTime);
        }

        private void Broadphase()
        {
            _broadphaseStage.Execute(_frameData);
        }

        private void Narrowphase()
        {
            _narrowphaseStage.Execute(_frameData);
        }

        private void Solve(float deltaTime)
        {
            _solverStage.Execute(_frameData, deltaTime);
        }

        private void Integrate(float deltaTime)
        {
            _integrateStage.Execute(_frameData, deltaTime);
        }
    }
}
