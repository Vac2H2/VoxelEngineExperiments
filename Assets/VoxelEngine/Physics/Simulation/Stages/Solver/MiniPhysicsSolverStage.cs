using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Physics.Body;

namespace VoxelEngine.Physics.Simulation.Stages.Solver
{
    public sealed class MiniPhysicsSolverStage
    {
        private const float Epsilon = 0.000001f;

        private readonly List<SolverBody> _solverBodies = new List<SolverBody>();
        private readonly List<ContactConstraint> _constraints = new List<ContactConstraint>();

        public int IterationCount { get; set; } = 10;

        public float Restitution { get; set; } = 0.05f;

        public float Friction { get; set; } = 0.6f;

        public float Baumgarte { get; set; } = 0.2f;

        public float PenetrationSlop { get; set; } = 0.01f;

        public float MaxDepenetrationVelocity { get; set; } = 3.0f;

        public float RestitutionVelocityThreshold { get; set; } = 1.0f;

        public void Execute(MiniPhysicsFrameData frameData, float deltaTime)
        {
            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            _solverBodies.Clear();
            _constraints.Clear();

            if (deltaTime <= 0.0f || !float.IsFinite(deltaTime))
            {
                return;
            }

            BuildSolverBodies(frameData);
            BuildContactConstraints(frameData, deltaTime);
            SolveVelocityConstraints(Mathf.Max(1, IterationCount));
            WriteBackSolverBodies(frameData);
        }

        private void BuildSolverBodies(MiniPhysicsFrameData frameData)
        {
            var bodies = frameData.Bodies;
            for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                MiniBodyFrame bodyFrame = bodies[bodyIndex];
                MiniRigidBody body = bodyFrame.Body;
                bool isDynamic = bodyFrame.IsDynamic && body != null && body.isActiveAndEnabled;
                _solverBodies.Add(new SolverBody(
                    isDynamic,
                    bodyFrame.WorldCenterOfMass,
                    isDynamic ? body.InverseMass : 0.0f,
                    isDynamic ? body.WorldInverseInertiaTensor : Matrix4x4.zero,
                    body != null ? body.LinearVelocity : bodyFrame.LinearVelocity,
                    body != null ? body.AngularVelocity : bodyFrame.AngularVelocity));
            }
        }

        private void BuildContactConstraints(MiniPhysicsFrameData frameData, float deltaTime)
        {
            var manifolds = frameData.ContactManifolds;
            for (int manifoldIndex = 0; manifoldIndex < manifolds.Count; manifoldIndex++)
            {
                MiniContactManifoldFrame manifold = manifolds[manifoldIndex];
                int bodyAIndex = ResolveSolverBodyIndex(manifold.BodyAIndex);
                int bodyBIndex = ResolveSolverBodyIndex(manifold.BodyBIndex);

                if (!CanSolvePair(bodyAIndex, bodyBIndex))
                {
                    continue;
                }

                Vector3 normal = NormalizeOrFallback(manifold.Normal, Vector3.up);
                ContactConstraint constraint = new ContactConstraint(bodyAIndex, bodyBIndex, normal);

                for (int pointIndex = 0; pointIndex < manifold.PointCount; pointIndex++)
                {
                    MiniContactPointFrame contactPoint = manifold.GetPoint(pointIndex);
                    float penetration = contactPoint.Penetration > 0.0f
                        ? contactPoint.Penetration
                        : manifold.Penetration;
                    ContactConstraintPoint constraintPoint = BuildConstraintPoint(
                        bodyAIndex,
                        bodyBIndex,
                        contactPoint.Position,
                        normal,
                        penetration,
                        deltaTime);

                    if (constraintPoint.NormalMass <= 0.0f)
                    {
                        continue;
                    }

                    constraint.AddPoint(constraintPoint);
                }

                if (constraint.PointCount > 0)
                {
                    _constraints.Add(constraint);
                }
            }
        }

        private ContactConstraintPoint BuildConstraintPoint(
            int bodyAIndex,
            int bodyBIndex,
            Vector3 position,
            Vector3 normal,
            float penetration,
            float deltaTime)
        {
            SolverBody bodyA = GetSolverBodyOrStatic(bodyAIndex);
            SolverBody bodyB = GetSolverBodyOrStatic(bodyBIndex);
            Vector3 rA = bodyAIndex >= 0 ? position - bodyA.WorldCenterOfMass : Vector3.zero;
            Vector3 rB = bodyBIndex >= 0 ? position - bodyB.WorldCenterOfMass : Vector3.zero;
            Vector3 relativeVelocity = ComputeRelativeVelocity(bodyA, bodyB, rA, rB);
            float normalVelocity = Vector3.Dot(relativeVelocity, normal);

            Vector3 tangent1 = relativeVelocity - (normal * normalVelocity);
            tangent1 = tangent1.sqrMagnitude > Epsilon
                ? tangent1.normalized
                : BuildPerpendicular(normal);
            Vector3 tangent2 = Vector3.Cross(normal, tangent1).normalized;

            float restitutionBias = normalVelocity < -RestitutionVelocityThreshold
                ? -Mathf.Max(0.0f, Restitution) * normalVelocity
                : 0.0f;
            float penetrationBias = Mathf.Max(0.0f, penetration - Mathf.Max(0.0f, PenetrationSlop)) *
                Mathf.Max(0.0f, Baumgarte) /
                deltaTime;
            penetrationBias = Mathf.Min(penetrationBias, Mathf.Max(0.0f, MaxDepenetrationVelocity));

            return new ContactConstraintPoint(
                position,
                rA,
                rB,
                tangent1,
                tangent2,
                ComputeEffectiveMass(bodyA, bodyB, rA, rB, normal),
                ComputeEffectiveMass(bodyA, bodyB, rA, rB, tangent1),
                ComputeEffectiveMass(bodyA, bodyB, rA, rB, tangent2),
                restitutionBias + penetrationBias);
        }

        private void SolveVelocityConstraints(int iterationCount)
        {
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                for (int constraintIndex = 0; constraintIndex < _constraints.Count; constraintIndex++)
                {
                    ContactConstraint constraint = _constraints[constraintIndex];
                    for (int pointIndex = 0; pointIndex < constraint.PointCount; pointIndex++)
                    {
                        SolveNormalConstraint(ref constraint, pointIndex);
                        SolveFrictionConstraint(ref constraint, pointIndex);
                    }

                    _constraints[constraintIndex] = constraint;
                }
            }
        }

        private void SolveNormalConstraint(ref ContactConstraint constraint, int pointIndex)
        {
            ContactConstraintPoint point = constraint.GetPoint(pointIndex);
            SolverBody bodyA = GetSolverBodyOrStatic(constraint.BodyAIndex);
            SolverBody bodyB = GetSolverBodyOrStatic(constraint.BodyBIndex);
            Vector3 relativeVelocity = ComputeRelativeVelocity(bodyA, bodyB, point.RA, point.RB);
            float normalVelocity = Vector3.Dot(relativeVelocity, constraint.Normal);
            float impulseDelta = point.NormalMass * (point.VelocityBias - normalVelocity);
            float previousImpulse = point.NormalImpulse;

            point.NormalImpulse = Mathf.Max(previousImpulse + impulseDelta, 0.0f);
            impulseDelta = point.NormalImpulse - previousImpulse;

            ApplyImpulse(constraint.BodyAIndex, constraint.BodyBIndex, point.RA, point.RB, constraint.Normal * impulseDelta);
            constraint.SetPoint(pointIndex, point);
        }

        private void SolveFrictionConstraint(ref ContactConstraint constraint, int pointIndex)
        {
            ContactConstraintPoint point = constraint.GetPoint(pointIndex);
            float maxFrictionImpulse = Mathf.Max(0.0f, Friction) * point.NormalImpulse;
            if (maxFrictionImpulse <= 0.0f)
            {
                return;
            }

            SolveTangentConstraint(
                constraint.BodyAIndex,
                constraint.BodyBIndex,
                point.RA,
                point.RB,
                point.Tangent1,
                point.Tangent1Mass,
                maxFrictionImpulse,
                ref point.Tangent1Impulse);
            SolveTangentConstraint(
                constraint.BodyAIndex,
                constraint.BodyBIndex,
                point.RA,
                point.RB,
                point.Tangent2,
                point.Tangent2Mass,
                maxFrictionImpulse,
                ref point.Tangent2Impulse);

            constraint.SetPoint(pointIndex, point);
        }

        private void SolveTangentConstraint(
            int bodyAIndex,
            int bodyBIndex,
            Vector3 rA,
            Vector3 rB,
            Vector3 tangent,
            float tangentMass,
            float maxFrictionImpulse,
            ref float accumulatedImpulse)
        {
            if (tangentMass <= 0.0f)
            {
                return;
            }

            SolverBody bodyA = GetSolverBodyOrStatic(bodyAIndex);
            SolverBody bodyB = GetSolverBodyOrStatic(bodyBIndex);
            Vector3 relativeVelocity = ComputeRelativeVelocity(bodyA, bodyB, rA, rB);
            float tangentVelocity = Vector3.Dot(relativeVelocity, tangent);
            float impulseDelta = -tangentMass * tangentVelocity;
            float previousImpulse = accumulatedImpulse;

            accumulatedImpulse = Mathf.Clamp(previousImpulse + impulseDelta, -maxFrictionImpulse, maxFrictionImpulse);
            impulseDelta = accumulatedImpulse - previousImpulse;

            ApplyImpulse(bodyAIndex, bodyBIndex, rA, rB, tangent * impulseDelta);
        }

        private void ApplyImpulse(int bodyAIndex, int bodyBIndex, Vector3 rA, Vector3 rB, Vector3 impulse)
        {
            if (bodyAIndex >= 0)
            {
                SolverBody bodyA = _solverBodies[bodyAIndex];
                bodyA.ApplyImpulse(-impulse, rA);
                _solverBodies[bodyAIndex] = bodyA;
            }

            if (bodyBIndex >= 0)
            {
                SolverBody bodyB = _solverBodies[bodyBIndex];
                bodyB.ApplyImpulse(impulse, rB);
                _solverBodies[bodyBIndex] = bodyB;
            }
        }

        private void WriteBackSolverBodies(MiniPhysicsFrameData frameData)
        {
            var bodies = frameData.Bodies;
            for (int bodyIndex = 0; bodyIndex < _solverBodies.Count; bodyIndex++)
            {
                SolverBody solverBody = _solverBodies[bodyIndex];
                if (!solverBody.IsDynamic)
                {
                    continue;
                }

                MiniRigidBody body = bodies[bodyIndex].Body;
                if (body == null)
                {
                    continue;
                }

                body.LinearVelocity = solverBody.LinearVelocity;
                body.AngularVelocity = solverBody.AngularVelocity;
            }
        }

        private bool CanSolvePair(int bodyAIndex, int bodyBIndex)
        {
            return IsDynamicSolverBody(bodyAIndex) || IsDynamicSolverBody(bodyBIndex);
        }

        private bool IsDynamicSolverBody(int bodyIndex)
        {
            return bodyIndex >= 0 && bodyIndex < _solverBodies.Count && _solverBodies[bodyIndex].IsDynamic;
        }

        private SolverBody GetSolverBodyOrStatic(int bodyIndex)
        {
            return bodyIndex >= 0 && bodyIndex < _solverBodies.Count
                ? _solverBodies[bodyIndex]
                : SolverBody.Static;
        }

        private static int ResolveSolverBodyIndex(int bodyIndex)
        {
            return bodyIndex == MiniColliderFrame.NoBodyIndex ? -1 : bodyIndex;
        }

        private static Vector3 ComputeRelativeVelocity(SolverBody bodyA, SolverBody bodyB, Vector3 rA, Vector3 rB)
        {
            Vector3 velocityA = bodyA.LinearVelocity + Vector3.Cross(bodyA.AngularVelocity, rA);
            Vector3 velocityB = bodyB.LinearVelocity + Vector3.Cross(bodyB.AngularVelocity, rB);
            return velocityB - velocityA;
        }

        private static float ComputeEffectiveMass(
            SolverBody bodyA,
            SolverBody bodyB,
            Vector3 rA,
            Vector3 rB,
            Vector3 axis)
        {
            float inverseMass = bodyA.InverseMass + bodyB.InverseMass;
            Vector3 angularA = Vector3.Cross(bodyA.WorldInverseInertiaTensor.MultiplyVector(Vector3.Cross(rA, axis)), rA);
            Vector3 angularB = Vector3.Cross(bodyB.WorldInverseInertiaTensor.MultiplyVector(Vector3.Cross(rB, axis)), rB);
            float denominator = inverseMass + Vector3.Dot(axis, angularA + angularB);
            return denominator > Epsilon ? 1.0f / denominator : 0.0f;
        }

        private static Vector3 BuildPerpendicular(Vector3 normal)
        {
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < Epsilon)
            {
                tangent = Vector3.Cross(normal, Vector3.right);
            }

            return tangent.normalized;
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            float magnitude = value.magnitude;
            return magnitude > 0.0f ? value / magnitude : fallback;
        }

        private struct SolverBody
        {
            public static readonly SolverBody Static = new SolverBody(false, Vector3.zero, 0.0f, Matrix4x4.zero, Vector3.zero, Vector3.zero);

            public SolverBody(
                bool isDynamic,
                Vector3 worldCenterOfMass,
                float inverseMass,
                Matrix4x4 worldInverseInertiaTensor,
                Vector3 linearVelocity,
                Vector3 angularVelocity)
            {
                IsDynamic = isDynamic;
                WorldCenterOfMass = worldCenterOfMass;
                InverseMass = inverseMass;
                WorldInverseInertiaTensor = worldInverseInertiaTensor;
                LinearVelocity = linearVelocity;
                AngularVelocity = angularVelocity;
            }

            public bool IsDynamic;

            public Vector3 WorldCenterOfMass;

            public float InverseMass;

            public Matrix4x4 WorldInverseInertiaTensor;

            public Vector3 LinearVelocity;

            public Vector3 AngularVelocity;

            public void ApplyImpulse(Vector3 impulse, Vector3 r)
            {
                if (!IsDynamic)
                {
                    return;
                }

                LinearVelocity += impulse * InverseMass;
                AngularVelocity += WorldInverseInertiaTensor.MultiplyVector(Vector3.Cross(r, impulse));
            }
        }

        private struct ContactConstraint
        {
            private ContactConstraintPoint _point0;
            private ContactConstraintPoint _point1;
            private ContactConstraintPoint _point2;
            private ContactConstraintPoint _point3;

            public ContactConstraint(int bodyAIndex, int bodyBIndex, Vector3 normal)
            {
                BodyAIndex = bodyAIndex;
                BodyBIndex = bodyBIndex;
                Normal = normal;
                PointCount = 0;
                _point0 = default;
                _point1 = default;
                _point2 = default;
                _point3 = default;
            }

            public int BodyAIndex { get; }

            public int BodyBIndex { get; }

            public Vector3 Normal { get; }

            public int PointCount { get; private set; }

            public void AddPoint(ContactConstraintPoint point)
            {
                if (PointCount >= MiniContactManifoldFrame.MaxPointCount)
                {
                    return;
                }

                SetPoint(PointCount, point);
                PointCount++;
            }

            public ContactConstraintPoint GetPoint(int pointIndex)
            {
                return pointIndex switch
                {
                    0 => _point0,
                    1 => _point1,
                    2 => _point2,
                    _ => _point3,
                };
            }

            public void SetPoint(int pointIndex, ContactConstraintPoint point)
            {
                switch (pointIndex)
                {
                    case 0:
                        _point0 = point;
                        break;
                    case 1:
                        _point1 = point;
                        break;
                    case 2:
                        _point2 = point;
                        break;
                    default:
                        _point3 = point;
                        break;
                }
            }
        }

        private struct ContactConstraintPoint
        {
            public ContactConstraintPoint(
                Vector3 position,
                Vector3 rA,
                Vector3 rB,
                Vector3 tangent1,
                Vector3 tangent2,
                float normalMass,
                float tangent1Mass,
                float tangent2Mass,
                float velocityBias)
            {
                Position = position;
                RA = rA;
                RB = rB;
                Tangent1 = tangent1;
                Tangent2 = tangent2;
                NormalMass = normalMass;
                Tangent1Mass = tangent1Mass;
                Tangent2Mass = tangent2Mass;
                VelocityBias = velocityBias;
                NormalImpulse = 0.0f;
                Tangent1Impulse = 0.0f;
                Tangent2Impulse = 0.0f;
            }

            public Vector3 Position;

            public Vector3 RA;

            public Vector3 RB;

            public Vector3 Tangent1;

            public Vector3 Tangent2;

            public float NormalMass;

            public float Tangent1Mass;

            public float Tangent2Mass;

            public float VelocityBias;

            public float NormalImpulse;

            public float Tangent1Impulse;

            public float Tangent2Impulse;
        }
    }
}
