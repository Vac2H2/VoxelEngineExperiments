using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public readonly struct VoxelPhysicsBackendConfig
    {
        public VoxelPhysicsBackendConfig(
            int initialBodyCapacity,
            int maxChunkCount,
            AllocatorManager.AllocatorHandle allocator)
            : this(
                initialBodyCapacity,
                maxChunkCount,
                VoxelEngineConstants.BytesPerChunk,
                VoxelEngineConstants.DefaultInitialChunksPerPhysicsBodyCapacity,
                allocator)
        {
        }

        public VoxelPhysicsBackendConfig(
            int initialBodyCapacity,
            int maxChunkCount,
            int chunkSize,
            int initialChunksPerBodyCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            InitialBodyCapacity = initialBodyCapacity;
            MaxChunkCount = maxChunkCount;
            ChunkSize = chunkSize;
            InitialChunksPerBodyCapacity = initialChunksPerBodyCapacity;
            Allocator = allocator;
        }

        public int InitialBodyCapacity { get; }
        public int MaxChunkCount { get; }
        public int ChunkSize { get; }
        public int InitialChunksPerBodyCapacity { get; }
        public AllocatorManager.AllocatorHandle Allocator { get; }
    }

    public sealed class VoxelPhysicsBackend :
        IVoxelPhysicsBackendRegistration,
        IDisposable
    {
        private readonly VoxelPhysicsBodyDatabase _bodyDatabase;
        private readonly VoxelPhysicsBodyStateDatabase _bodyStateDatabase;
        private readonly VoxelPhysicsStructuralChangeTracker _structuralChangeTracker;
        private readonly VoxelPhysicsStructuralMutator _structuralMutator;
        private readonly VoxelPhysicsChunkDataMutator _chunkDataMutator;
        private bool _isDisposed;

        public VoxelPhysicsBackend(VoxelPhysicsBackendConfig config)
        {
            _bodyDatabase = new VoxelPhysicsBodyDatabase(
                config.InitialBodyCapacity,
                config.MaxChunkCount,
                config.ChunkSize,
                config.InitialChunksPerBodyCapacity,
                config.Allocator);

            _bodyStateDatabase = new VoxelPhysicsBodyStateDatabase(
                config.InitialBodyCapacity,
                config.Allocator);

            _structuralChangeTracker = new VoxelPhysicsStructuralChangeTracker(
                config.InitialBodyCapacity,
                config.MaxChunkCount,
                config.Allocator);

            _structuralMutator = new VoxelPhysicsStructuralMutator(
                _bodyDatabase,
                _structuralChangeTracker);

            _chunkDataMutator = new VoxelPhysicsChunkDataMutator(
                _bodyDatabase,
                _structuralChangeTracker);
        }

        public bool IsCreated =>
            !_isDisposed &&
            _bodyDatabase.IsCreated &&
            _bodyStateDatabase.IsCreated &&
            _structuralChangeTracker.IsCreated;

        public IVoxelPhysicsBackendRegistration Registration => this;

        public VoxelPhysicsBodyDatabase BodyDatabase
        {
            get
            {
                ThrowIfDisposed();
                return _bodyDatabase;
            }
        }

        public VoxelPhysicsStructuralChangeTracker StructuralChangeTracker
        {
            get
            {
                ThrowIfDisposed();
                return _structuralChangeTracker;
            }
        }

        public VoxelPhysicsBodyStateDatabase BodyStateDatabase
        {
            get
            {
                ThrowIfDisposed();
                return _bodyStateDatabase;
            }
        }

        public VoxelPhysicsStructuralMutator StructuralMutator
        {
            get
            {
                ThrowIfDisposed();
                return _structuralMutator;
            }
        }

        public VoxelPhysicsChunkDataMutator ChunkDataMutator
        {
            get
            {
                ThrowIfDisposed();
                return _chunkDataMutator;
            }
        }

        public VoxelPhysicsBodyChunkDataReader AsChunkDataReader()
        {
            ThrowIfDisposed();
            return _bodyDatabase.AsChunkDataReader();
        }

        public bool TryCreateBody(
            in VoxelPhysicsBodyConfig config,
            out VoxelPhysicsBodyHandle bodyHandle)
        {
            if (!IsCreated)
            {
                bodyHandle = default;
                return false;
            }

            VoxelPhysicsBody body = _structuralMutator.RequestBody();
            bodyHandle = body.Handle;
            if (_bodyStateDatabase.RegisterBody(bodyHandle, config))
            {
                return true;
            }

            _structuralMutator.TryDestroyBody(bodyHandle);
            bodyHandle = default;
            return false;
        }

        public bool TryDestroyBody(VoxelPhysicsBodyHandle bodyHandle)
        {
            if (!IsCreated ||
                !_bodyDatabase.ContainsBody(bodyHandle) ||
                !_bodyStateDatabase.ContainsBody(bodyHandle))
            {
                return false;
            }

            if (!_structuralMutator.TryDestroyBody(bodyHandle))
            {
                return false;
            }

            return _bodyStateDatabase.DestroyBody(bodyHandle);
        }

        public bool TryAddChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition)
        {
            return IsCreated &&
                   _structuralMutator.TryAddChunk(bodyHandle, chunkPosition);
        }

        public bool TryRemoveChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition)
        {
            return IsCreated &&
                   _structuralMutator.TryRemoveChunk(bodyHandle, chunkPosition);
        }

        public bool ContainsBody(VoxelPhysicsBodyHandle bodyHandle)
        {
            return IsCreated &&
                   _bodyDatabase.ContainsBody(bodyHandle) &&
                   _bodyStateDatabase.ContainsBody(bodyHandle);
        }

        public bool ContainsChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition)
        {
            return IsCreated &&
                   _bodyDatabase.ContainsChunk(bodyHandle, chunkPosition);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _structuralChangeTracker.Dispose();
            _bodyStateDatabase.Dispose();
            _bodyDatabase.Dispose();
            _isDisposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(nameof(VoxelPhysicsBackend));
            }
        }
    }
}
