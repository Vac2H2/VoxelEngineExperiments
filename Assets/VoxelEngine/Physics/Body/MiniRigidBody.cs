using System;
using UnityEngine;
using VoxelEngine.Physics.World;

namespace VoxelEngine.Physics.Body
{
    public enum MiniRigidBodyType
    {
        Static = 0,
        Dynamic = 1,
        Kinematic = 2,
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Physics/Mini Rigid Body")]
    public sealed class MiniRigidBody : MonoBehaviour
    {
        private const float MinimumMass = 0.000001f;
        private const float MinimumInertia = 0.000001f;

        [SerializeField] private MiniRigidBodyType _bodyType = MiniRigidBodyType.Dynamic;
        [SerializeField] private float _mass = 1.0f;
        [SerializeField] private Vector3 _centerOfMass = Vector3.zero;
        [SerializeField] private Vector3 _inertiaTensor = Vector3.one;
        [SerializeField] private Quaternion _inertiaTensorRotation = Quaternion.identity;
        [SerializeField] private bool _useGravity = true;
        [SerializeField] private float _gravityScale = 1.0f;
        [SerializeField] private float _linearDamping;
        [SerializeField] private float _angularDamping;
        [SerializeField] private Vector3 _linearVelocity;
        [SerializeField] private Vector3 _angularVelocity;

        private Vector3 _accumulatedForce;
        private Vector3 _accumulatedTorque;
        private MiniRigidBodyHandle _handle;

        public MiniRigidBodyHandle Handle => _handle;

        public bool IsRegistered => _handle.IsValid;

        public MiniRigidBodyType BodyType
        {
            get => _bodyType;
            set => _bodyType = value;
        }

        public bool IsStatic => _bodyType == MiniRigidBodyType.Static;

        public bool IsDynamic => _bodyType == MiniRigidBodyType.Dynamic;

        public bool IsKinematic => _bodyType == MiniRigidBodyType.Kinematic;

        public float Mass
        {
            get => _mass;
            set => _mass = SanitizePositive(value, MinimumMass);
        }

        public float InverseMass => IsDynamic ? 1.0f / _mass : 0.0f;

        public Vector3 LocalCenterOfMass
        {
            get => _centerOfMass;
            set => _centerOfMass = SanitizeVector(value);
        }

        public Vector3 WorldCenterOfMass => transform.position + (transform.rotation * _centerOfMass);

        public Vector3 InertiaTensor
        {
            get => _inertiaTensor;
            set => _inertiaTensor = SanitizePositiveVector(value, MinimumInertia);
        }

        public Vector3 InverseInertiaTensor => IsDynamic
            ? new Vector3(1.0f / _inertiaTensor.x, 1.0f / _inertiaTensor.y, 1.0f / _inertiaTensor.z)
            : Vector3.zero;

        public Quaternion InertiaTensorRotation
        {
            get => _inertiaTensorRotation;
            set => _inertiaTensorRotation = SanitizeRotation(value);
        }

        public Matrix4x4 WorldInverseInertiaTensor
        {
            get
            {
                if (!IsDynamic)
                {
                    return Matrix4x4.zero;
                }

                Matrix4x4 rotation = Matrix4x4.Rotate(transform.rotation * _inertiaTensorRotation);
                Matrix4x4 inverseInertia = Matrix4x4.Scale(InverseInertiaTensor);
                return rotation * inverseInertia * rotation.transpose;
            }
        }

        public bool UseGravity
        {
            get => _useGravity;
            set => _useGravity = value;
        }

        public float GravityScale
        {
            get => _gravityScale;
            set => _gravityScale = SanitizeFinite(value);
        }

        public float LinearDamping
        {
            get => _linearDamping;
            set => _linearDamping = Mathf.Max(0.0f, SanitizeFinite(value));
        }

        public float AngularDamping
        {
            get => _angularDamping;
            set => _angularDamping = Mathf.Max(0.0f, SanitizeFinite(value));
        }

        public Vector3 LinearVelocity
        {
            get => _linearVelocity;
            set => _linearVelocity = SanitizeVector(value);
        }

        public Vector3 AngularVelocity
        {
            get => _angularVelocity;
            set => _angularVelocity = SanitizeVector(value);
        }

        public Vector3 AccumulatedForce => _accumulatedForce;

        public Vector3 AccumulatedTorque => _accumulatedTorque;

        public void AddForce(Vector3 force)
        {
            if (!IsDynamic)
            {
                return;
            }

            _accumulatedForce += SanitizeVector(force);
        }

        public void AddAcceleration(Vector3 acceleration)
        {
            if (!IsDynamic)
            {
                return;
            }

            AddForce(SanitizeVector(acceleration) * _mass);
        }

        public void AddTorque(Vector3 torque)
        {
            if (!IsDynamic)
            {
                return;
            }

            _accumulatedTorque += SanitizeVector(torque);
        }

        public void AddForceAtWorldPoint(Vector3 force, Vector3 worldPoint)
        {
            if (!IsDynamic)
            {
                return;
            }

            Vector3 sanitizedForce = SanitizeVector(force);
            AddForce(sanitizedForce);
            AddTorque(Vector3.Cross(SanitizeVector(worldPoint) - WorldCenterOfMass, sanitizedForce));
        }

        public void AddImpulse(Vector3 impulse)
        {
            if (!IsDynamic)
            {
                return;
            }

            LinearVelocity += SanitizeVector(impulse) * InverseMass;
        }

        public void AddAngularImpulse(Vector3 angularImpulse)
        {
            if (!IsDynamic)
            {
                return;
            }

            AngularVelocity += WorldInverseInertiaTensor.MultiplyVector(SanitizeVector(angularImpulse));
        }

        public void AddImpulseAtWorldPoint(Vector3 impulse, Vector3 worldPoint)
        {
            if (!IsDynamic)
            {
                return;
            }

            Vector3 sanitizedImpulse = SanitizeVector(impulse);
            AddImpulse(sanitizedImpulse);
            AddAngularImpulse(Vector3.Cross(SanitizeVector(worldPoint) - WorldCenterOfMass, sanitizedImpulse));
        }

        public void ConsumeAccumulators(out Vector3 force, out Vector3 torque)
        {
            force = _accumulatedForce;
            torque = _accumulatedTorque;
            ClearAccumulators();
        }

        public void ClearAccumulators()
        {
            _accumulatedForce = Vector3.zero;
            _accumulatedTorque = Vector3.zero;
        }

        public void IntegrateForces(Vector3 gravity, float deltaTime)
        {
            if (!IsDynamic || deltaTime <= 0.0f || !float.IsFinite(deltaTime))
            {
                ClearAccumulators();
                return;
            }

            Vector3 force = _accumulatedForce;
            Vector3 torque = _accumulatedTorque;
            ClearAccumulators();

            if (_useGravity)
            {
                force += SanitizeVector(gravity) * (_mass * _gravityScale);
            }

            LinearVelocity += force * (InverseMass * deltaTime);
            AngularVelocity += WorldInverseInertiaTensor.MultiplyVector(torque) * deltaTime;
        }

        public void IntegrateTransform(float deltaTime)
        {
            if (!IsDynamic || deltaTime <= 0.0f || !float.IsFinite(deltaTime))
            {
                return;
            }

            ApplyDamping(deltaTime);

            Vector3 nextWorldCenterOfMass = WorldCenterOfMass + (_linearVelocity * deltaTime);
            Quaternion nextRotation = IntegrateAngularStep(transform.rotation, deltaTime);

            transform.rotation = nextRotation;
            transform.position = nextWorldCenterOfMass - GetWorldCenterOfMassOffset(nextRotation);
        }

        public void MovePosition(Vector3 worldPosition)
        {
            transform.position = SanitizeVector(worldPosition);
        }

        public void MoveRotation(Quaternion worldRotation)
        {
            transform.rotation = SanitizeRotation(worldRotation);
        }

        public void SetSolidBoxInertiaTensor(Vector3 size)
        {
            InertiaTensor = ComputeSolidBoxInertiaTensor(_mass, size);
            InertiaTensorRotation = Quaternion.identity;
        }

        public void SetSolidSphereInertiaTensor(float radius)
        {
            InertiaTensor = ComputeSolidSphereInertiaTensor(_mass, radius);
            InertiaTensorRotation = Quaternion.identity;
        }

        public static Vector3 ComputeSolidBoxInertiaTensor(float mass, Vector3 size)
        {
            float sanitizedMass = SanitizePositive(mass, MinimumMass);
            Vector3 sanitizedSize = SanitizePositiveVector(size, MinimumInertia);
            float x2 = sanitizedSize.x * sanitizedSize.x;
            float y2 = sanitizedSize.y * sanitizedSize.y;
            float z2 = sanitizedSize.z * sanitizedSize.z;
            float scale = sanitizedMass / 12.0f;
            return new Vector3(
                scale * (y2 + z2),
                scale * (x2 + z2),
                scale * (x2 + y2));
        }

        public static Vector3 ComputeSolidSphereInertiaTensor(float mass, float radius)
        {
            float sanitizedMass = SanitizePositive(mass, MinimumMass);
            float sanitizedRadius = SanitizePositive(radius, MinimumInertia);
            float inertia = 0.4f * sanitizedMass * sanitizedRadius * sanitizedRadius;
            return new Vector3(inertia, inertia, inertia);
        }

        private void Awake()
        {
            NormalizeSerializedState();
            _handle = MiniPhysicsWorld.Default.RegisterRigidBody(this);
        }

        private void OnDestroy()
        {
            if (!_handle.IsValid)
            {
                return;
            }

            MiniPhysicsWorld.Default.UnregisterRigidBody(_handle);
            _handle = default;
        }

        private void OnValidate()
        {
            NormalizeSerializedState();
        }

        private void NormalizeSerializedState()
        {
            _mass = SanitizePositive(_mass, MinimumMass);
            _centerOfMass = SanitizeVector(_centerOfMass);
            _inertiaTensor = SanitizePositiveVector(_inertiaTensor, MinimumInertia);
            _inertiaTensorRotation = SanitizeRotation(_inertiaTensorRotation);
            _gravityScale = SanitizeFinite(_gravityScale);
            _linearDamping = Mathf.Max(0.0f, SanitizeFinite(_linearDamping));
            _angularDamping = Mathf.Max(0.0f, SanitizeFinite(_angularDamping));
            _linearVelocity = SanitizeVector(_linearVelocity);
            _angularVelocity = SanitizeVector(_angularVelocity);
            _accumulatedForce = SanitizeVector(_accumulatedForce);
            _accumulatedTorque = SanitizeVector(_accumulatedTorque);
        }

        private void ApplyDamping(float deltaTime)
        {
            _linearVelocity *= ComputeDampingMultiplier(_linearDamping, deltaTime);
            _angularVelocity *= ComputeDampingMultiplier(_angularDamping, deltaTime);
        }

        private Quaternion IntegrateAngularStep(Quaternion currentRotation, float deltaTime)
        {
            float angularSpeed = _angularVelocity.magnitude;
            if (angularSpeed <= 0.0f)
            {
                return SanitizeRotation(currentRotation);
            }

            float angleDegrees = angularSpeed * deltaTime * Mathf.Rad2Deg;
            Quaternion deltaRotation = Quaternion.AngleAxis(angleDegrees, _angularVelocity / angularSpeed);
            return SanitizeRotation(deltaRotation * currentRotation);
        }

        private Vector3 GetWorldCenterOfMassOffset(Quaternion worldRotation)
        {
            return worldRotation * _centerOfMass;
        }

        private static float ComputeDampingMultiplier(float damping, float deltaTime)
        {
            return 1.0f / (1.0f + (damping * deltaTime));
        }

        private static float SanitizePositive(float value, float minimum)
        {
            if (!float.IsFinite(value))
            {
                return minimum;
            }

            return Mathf.Max(minimum, value);
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsFinite(value) ? value : 0.0f;
        }

        private static Vector3 SanitizePositiveVector(Vector3 value, float minimum)
        {
            value = SanitizeVector(value);
            return new Vector3(
                Mathf.Max(minimum, value.x),
                Mathf.Max(minimum, value.y),
                Mathf.Max(minimum, value.z));
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                SanitizeFinite(value.x),
                SanitizeFinite(value.y),
                SanitizeFinite(value.z));
        }

        private static Quaternion SanitizeRotation(Quaternion rotation)
        {
            if (!float.IsFinite(rotation.x) ||
                !float.IsFinite(rotation.y) ||
                !float.IsFinite(rotation.z) ||
                !float.IsFinite(rotation.w))
            {
                return Quaternion.identity;
            }

            float lengthSquared =
                (rotation.x * rotation.x) +
                (rotation.y * rotation.y) +
                (rotation.z * rotation.z) +
                (rotation.w * rotation.w);

            if (lengthSquared <= 0.0f)
            {
                return Quaternion.identity;
            }

            float inverseLength = 1.0f / Mathf.Sqrt(lengthSquared);
            return new Quaternion(
                rotation.x * inverseLength,
                rotation.y * inverseLength,
                rotation.z * inverseLength,
                rotation.w * inverseLength);
        }
    }
}
