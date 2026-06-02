using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineDOTS.Physics.Backend
{
    [Flags]
    public enum VoxelPhysicsBodyStructuralChangeFlags : byte
    {
        None = 0,
        Created = 1 << 0,
        Destroyed = 1 << 1,
        Chunks = 1 << 2,
    }

    [Flags]
    public enum VoxelPhysicsChunkStructuralChangeFlags : byte
    {
        None = 0,
        Added = 1 << 0,
        Removed = 1 << 1,
        Bounds = 1 << 2,
    }

    public readonly struct VoxelPhysicsBodyStructuralChangeKey :
        IEquatable<VoxelPhysicsBodyStructuralChangeKey>
    {
        public VoxelPhysicsBodyStructuralChangeKey(VoxelPhysicsBodyHandle bodyHandle)
            : this(bodyHandle.Index, bodyHandle.Version)
        {
        }

        public VoxelPhysicsBodyStructuralChangeKey(int bodyId, int bodyVersion)
        {
            BodyId = bodyId;
            BodyVersion = bodyVersion;
        }

        public int BodyId { get; }
        public int BodyVersion { get; }

        public VoxelPhysicsBodyHandle BodyHandle =>
            new VoxelPhysicsBodyHandle(BodyId, BodyVersion);

        public bool Equals(VoxelPhysicsBodyStructuralChangeKey other)
        {
            return BodyId == other.BodyId &&
                   BodyVersion == other.BodyVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelPhysicsBodyStructuralChangeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)math.hash(new int2(BodyId, BodyVersion));
        }
    }

    public readonly struct VoxelPhysicsChunkStructuralChangeKey :
        IEquatable<VoxelPhysicsChunkStructuralChangeKey>
    {
        public VoxelPhysicsChunkStructuralChangeKey(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
            : this(bodyHandle.Index, bodyHandle.Version, chunkPosition)
        {
        }

        public VoxelPhysicsChunkStructuralChangeKey(
            int bodyId,
            int bodyVersion,
            int3 chunkPosition)
        {
            BodyId = bodyId;
            BodyVersion = bodyVersion;
            ChunkPosition = chunkPosition;
        }

        public int BodyId { get; }
        public int BodyVersion { get; }
        public int3 ChunkPosition { get; }

        public VoxelPhysicsBodyHandle BodyHandle =>
            new VoxelPhysicsBodyHandle(BodyId, BodyVersion);

        public bool Equals(VoxelPhysicsChunkStructuralChangeKey other)
        {
            return BodyId == other.BodyId &&
                   BodyVersion == other.BodyVersion &&
                   math.all(ChunkPosition == other.ChunkPosition);
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelPhysicsChunkStructuralChangeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            uint bodyAndChunkHash = math.hash(new int4(
                BodyId,
                BodyVersion,
                ChunkPosition.x,
                ChunkPosition.y));

            return (int)math.hash(new int2(
                (int)bodyAndChunkHash,
                ChunkPosition.z));
        }
    }

    public sealed class VoxelPhysicsStructuralChangeTracker : IDisposable
    {
        private const string ContainerName = "VoxelPhysicsStructuralChangeTracker";

        private NativeList<VoxelPhysicsBodyStructuralChangeKey> _dirtyBodyKeys;
        private NativeHashMap<VoxelPhysicsBodyStructuralChangeKey, VoxelPhysicsBodyStructuralChangeFlags> _bodyFlagsByKey;
        private NativeList<VoxelPhysicsChunkStructuralChangeKey> _dirtyChunkKeys;
        private NativeHashMap<VoxelPhysicsChunkStructuralChangeKey, VoxelPhysicsChunkStructuralChangeFlags> _chunkFlagsByKey;
        private bool _isDisposed;

        public VoxelPhysicsStructuralChangeTracker(
            int initialBodyCapacity,
            int initialChunkCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateInitialCapacity(initialBodyCapacity, nameof(initialBodyCapacity));
            ValidateInitialCapacity(initialChunkCapacity, nameof(initialChunkCapacity));

            _dirtyBodyKeys = new NativeList<VoxelPhysicsBodyStructuralChangeKey>(
                initialBodyCapacity,
                allocator);
            _bodyFlagsByKey = new NativeHashMap<VoxelPhysicsBodyStructuralChangeKey, VoxelPhysicsBodyStructuralChangeFlags>(
                initialBodyCapacity,
                allocator);
            _dirtyChunkKeys = new NativeList<VoxelPhysicsChunkStructuralChangeKey>(
                initialChunkCapacity,
                allocator);
            _chunkFlagsByKey = new NativeHashMap<VoxelPhysicsChunkStructuralChangeKey, VoxelPhysicsChunkStructuralChangeFlags>(
                initialChunkCapacity,
                allocator);
        }

        public bool IsCreated =>
            !_isDisposed &&
            _dirtyBodyKeys.IsCreated &&
            _bodyFlagsByKey.IsCreated &&
            _dirtyChunkKeys.IsCreated &&
            _chunkFlagsByKey.IsCreated;

        public int DirtyBodyCount
        {
            get
            {
                ThrowIfDisposed();
                return _dirtyBodyKeys.Length;
            }
        }

        public int DirtyChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return _dirtyChunkKeys.Length;
            }
        }

        public NativeArray<VoxelPhysicsBodyStructuralChangeKey> DirtyBodyKeys
        {
            get
            {
                ThrowIfDisposed();
                return _dirtyBodyKeys.AsArray();
            }
        }

        public NativeArray<VoxelPhysicsChunkStructuralChangeKey> DirtyChunkKeys
        {
            get
            {
                ThrowIfDisposed();
                return _dirtyChunkKeys.AsArray();
            }
        }

        public void MarkBodyCreated(VoxelPhysicsBodyHandle bodyHandle)
        {
            MarkBody(bodyHandle, VoxelPhysicsBodyStructuralChangeFlags.Created);
        }

        public void MarkBodyDestroyed(VoxelPhysicsBodyHandle bodyHandle)
        {
            MarkBody(bodyHandle, VoxelPhysicsBodyStructuralChangeFlags.Destroyed);
        }

        public void MarkChunkAdded(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            MarkBody(bodyHandle, VoxelPhysicsBodyStructuralChangeFlags.Chunks);
            MarkChunk(bodyHandle, chunkPosition, VoxelPhysicsChunkStructuralChangeFlags.Added);
        }

        public void MarkChunkRemoved(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            MarkBody(bodyHandle, VoxelPhysicsBodyStructuralChangeFlags.Chunks);
            MarkChunk(bodyHandle, chunkPosition, VoxelPhysicsChunkStructuralChangeFlags.Removed);
        }

        public void MarkChunkBoundsDirty(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition)
        {
            MarkBody(bodyHandle, VoxelPhysicsBodyStructuralChangeFlags.Chunks);
            MarkChunk(bodyHandle, chunkPosition, VoxelPhysicsChunkStructuralChangeFlags.Bounds);
        }

        public bool TryGetBodyFlags(
            VoxelPhysicsBodyHandle bodyHandle,
            out VoxelPhysicsBodyStructuralChangeFlags flags)
        {
            return TryGetBodyFlags(
                new VoxelPhysicsBodyStructuralChangeKey(bodyHandle),
                out flags);
        }

        public bool TryGetBodyFlags(
            VoxelPhysicsBodyStructuralChangeKey key,
            out VoxelPhysicsBodyStructuralChangeFlags flags)
        {
            ThrowIfDisposed();
            return _bodyFlagsByKey.TryGetValue(key, out flags);
        }

        public bool TryGetChunkFlags(
            VoxelPhysicsChunkStructuralChangeKey key,
            out VoxelPhysicsChunkStructuralChangeFlags flags)
        {
            ThrowIfDisposed();
            return _chunkFlagsByKey.TryGetValue(key, out flags);
        }

        public void Clear()
        {
            ThrowIfDisposed();
            _dirtyBodyKeys.Clear();
            _bodyFlagsByKey.Clear();
            _dirtyChunkKeys.Clear();
            _chunkFlagsByKey.Clear();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_dirtyBodyKeys.IsCreated)
            {
                _dirtyBodyKeys.Dispose();
            }

            if (_bodyFlagsByKey.IsCreated)
            {
                _bodyFlagsByKey.Dispose();
            }

            if (_dirtyChunkKeys.IsCreated)
            {
                _dirtyChunkKeys.Dispose();
            }

            if (_chunkFlagsByKey.IsCreated)
            {
                _chunkFlagsByKey.Dispose();
            }

            _isDisposed = true;
        }

        private void MarkBody(
            VoxelPhysicsBodyHandle bodyHandle,
            VoxelPhysicsBodyStructuralChangeFlags flags)
        {
            ThrowIfDisposed();

            VoxelPhysicsBodyStructuralChangeKey key =
                new VoxelPhysicsBodyStructuralChangeKey(bodyHandle);

            if (!_bodyFlagsByKey.TryGetValue(key, out VoxelPhysicsBodyStructuralChangeFlags existingFlags))
            {
                _dirtyBodyKeys.Add(key);
                existingFlags = VoxelPhysicsBodyStructuralChangeFlags.None;
            }

            _bodyFlagsByKey[key] = existingFlags | flags;
        }

        private void MarkChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            VoxelPhysicsChunkStructuralChangeFlags flags)
        {
            VoxelPhysicsChunkStructuralChangeKey key =
                new VoxelPhysicsChunkStructuralChangeKey(bodyHandle, chunkPosition);

            if (!_chunkFlagsByKey.TryGetValue(key, out VoxelPhysicsChunkStructuralChangeFlags existingFlags))
            {
                _dirtyChunkKeys.Add(key);
                existingFlags = VoxelPhysicsChunkStructuralChangeFlags.None;
            }

            _chunkFlagsByKey[key] = existingFlags | flags;
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        private static void ValidateInitialCapacity(int initialCapacity, string parameterName)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Initial capacity must be greater than zero.");
            }
        }
    }
}
