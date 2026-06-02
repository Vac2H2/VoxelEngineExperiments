using System;
using UnityEngine;
using VoxelEngine.Physics.Body;
using VoxelEngine.Physics.Collider;
using VoxelEngine.Physics.World;

namespace VoxelEngine.Physics.Simulation.Stages.Collect
{
    public sealed class MiniPhysicsCollectStage
    {
        public void Execute(MiniPhysicsWorld world, MiniPhysicsFrameData frameData)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            frameData.Clear();
            CollectBodies(world, frameData);
            CollectColliders(world, frameData);
        }

        private static void CollectBodies(MiniPhysicsWorld world, MiniPhysicsFrameData frameData)
        {
            var bodies = world.RigidBodies;
            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                MiniRigidBody body = bodies[bodyIndex];
                if (body == null || !body.isActiveAndEnabled)
                {
                    continue;
                }

                frameData.AddBody(new MiniBodyFrame(
                    body,
                    body.Handle,
                    body.BodyType,
                    body.transform.position,
                    body.transform.rotation,
                    body.WorldCenterOfMass,
                    body.LinearVelocity,
                    body.AngularVelocity,
                    body.InverseMass,
                    body.WorldInverseInertiaTensor));
            }
        }

        private static void CollectColliders(MiniPhysicsWorld world, MiniPhysicsFrameData frameData)
        {
            var colliders = world.Colliders;
            for (int worldColliderIndex = 0; worldColliderIndex < colliders.Count; worldColliderIndex++)
            {
                MiniCollider collider = colliders[worldColliderIndex];
                if (collider == null || !collider.isActiveAndEnabled)
                {
                    continue;
                }

                collider.RefreshAttachedBody();

                int bodyIndex = MiniColliderFrame.NoBodyIndex;
                MiniRigidBody attachedBody = collider.AttachedBody;
                if (attachedBody != null && attachedBody.isActiveAndEnabled)
                {
                    frameData.TryGetBodyIndex(attachedBody, out bodyIndex);
                }

                bool hasWorldBounds = CollectColliderBounds(
                    collider,
                    out Bounds worldBounds,
                    out MiniObbFrame worldObb);

                frameData.AddCollider(new MiniColliderFrame(
                    collider,
                    collider.Handle,
                    bodyIndex,
                    worldBounds,
                    worldObb,
                    hasWorldBounds));
            }
        }

        private static bool CollectColliderBounds(
            MiniCollider collider,
            out Bounds worldBounds,
            out MiniObbFrame worldObb)
        {
            MiniColliderData data = collider.Data;
            if (!data.TryGetLocalBounds(out Bounds localBounds))
            {
                worldBounds = default;
                worldObb = default;
                return false;
            }

            worldObb = MiniObbFrame.FromLocalBounds(localBounds, BuildScaleFreeLocalToWorld(collider.transform));
            worldBounds = worldObb.ToBounds();
            return true;
        }

        private static Matrix4x4 BuildScaleFreeLocalToWorld(Transform transform)
        {
            return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        }
    }
}
