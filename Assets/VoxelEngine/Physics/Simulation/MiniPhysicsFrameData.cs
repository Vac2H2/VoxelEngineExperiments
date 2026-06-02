using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Physics.Body;
using VoxelEngine.Physics.Collider;
using VoxelEngine.Physics.Simulation.Stages.Broadphase;
using VoxelEngine.Physics.World;

namespace VoxelEngine.Physics.Simulation
{
    public sealed class MiniPhysicsFrameData
    {
        private readonly List<MiniBodyFrame> _bodies = new List<MiniBodyFrame>();
        private readonly List<MiniColliderFrame> _colliders = new List<MiniColliderFrame>();
        private readonly List<MiniColliderPairFrame> _colliderPairs = new List<MiniColliderPairFrame>();
        private readonly List<MiniContactManifoldFrame> _contactManifolds = new List<MiniContactManifoldFrame>();
        private readonly Dictionary<MiniRigidBody, int> _bodyIndexByBody = new Dictionary<MiniRigidBody, int>();

        public IReadOnlyList<MiniBodyFrame> Bodies => _bodies;

        public IReadOnlyList<MiniColliderFrame> Colliders => _colliders;

        internal IReadOnlyList<MiniColliderPairFrame> ColliderPairs => _colliderPairs;

        public IReadOnlyList<MiniContactManifoldFrame> ContactManifolds => _contactManifolds;

        public int BodyCount => _bodies.Count;

        public int ColliderCount => _colliders.Count;

        public int ColliderPairCount => _colliderPairs.Count;

        public int ContactManifoldCount => _contactManifolds.Count;

        internal void Clear()
        {
            _bodies.Clear();
            _colliders.Clear();
            _colliderPairs.Clear();
            _contactManifolds.Clear();
            _bodyIndexByBody.Clear();
        }

        internal void ClearBroadphase()
        {
            _colliderPairs.Clear();
            ClearNarrowphase();
        }

        internal void ClearNarrowphase()
        {
            _contactManifolds.Clear();
        }

        internal int AddBody(MiniBodyFrame bodyFrame)
        {
            int bodyIndex = _bodies.Count;
            _bodies.Add(bodyFrame);

            if (bodyFrame.Body != null)
            {
                _bodyIndexByBody[bodyFrame.Body] = bodyIndex;
            }

            return bodyIndex;
        }

        internal int AddCollider(MiniColliderFrame colliderFrame)
        {
            int colliderIndex = _colliders.Count;
            _colliders.Add(colliderFrame);
            return colliderIndex;
        }

        internal void AddColliderPair(MiniColliderPairFrame colliderPair)
        {
            _colliderPairs.Add(colliderPair);
        }

        internal void AddContactManifold(MiniContactManifoldFrame contactManifold)
        {
            _contactManifolds.Add(contactManifold);
        }

        internal bool TryGetBodyIndex(MiniRigidBody body, out int bodyIndex)
        {
            if (body == null)
            {
                bodyIndex = -1;
                return false;
            }

            if (_bodyIndexByBody.TryGetValue(body, out bodyIndex))
            {
                return true;
            }

            bodyIndex = -1;
            return false;
        }
    }

    public readonly struct MiniContactPointFrame
    {
        public MiniContactPointFrame(Vector3 position, float penetration)
        {
            Position = SanitizeVector(position);
            Penetration = Mathf.Max(0.0f, SanitizeFinite(penetration));
        }

        public Vector3 Position { get; }

        public float Penetration { get; }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                SanitizeFinite(value.x),
                SanitizeFinite(value.y),
                SanitizeFinite(value.z));
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsFinite(value) ? value : 0.0f;
        }
    }

    public readonly struct MiniContactManifoldFrame
    {
        public const int MaxPointCount = 4;

        public MiniContactManifoldFrame(
            int colliderAIndex,
            int colliderBIndex,
            int bodyAIndex,
            int bodyBIndex,
            Vector3 normal,
            float penetration,
            MiniContactPointFrame point0,
            MiniContactPointFrame point1,
            MiniContactPointFrame point2,
            MiniContactPointFrame point3,
            int pointCount)
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
            BodyAIndex = bodyAIndex;
            BodyBIndex = bodyBIndex;
            Normal = NormalizeOrFallback(normal, Vector3.up);
            Penetration = Mathf.Max(0.0f, SanitizeFinite(penetration));
            Point0 = point0;
            Point1 = point1;
            Point2 = point2;
            Point3 = point3;
            PointCount = Mathf.Clamp(pointCount, 0, MaxPointCount);
        }

        public int ColliderAIndex { get; }

        public int ColliderBIndex { get; }

        public int BodyAIndex { get; }

        public int BodyBIndex { get; }

        public Vector3 Normal { get; }

        public float Penetration { get; }

        public int PointCount { get; }

        public MiniContactPointFrame Point0 { get; }

        public MiniContactPointFrame Point1 { get; }

        public MiniContactPointFrame Point2 { get; }

        public MiniContactPointFrame Point3 { get; }

        public MiniContactPointFrame GetPoint(int pointIndex)
        {
            if ((uint)pointIndex >= (uint)PointCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pointIndex), "Contact point index is out of range.");
            }

            return pointIndex switch
            {
                0 => Point0,
                1 => Point1,
                2 => Point2,
                _ => Point3,
            };
        }

        public bool HasBodyA => BodyAIndex != MiniColliderFrame.NoBodyIndex;

        public bool HasBodyB => BodyBIndex != MiniColliderFrame.NoBodyIndex;

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            value = new Vector3(
                SanitizeFinite(value.x),
                SanitizeFinite(value.y),
                SanitizeFinite(value.z));
            float magnitude = value.magnitude;
            return magnitude > 0.0f ? value / magnitude : fallback;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsFinite(value) ? value : 0.0f;
        }
    }

    public readonly struct MiniBodyFrame
    {
        public MiniBodyFrame(
            MiniRigidBody body,
            MiniRigidBodyHandle handle,
            MiniRigidBodyType bodyType,
            Vector3 position,
            Quaternion rotation,
            Vector3 worldCenterOfMass,
            Vector3 linearVelocity,
            Vector3 angularVelocity,
            float inverseMass,
            Matrix4x4 worldInverseInertiaTensor)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
            Handle = handle;
            BodyType = bodyType;
            Position = position;
            Rotation = rotation;
            WorldCenterOfMass = worldCenterOfMass;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;
            InverseMass = inverseMass;
            WorldInverseInertiaTensor = worldInverseInertiaTensor;
        }

        public MiniRigidBody Body { get; }

        public MiniRigidBodyHandle Handle { get; }

        public MiniRigidBodyType BodyType { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 WorldCenterOfMass { get; }

        public Vector3 LinearVelocity { get; }

        public Vector3 AngularVelocity { get; }

        public float InverseMass { get; }

        public Matrix4x4 WorldInverseInertiaTensor { get; }

        public bool IsDynamic => BodyType == MiniRigidBodyType.Dynamic;

        public bool IsKinematic => BodyType == MiniRigidBodyType.Kinematic;

        public bool IsStatic => BodyType == MiniRigidBodyType.Static;
    }

    public readonly struct MiniColliderFrame
    {
        public const int NoBodyIndex = -1;

        public MiniColliderFrame(
            MiniCollider collider,
            MiniColliderHandle handle,
            int bodyIndex,
            Bounds worldBounds,
            MiniObbFrame worldObb,
            bool hasWorldBounds)
        {
            Collider = collider ?? throw new ArgumentNullException(nameof(collider));
            Handle = handle;
            BodyIndex = bodyIndex;
            WorldBounds = worldBounds;
            WorldObb = worldObb;
            HasWorldBounds = hasWorldBounds;
        }

        public MiniCollider Collider { get; }

        public MiniColliderHandle Handle { get; }

        public int BodyIndex { get; }

        public Bounds WorldBounds { get; }

        public MiniObbFrame WorldObb { get; }

        public bool HasWorldBounds { get; }

        public bool HasBody => BodyIndex != NoBodyIndex;
    }

    public readonly struct MiniObbFrame
    {
        public MiniObbFrame(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 extents)
        {
            Center = SanitizeVector(center);
            AxisX = NormalizeAxis(axisX, Vector3.right);
            AxisY = NormalizeAxis(axisY, Vector3.up);
            AxisZ = NormalizeAxis(axisZ, Vector3.forward);
            Extents = new Vector3(
                Mathf.Max(0.0f, SanitizeFinite(extents.x)),
                Mathf.Max(0.0f, SanitizeFinite(extents.y)),
                Mathf.Max(0.0f, SanitizeFinite(extents.z)));
        }

        public Vector3 Center { get; }

        public Vector3 AxisX { get; }

        public Vector3 AxisY { get; }

        public Vector3 AxisZ { get; }

        public Vector3 Extents { get; }

        public Bounds ToBounds()
        {
            Vector3 worldExtents = new Vector3(
                (Mathf.Abs(AxisX.x) * Extents.x) +
                (Mathf.Abs(AxisY.x) * Extents.y) +
                (Mathf.Abs(AxisZ.x) * Extents.z),
                (Mathf.Abs(AxisX.y) * Extents.x) +
                (Mathf.Abs(AxisY.y) * Extents.y) +
                (Mathf.Abs(AxisZ.y) * Extents.z),
                (Mathf.Abs(AxisX.z) * Extents.x) +
                (Mathf.Abs(AxisY.z) * Extents.y) +
                (Mathf.Abs(AxisZ.z) * Extents.z));

            return new Bounds(Center, worldExtents * 2.0f);
        }

        public static MiniObbFrame FromLocalBounds(Bounds localBounds, Matrix4x4 localToWorld)
        {
            Vector3 center = localToWorld.MultiplyPoint3x4(localBounds.center);
            Vector3 localExtents = localBounds.extents;

            Vector3 axisXVector = localToWorld.MultiplyVector(Vector3.right);
            Vector3 axisYVector = localToWorld.MultiplyVector(Vector3.up);
            Vector3 axisZVector = localToWorld.MultiplyVector(Vector3.forward);

            return new MiniObbFrame(
                center,
                axisXVector,
                axisYVector,
                axisZVector,
                new Vector3(
                    axisXVector.magnitude * localExtents.x,
                    axisYVector.magnitude * localExtents.y,
                    axisZVector.magnitude * localExtents.z));
        }

        private static Vector3 NormalizeAxis(Vector3 axis, Vector3 fallback)
        {
            axis = SanitizeVector(axis);
            float magnitude = axis.magnitude;
            return magnitude > 0.0f ? axis / magnitude : fallback;
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                SanitizeFinite(value.x),
                SanitizeFinite(value.y),
                SanitizeFinite(value.z));
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsFinite(value) ? value : 0.0f;
        }
    }
}
