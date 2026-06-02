using System;
using System.Collections.Generic;
using VoxelEngine.Physics.Body;
using VoxelEngine.Physics.Collider;

namespace VoxelEngine.Physics.World
{
    public sealed class MiniPhysicsWorld
    {
        private readonly List<MiniRigidBody> _rigidBodies = new List<MiniRigidBody>();
        private readonly Dictionary<MiniRigidBody, int> _rigidBodyIndices = new Dictionary<MiniRigidBody, int>();
        private readonly Dictionary<MiniRigidBody, MiniRigidBodyHandle> _handlesByRigidBody =
            new Dictionary<MiniRigidBody, MiniRigidBodyHandle>();
        private readonly Dictionary<int, MiniRigidBody> _rigidBodiesByHandle = new Dictionary<int, MiniRigidBody>();
        private readonly Stack<int> _freeRigidBodyHandleValues = new Stack<int>();

        private readonly List<MiniCollider> _colliders = new List<MiniCollider>();
        private readonly Dictionary<MiniCollider, int> _colliderIndices = new Dictionary<MiniCollider, int>();
        private readonly Dictionary<MiniCollider, MiniColliderHandle> _handlesByCollider =
            new Dictionary<MiniCollider, MiniColliderHandle>();
        private readonly Dictionary<int, MiniCollider> _collidersByHandle = new Dictionary<int, MiniCollider>();
        private readonly Stack<int> _freeColliderHandleValues = new Stack<int>();

        public static MiniPhysicsWorld Default { get; } = new MiniPhysicsWorld();

        public IReadOnlyList<MiniRigidBody> RigidBodies => _rigidBodies;

        public IReadOnlyList<MiniCollider> Colliders => _colliders;

        public int RigidBodyCount => _rigidBodies.Count;

        public int ColliderCount => _colliders.Count;

        public MiniRigidBodyHandle RegisterRigidBody(MiniRigidBody rigidBody)
        {
            if (rigidBody == null)
            {
                throw new ArgumentNullException(nameof(rigidBody));
            }

            if (_handlesByRigidBody.TryGetValue(rigidBody, out MiniRigidBodyHandle existingHandle))
            {
                return existingHandle;
            }

            MiniRigidBodyHandle handle = AllocateRigidBodyHandle();
            _rigidBodyIndices.Add(rigidBody, _rigidBodies.Count);
            _handlesByRigidBody.Add(rigidBody, handle);
            _rigidBodiesByHandle.Add(handle.Value, rigidBody);
            _rigidBodies.Add(rigidBody);
            return handle;
        }

        public bool UnregisterRigidBody(MiniRigidBody rigidBody)
        {
            if (rigidBody == null)
            {
                return false;
            }

            if (!_handlesByRigidBody.TryGetValue(rigidBody, out MiniRigidBodyHandle handle))
            {
                return false;
            }

            RemoveRigidBody(rigidBody, handle);
            return true;
        }

        public bool UnregisterRigidBody(MiniRigidBodyHandle handle)
        {
            if (!handle.IsValid || !_rigidBodiesByHandle.TryGetValue(handle.Value, out MiniRigidBody rigidBody))
            {
                return false;
            }

            RemoveRigidBody(rigidBody, handle);
            return true;
        }

        public bool TryGetRigidBody(MiniRigidBodyHandle handle, out MiniRigidBody rigidBody)
        {
            if (!handle.IsValid)
            {
                rigidBody = null;
                return false;
            }

            return _rigidBodiesByHandle.TryGetValue(handle.Value, out rigidBody);
        }

        public MiniColliderHandle RegisterCollider(MiniCollider collider)
        {
            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            if (_handlesByCollider.TryGetValue(collider, out MiniColliderHandle existingHandle))
            {
                return existingHandle;
            }

            MiniColliderHandle handle = AllocateColliderHandle();
            _colliderIndices.Add(collider, _colliders.Count);
            _handlesByCollider.Add(collider, handle);
            _collidersByHandle.Add(handle.Value, collider);
            _colliders.Add(collider);
            return handle;
        }

        public bool UnregisterCollider(MiniCollider collider)
        {
            if (collider == null)
            {
                return false;
            }

            if (!_handlesByCollider.TryGetValue(collider, out MiniColliderHandle handle))
            {
                return false;
            }

            RemoveCollider(collider, handle);
            return true;
        }

        public bool UnregisterCollider(MiniColliderHandle handle)
        {
            if (!handle.IsValid || !_collidersByHandle.TryGetValue(handle.Value, out MiniCollider collider))
            {
                return false;
            }

            RemoveCollider(collider, handle);
            return true;
        }

        public bool TryGetCollider(MiniColliderHandle handle, out MiniCollider collider)
        {
            if (!handle.IsValid)
            {
                collider = null;
                return false;
            }

            return _collidersByHandle.TryGetValue(handle.Value, out collider);
        }

        public void Clear()
        {
            _rigidBodies.Clear();
            _rigidBodyIndices.Clear();
            _handlesByRigidBody.Clear();
            _rigidBodiesByHandle.Clear();
            _freeRigidBodyHandleValues.Clear();

            _colliders.Clear();
            _colliderIndices.Clear();
            _handlesByCollider.Clear();
            _collidersByHandle.Clear();
            _freeColliderHandleValues.Clear();
        }

        private MiniRigidBodyHandle AllocateRigidBodyHandle()
        {
            int handleValue = _freeRigidBodyHandleValues.Count > 0
                ? _freeRigidBodyHandleValues.Pop()
                : checked(_rigidBodiesByHandle.Count + _freeRigidBodyHandleValues.Count + 1);
            return new MiniRigidBodyHandle(handleValue);
        }

        private MiniColliderHandle AllocateColliderHandle()
        {
            int handleValue = _freeColliderHandleValues.Count > 0
                ? _freeColliderHandleValues.Pop()
                : checked(_collidersByHandle.Count + _freeColliderHandleValues.Count + 1);
            return new MiniColliderHandle(handleValue);
        }

        private void RemoveRigidBody(MiniRigidBody rigidBody, MiniRigidBodyHandle handle)
        {
            int removedIndex = _rigidBodyIndices[rigidBody];
            int lastIndex = _rigidBodies.Count - 1;
            MiniRigidBody lastRigidBody = _rigidBodies[lastIndex];

            _rigidBodies[removedIndex] = lastRigidBody;
            _rigidBodyIndices[lastRigidBody] = removedIndex;
            _rigidBodies.RemoveAt(lastIndex);

            _rigidBodyIndices.Remove(rigidBody);
            _handlesByRigidBody.Remove(rigidBody);
            _rigidBodiesByHandle.Remove(handle.Value);
            _freeRigidBodyHandleValues.Push(handle.Value);
        }

        private void RemoveCollider(MiniCollider collider, MiniColliderHandle handle)
        {
            int removedIndex = _colliderIndices[collider];
            int lastIndex = _colliders.Count - 1;
            MiniCollider lastCollider = _colliders[lastIndex];

            _colliders[removedIndex] = lastCollider;
            _colliderIndices[lastCollider] = removedIndex;
            _colliders.RemoveAt(lastIndex);

            _colliderIndices.Remove(collider);
            _handlesByCollider.Remove(collider);
            _collidersByHandle.Remove(handle.Value);
            _freeColliderHandleValues.Push(handle.Value);
        }
    }

    public readonly struct MiniRigidBodyHandle : IEquatable<MiniRigidBodyHandle>
    {
        public const int InvalidValue = 0;

        internal MiniRigidBodyHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != InvalidValue;

        public bool Equals(MiniRigidBodyHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is MiniRigidBodyHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(MiniRigidBodyHandle left, MiniRigidBodyHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MiniRigidBodyHandle left, MiniRigidBodyHandle right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct MiniColliderHandle : IEquatable<MiniColliderHandle>
    {
        public const int InvalidValue = 0;

        internal MiniColliderHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != InvalidValue;

        public bool Equals(MiniColliderHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is MiniColliderHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(MiniColliderHandle left, MiniColliderHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MiniColliderHandle left, MiniColliderHandle right)
        {
            return !left.Equals(right);
        }
    }
}
