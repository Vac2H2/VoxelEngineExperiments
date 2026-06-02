using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngineDOTS.Physics;

namespace VoxelEngineDOTS.Physics.Backend
{
    public struct VoxelPhysicsBody
    {
        public VoxelPhysicsBody(VoxelPhysicsBodyHandle handle, bool isAlive)
        {
            Handle = handle;
            IsAlive = isAlive;
        }

        public VoxelPhysicsBodyHandle Handle;
        public bool IsAlive;
    }

    public readonly struct VoxelPhysicsChunk
    {
        public VoxelPhysicsChunk(int3 chunkPosition, int chunkDataId)
        {
            ChunkPosition = chunkPosition;
            ChunkDataId = chunkDataId;
        }

        public int3 ChunkPosition { get; }
        public int ChunkDataId { get; }
    }

    public sealed class VoxelPhysicsBodyDatabase : IDisposable
    {
        private const string ContainerName = "VoxelPhysicsBodyDatabase";
        private const int DefaultInitialChunksPerBodyCapacity = 4;

        private readonly AllocatorManager.AllocatorHandle _allocator;
        private readonly ChunkDataContainer<byte> _chunkDataContainer;
        private readonly int _maxChunkCount;
        private NativeList<VoxelPhysicsBody> _bodies;
        private readonly List<NativeList<VoxelPhysicsChunk>> _bodyChunks;
        private readonly List<NativeHashMap<int3, int>> _bodyChunkMaps;
        private NativeHashMap<VoxelPhysicsBodyChunkKey, int> _chunkDataIdsByBodyChunk;
        private NativeList<int> _freeBodyIds;
        private readonly int _initialChunksPerBodyCapacity;

        private int _bodyCount;
        private bool _isDisposed;

        public VoxelPhysicsBodyDatabase(
            int initialBodyCapacity,
            int maxChunkCount,
            int chunkSize,
            AllocatorManager.AllocatorHandle allocator)
            : this(
                initialBodyCapacity,
                maxChunkCount,
                chunkSize,
                DefaultInitialChunksPerBodyCapacity,
                allocator)
        {
        }

        public VoxelPhysicsBodyDatabase(
            int initialBodyCapacity,
            int maxChunkCount,
            int chunkSize,
            int initialChunksPerBodyCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateInitialBodyCapacity(initialBodyCapacity);
            ValidateMaxChunkCount(maxChunkCount);
            ValidateInitialChunksPerBodyCapacity(initialChunksPerBodyCapacity);

            _allocator = allocator;
            _maxChunkCount = maxChunkCount;
            _chunkDataContainer = new ChunkDataContainer<byte>(
                chunkSize,
                maxChunkCount,
                allocator);

            _bodies = new NativeList<VoxelPhysicsBody>(initialBodyCapacity, allocator);
            _bodyChunks = new List<NativeList<VoxelPhysicsChunk>>(initialBodyCapacity);
            _bodyChunkMaps = new List<NativeHashMap<int3, int>>(initialBodyCapacity);
            _chunkDataIdsByBodyChunk =
                new NativeHashMap<VoxelPhysicsBodyChunkKey, int>(
                    maxChunkCount,
                    allocator);
            _freeBodyIds = new NativeList<int>(initialBodyCapacity, allocator);
            _initialChunksPerBodyCapacity = initialChunksPerBodyCapacity;
        }

        public bool IsCreated =>
            !_isDisposed &&
            _chunkDataContainer.IsCreated &&
            _bodies.IsCreated &&
            _chunkDataIdsByBodyChunk.IsCreated;

        public int BodyCount
        {
            get
            {
                ThrowIfDisposed();
                return _bodyCount;
            }
        }

        public int BodyCapacity
        {
            get
            {
                ThrowIfDisposed();
                return _bodies.Capacity;
            }
        }

        public int BodySlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _bodies.Length;
            }
        }

        public int MaxChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return _maxChunkCount;
            }
        }

        public int AllocatedChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return _chunkDataContainer.AllocatedChunkCount;
            }
        }

        internal ChunkDataContainer<byte> ChunkDataContainer
        {
            get
            {
                ThrowIfDisposed();
                return _chunkDataContainer;
            }
        }

        public VoxelPhysicsBodyChunkDataReader AsChunkDataReader()
        {
            ThrowIfDisposed();
            return new VoxelPhysicsBodyChunkDataReader(
                _chunkDataIdsByBodyChunk.AsReadOnly(),
                _chunkDataContainer.AsReader());
        }

        internal VoxelPhysicsBody RequestBody()
        {
            ThrowIfDisposed();

            int bodyId;
            int version;
            if (_freeBodyIds.Length > 0)
            {
                int freeListIndex = _freeBodyIds.Length - 1;
                bodyId = _freeBodyIds[freeListIndex];
                _freeBodyIds.RemoveAt(freeListIndex);

                VoxelPhysicsBody oldBody = _bodies[bodyId];
                version = oldBody.Handle.Version;
            }
            else
            {
                bodyId = _bodies.Length;
                version = 1;

                _bodies.Add(default);
                _bodyChunks.Add(new NativeList<VoxelPhysicsChunk>(
                    _initialChunksPerBodyCapacity,
                    _allocator));
                _bodyChunkMaps.Add(new NativeHashMap<int3, int>(
                    _initialChunksPerBodyCapacity,
                    _allocator));
            }

            _bodyChunks[bodyId].Clear();
            _bodyChunkMaps[bodyId].Clear();

            VoxelPhysicsBody body = new VoxelPhysicsBody(
                new VoxelPhysicsBodyHandle(bodyId, version),
                true);

            _bodies[bodyId] = body;
            _bodyCount++;
            return body;
        }

        internal bool ContainsBody(VoxelPhysicsBodyHandle bodyHandle)
        {
            ThrowIfDisposed();
            return TryGetAliveBody(bodyHandle, out _);
        }

        internal bool ContainsChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition)
        {
            ThrowIfDisposed();

            return TryGetAliveBody(bodyHandle, out int bodyId) &&
                _bodyChunkMaps[bodyId].ContainsKey(chunkPosition);
        }

        internal bool TryGetBody(VoxelPhysicsBodyHandle bodyHandle, out VoxelPhysicsBody body)
        {
            ThrowIfDisposed();

            if (!TryGetAliveBody(bodyHandle, out int bodyId))
            {
                body = default;
                return false;
            }

            body = _bodies[bodyId];
            return true;
        }

        internal bool TryGetBodyChunks(
            VoxelPhysicsBodyHandle bodyHandle,
            out NativeList<VoxelPhysicsChunk> chunks)
        {
            ThrowIfDisposed();

            if (!TryGetAliveBody(bodyHandle, out int bodyId))
            {
                chunks = default;
                return false;
            }

            chunks = _bodyChunks[bodyId];
            return true;
        }

        internal bool TryAddChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition)
        {
            return TryAddChunk(bodyHandle, chunkPosition, out _);
        }

        internal bool TryAddChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            out int chunkDataId)
        {
            ThrowIfDisposed();

            if (!TryGetAliveBody(bodyHandle, out int bodyId))
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            NativeHashMap<int3, int> chunkMap = _bodyChunkMaps[bodyId];
            if (chunkMap.ContainsKey(chunkPosition))
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            if (_chunkDataContainer.AllocatedChunkCount >= _maxChunkCount)
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            NativeList<VoxelPhysicsChunk> chunks = _bodyChunks[bodyId];
            int chunkIndex = chunks.Length;
            chunkDataId = _chunkDataContainer.RequestChunk();

            if (!chunkMap.TryAdd(chunkPosition, chunkIndex))
            {
                _chunkDataContainer.ReleaseChunk(chunkDataId);
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            VoxelPhysicsBodyChunkKey bodyChunkKey =
                new VoxelPhysicsBodyChunkKey(bodyHandle.Index, chunkPosition);
            if (!_chunkDataIdsByBodyChunk.TryAdd(bodyChunkKey, chunkDataId))
            {
                chunkMap.Remove(chunkPosition);
                _chunkDataContainer.ReleaseChunk(chunkDataId);
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            chunks.Add(new VoxelPhysicsChunk(chunkPosition, chunkDataId));
            return true;
        }

        internal bool TryUpdateChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            NativeSlice<byte> source)
        {
            ThrowIfDisposed();

            if (!TryGetChunk(bodyHandle, chunkPosition, out VoxelPhysicsChunk chunk))
            {
                return false;
            }

            return _chunkDataContainer.AsWriter().TryOverwriteChunk(
                chunk.ChunkDataId,
                source);
        }

        internal bool TryGetChunkDataId(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            out int chunkDataId)
        {
            ThrowIfDisposed();

            if (!TryGetChunk(bodyHandle, chunkPosition, out VoxelPhysicsChunk chunk))
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            chunkDataId = chunk.ChunkDataId;
            return true;
        }

        internal bool TryGetChunkSlice(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            out NativeSlice<byte> chunkData)
        {
            ThrowIfDisposed();

            if (!TryGetChunk(bodyHandle, chunkPosition, out VoxelPhysicsChunk chunk))
            {
                chunkData = default;
                return false;
            }

            return _chunkDataContainer.AsReader().TryGetChunkSlice(
                chunk.ChunkDataId,
                out chunkData);
        }

        internal bool TryRemoveChunk(VoxelPhysicsBodyHandle bodyHandle, int3 chunkPosition)
        {
            return TryRemoveChunk(bodyHandle, chunkPosition, out _);
        }

        internal bool TryRemoveChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            out int chunkDataId)
        {
            ThrowIfDisposed();

            if (!TryGetAliveBody(bodyHandle, out int bodyId))
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            NativeHashMap<int3, int> chunkMap = _bodyChunkMaps[bodyId];
            if (!chunkMap.TryGetValue(chunkPosition, out int chunkIndex))
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            NativeList<VoxelPhysicsChunk> chunks = _bodyChunks[bodyId];
            VoxelPhysicsChunk chunk = chunks[chunkIndex];
            chunkDataId = chunk.ChunkDataId;

            if (!_chunkDataContainer.ReleaseChunk(chunkDataId))
            {
                chunkDataId = ChunkDataContainer<byte>.InvalidChunkId;
                return false;
            }

            chunkMap.Remove(chunkPosition);
            _chunkDataIdsByBodyChunk.Remove(
                new VoxelPhysicsBodyChunkKey(bodyHandle.Index, chunkPosition));
            chunks.RemoveAtSwapBack(chunkIndex);

            if (chunkIndex < chunks.Length)
            {
                VoxelPhysicsChunk movedChunk = chunks[chunkIndex];
                chunkMap[movedChunk.ChunkPosition] = chunkIndex;
            }

            return true;
        }

        internal bool TryDestroyBody(VoxelPhysicsBodyHandle bodyHandle)
        {
            ThrowIfDisposed();

            if (!TryGetAliveBody(bodyHandle, out int bodyId))
            {
                return false;
            }

            NativeList<VoxelPhysicsChunk> chunks = _bodyChunks[bodyId];
            for (int i = 0; i < chunks.Length; i++)
            {
                _chunkDataIdsByBodyChunk.Remove(
                    new VoxelPhysicsBodyChunkKey(bodyId, chunks[i].ChunkPosition));
                _chunkDataContainer.ReleaseChunk(chunks[i].ChunkDataId);
            }

            chunks.Clear();
            _bodyChunkMaps[bodyId].Clear();

            VoxelPhysicsBody body = _bodies[bodyId];
            body.Handle = new VoxelPhysicsBodyHandle(
                bodyId,
                GetNextVersion(body.Handle.Version));
            body.IsAlive = false;
            _bodies[bodyId] = body;

            _freeBodyIds.Add(bodyId);
            _bodyCount--;
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_bodies.IsCreated)
            {
                _bodies.Dispose();
            }

            for (int i = 0; i < _bodyChunks.Count; i++)
            {
                NativeList<VoxelPhysicsChunk> chunks = _bodyChunks[i];
                if (chunks.IsCreated)
                {
                    chunks.Dispose();
                }
            }

            for (int i = 0; i < _bodyChunkMaps.Count; i++)
            {
                NativeHashMap<int3, int> chunkMap = _bodyChunkMaps[i];
                if (chunkMap.IsCreated)
                {
                    chunkMap.Dispose();
                }
            }

            _chunkDataContainer.Dispose();
            if (_chunkDataIdsByBodyChunk.IsCreated)
            {
                _chunkDataIdsByBodyChunk.Dispose();
            }

            _bodyChunks.Clear();
            _bodyChunkMaps.Clear();
            if (_freeBodyIds.IsCreated)
            {
                _freeBodyIds.Dispose();
            }
            _bodyCount = 0;
            _isDisposed = true;
        }

        private bool TryGetAliveBody(VoxelPhysicsBodyHandle bodyHandle, out int bodyId)
        {
            if (!bodyHandle.IsValid || (uint)bodyHandle.Index >= (uint)_bodies.Length)
            {
                bodyId = -1;
                return false;
            }

            VoxelPhysicsBody body = _bodies[bodyHandle.Index];
            if (!body.IsAlive || body.Handle.Version != bodyHandle.Version)
            {
                bodyId = -1;
                return false;
            }

            bodyId = bodyHandle.Index;
            return true;
        }

        private bool TryGetChunk(
            VoxelPhysicsBodyHandle bodyHandle,
            int3 chunkPosition,
            out VoxelPhysicsChunk chunk)
        {
            if (!TryGetAliveBody(bodyHandle, out int bodyId))
            {
                chunk = default;
                return false;
            }

            NativeHashMap<int3, int> chunkMap = _bodyChunkMaps[bodyId];
            if (!chunkMap.TryGetValue(chunkPosition, out int chunkIndex))
            {
                chunk = default;
                return false;
            }

            chunk = _bodyChunks[bodyId][chunkIndex];
            return true;
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        private static int GetNextVersion(int version)
        {
            return version == int.MaxValue ? 1 : version + 1;
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

        private static void ValidateMaxChunkCount(int maxChunkCount)
        {
            if (maxChunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxChunkCount),
                    "Max chunk count must be greater than zero.");
            }
        }

        private static void ValidateInitialChunksPerBodyCapacity(int initialChunksPerBodyCapacity)
        {
            if (initialChunksPerBodyCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialChunksPerBodyCapacity),
                    "Initial chunks per body capacity must be non-negative.");
            }
        }
    }
}
