using System;
using Unity.Collections;

namespace VoxelEngineDOTS.Physics
{
    public sealed class ChunkMap<TKey>
        where TKey : unmanaged, IEquatable<TKey>
    {
        public const int InvalidChunkId = -1;

        private const string ContainerName = "ChunkMap";

        private NativeHashMap<TKey, int> _chunkIdsByKey;

        public ChunkMap(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
        {
            ValidateInitialCapacity(initialCapacity);
            _chunkIdsByKey = new NativeHashMap<TKey, int>(initialCapacity, allocator);
        }

        public bool IsCreated => _chunkIdsByKey.IsCreated;

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return _chunkIdsByKey.Count;
            }
        }

        public int Capacity
        {
            get
            {
                ThrowIfDisposed();
                return _chunkIdsByKey.Capacity;
            }
        }

        public Reader AsReader()
        {
            ThrowIfDisposed();
            return new Reader(_chunkIdsByKey.AsReadOnly());
        }

        public bool TryAdd(TKey key, int chunkId)
        {
            ThrowIfDisposed();
            ValidateChunkId(chunkId);
            return _chunkIdsByKey.TryAdd(key, chunkId);
        }

        public bool Remove(TKey key)
        {
            ThrowIfDisposed();
            return _chunkIdsByKey.Remove(key);
        }

        public bool Remove(TKey key, out int chunkId)
        {
            ThrowIfDisposed();

            if (!_chunkIdsByKey.TryGetValue(key, out chunkId))
            {
                chunkId = InvalidChunkId;
                return false;
            }

            _chunkIdsByKey.Remove(key);
            return true;
        }

        public bool ContainsKey(TKey key)
        {
            ThrowIfDisposed();
            return _chunkIdsByKey.ContainsKey(key);
        }

        public bool TryGetChunkId(TKey key, out int chunkId)
        {
            ThrowIfDisposed();
            return _chunkIdsByKey.TryGetValue(key, out chunkId);
        }

        public void Clear()
        {
            ThrowIfDisposed();
            _chunkIdsByKey.Clear();
        }

        public void Dispose()
        {
            if (_chunkIdsByKey.IsCreated)
            {
                _chunkIdsByKey.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        private static void ValidateInitialCapacity(int initialCapacity)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    "Initial capacity must be greater than zero.");
            }
        }

        private static void ValidateChunkId(int chunkId)
        {
            if (chunkId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkId), "Chunk id must be non-negative.");
            }
        }

        public readonly struct Reader
        {
            private readonly NativeHashMap<TKey, int>.ReadOnly _chunkIdsByKey;

            internal Reader(NativeHashMap<TKey, int>.ReadOnly chunkIdsByKey)
            {
                _chunkIdsByKey = chunkIdsByKey;
            }

            public bool IsCreated => _chunkIdsByKey.IsCreated;

            public int Count => IsCreated ? _chunkIdsByKey.Count : 0;

            public bool ContainsKey(TKey key)
            {
                return IsCreated && _chunkIdsByKey.ContainsKey(key);
            }

            public bool TryGetChunkId(TKey key, out int chunkId)
            {
                if (!IsCreated)
                {
                    chunkId = InvalidChunkId;
                    return false;
                }

                return _chunkIdsByKey.TryGetValue(key, out chunkId);
            }
        }
    }
}
