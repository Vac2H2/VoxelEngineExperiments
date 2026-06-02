using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public struct VoxelPhysicsBodyState
    {
        public VoxelPhysicsBodyState(
            VoxelPhysicsBodyHandle handle,
            float4x4 localToWorld)
        {
            Handle = handle;
            LocalToWorld = localToWorld;
            LinearVelocity = float3.zero;
            AngularVelocity = float3.zero;
            IsAlive = true;
        }

        public VoxelPhysicsBodyHandle Handle;
        public float4x4 LocalToWorld;
        public float3 LinearVelocity;
        public float3 AngularVelocity;
        public bool IsAlive;
    }

    public sealed class VoxelPhysicsBodyStateDatabase : IDisposable
    {
        private const string ContainerName = "VoxelPhysicsBodyStateDatabase";

        private NativeList<VoxelPhysicsBodyState> _states;
        private int _activeBodyCount;
        private bool _isDisposed;

        public VoxelPhysicsBodyStateDatabase(
            int initialBodyCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateInitialBodyCapacity(initialBodyCapacity);
            _states = new NativeList<VoxelPhysicsBodyState>(
                initialBodyCapacity,
                allocator);
        }

        public bool IsCreated =>
            !_isDisposed &&
            _states.IsCreated;

        public int ActiveBodyCount
        {
            get
            {
                ThrowIfDisposed();
                return _activeBodyCount;
            }
        }

        public int BodySlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _states.Length;
            }
        }

        internal bool RegisterBody(
            VoxelPhysicsBodyHandle bodyHandle,
            in VoxelPhysicsBodyConfig config)
        {
            ThrowIfDisposed();

            if (!bodyHandle.IsValid)
            {
                return false;
            }

            EnsureSlotExists(bodyHandle.Index);

            VoxelPhysicsBodyState existingState = _states[bodyHandle.Index];
            if (existingState.IsAlive)
            {
                return false;
            }

            _states[bodyHandle.Index] = new VoxelPhysicsBodyState(
                bodyHandle,
                config.LocalToWorld);
            _activeBodyCount++;
            return true;
        }

        internal bool DestroyBody(VoxelPhysicsBodyHandle bodyHandle)
        {
            ThrowIfDisposed();

            if (!TryGetAliveStateIndex(bodyHandle, out int bodyId))
            {
                return false;
            }

            VoxelPhysicsBodyState state = _states[bodyId];
            state.IsAlive = false;
            _states[bodyId] = state;
            _activeBodyCount--;
            return true;
        }

        internal bool ContainsBody(VoxelPhysicsBodyHandle bodyHandle)
        {
            ThrowIfDisposed();
            return TryGetAliveStateIndex(bodyHandle, out _);
        }

        internal bool TryGetBodyState(
            VoxelPhysicsBodyHandle bodyHandle,
            out VoxelPhysicsBodyState state)
        {
            ThrowIfDisposed();

            if (!TryGetAliveStateIndex(bodyHandle, out int bodyId))
            {
                state = default;
                return false;
            }

            state = _states[bodyId];
            return true;
        }

        internal bool TrySetBodyState(
            VoxelPhysicsBodyHandle bodyHandle,
            VoxelPhysicsBodyState state)
        {
            ThrowIfDisposed();

            if (!TryGetAliveStateIndex(bodyHandle, out int bodyId) ||
                state.Handle.Index != bodyHandle.Index ||
                state.Handle.Version != bodyHandle.Version ||
                !state.IsAlive)
            {
                return false;
            }

            _states[bodyId] = state;
            return true;
        }

        internal bool TrySetVelocities(
            VoxelPhysicsBodyHandle bodyHandle,
            float3 linearVelocity,
            float3 angularVelocity)
        {
            ThrowIfDisposed();

            if (!TryGetAliveStateIndex(bodyHandle, out int bodyId))
            {
                return false;
            }

            VoxelPhysicsBodyState state = _states[bodyId];
            state.LinearVelocity = linearVelocity;
            state.AngularVelocity = angularVelocity;
            _states[bodyId] = state;
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_states.IsCreated)
            {
                _states.Dispose();
            }

            _activeBodyCount = 0;
            _isDisposed = true;
        }

        private void EnsureSlotExists(int bodyId)
        {
            int requiredLength = bodyId + 1;
            if (_states.Length < requiredLength)
            {
                _states.Resize(requiredLength, NativeArrayOptions.ClearMemory);
            }
        }

        private bool TryGetAliveStateIndex(
            VoxelPhysicsBodyHandle bodyHandle,
            out int bodyId)
        {
            if (!bodyHandle.IsValid || (uint)bodyHandle.Index >= (uint)_states.Length)
            {
                bodyId = -1;
                return false;
            }

            VoxelPhysicsBodyState state = _states[bodyHandle.Index];
            if (!state.IsAlive ||
                state.Handle.Version != bodyHandle.Version)
            {
                bodyId = -1;
                return false;
            }

            bodyId = bodyHandle.Index;
            return true;
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        private static void ValidateInitialBodyCapacity(int initialBodyCapacity)
        {
            if (initialBodyCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialBodyCapacity),
                    "Initial body capacity must be greater than zero.");
            }
        }
    }
}
