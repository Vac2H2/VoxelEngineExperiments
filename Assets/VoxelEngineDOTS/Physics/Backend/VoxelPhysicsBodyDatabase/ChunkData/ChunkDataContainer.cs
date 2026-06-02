using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace VoxelEngineDOTS.Physics
{
    public sealed class ChunkDataContainer<T>
        where T : unmanaged
    {
        public const int InvalidChunkId = -1;

        private const string ContainerName = "ChunkDataContainer";
        private NativeList<byte> _chunkUsage;
        private NativeList<int> _freeChunkIds;
        private NativeList<T> _chunkData;
        private int _chunkSize;

        public ChunkDataContainer(
            int chunkSize,
            int chunkCount,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateChunkSize(chunkSize);
            ValidateChunkCount(chunkCount);

            _chunkUsage = new NativeList<byte>(chunkCount, allocator);
            _freeChunkIds = new NativeList<int>(chunkCount, allocator);
            _chunkData = new NativeList<T>(checked(chunkSize * chunkCount), allocator);
            _chunkSize = chunkSize;

            AppendChunkSlots(chunkCount);
        }

        public bool IsCreated =>
            _chunkUsage.IsCreated &&
            _freeChunkIds.IsCreated &&
            _chunkData.IsCreated;

        public int ChunkSize => _chunkSize;

        public int ChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return _chunkUsage.Length;
            }
        }

        public int FreeChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return _freeChunkIds.Length;
            }
        }

        public int AllocatedChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return _chunkUsage.Length - _freeChunkIds.Length;
            }
        }

        public int DataLength
        {
            get
            {
                ThrowIfDisposed();
                return _chunkData.Length;
            }
        }

        public Reader AsReader()
        {
            ThrowIfDisposed();
            return new Reader(_chunkUsage.AsReadOnly(), _chunkData.AsArray(), _chunkSize);
        }

        public Writer AsWriter()
        {
            ThrowIfDisposed();
            return new Writer(_chunkUsage.AsReadOnly(), _chunkData.AsArray(), _chunkSize);
        }

        public int RequestChunk()
        {
            ThrowIfDisposed();

            if (_freeChunkIds.Length == 0)
            {
                GrowChunkSlots();
            }

            int freeListIndex = _freeChunkIds.Length - 1;
            int chunkId = _freeChunkIds[freeListIndex];
            _freeChunkIds.RemoveAt(freeListIndex);
            _chunkUsage[chunkId] = 1;
            return chunkId;
        }

        public bool ReleaseChunk(int chunkId)
        {
            ThrowIfDisposed();

            if (!IsValidChunkId(chunkId) || _chunkUsage[chunkId] == 0)
            {
                return false;
            }

            _chunkUsage[chunkId] = 0;
            _freeChunkIds.Add(chunkId);
            return true;
        }

        public bool IsChunkAllocated(int chunkId)
        {
            ThrowIfDisposed();
            return IsValidChunkId(chunkId) && _chunkUsage[chunkId] != 0;
        }

        public void Clear()
        {
            ThrowIfDisposed();

            for (int chunkId = 0; chunkId < _chunkUsage.Length; chunkId++)
            {
                _chunkUsage[chunkId] = 0;
            }

            _freeChunkIds.Clear();
            for (int chunkId = _chunkUsage.Length - 1; chunkId >= 0; chunkId--)
            {
                _freeChunkIds.Add(chunkId);
            }
        }

        public void Dispose()
        {
            if (_chunkUsage.IsCreated)
            {
                _chunkUsage.Dispose();
            }

            if (_freeChunkIds.IsCreated)
            {
                _freeChunkIds.Dispose();
            }

            if (_chunkData.IsCreated)
            {
                _chunkData.Dispose();
            }
        }

        private void GrowChunkSlots()
        {
            AppendChunkSlots(Math.Max(1, _chunkUsage.Length));
        }

        private void AppendChunkSlots(int chunkCount)
        {
            int startChunkId = _chunkUsage.Length;
            int newChunkCount = checked(startChunkId + chunkCount);
            int newDataLength = checked(newChunkCount * _chunkSize);

            _chunkUsage.Resize(newChunkCount, NativeArrayOptions.ClearMemory);
            _chunkData.Resize(newDataLength, NativeArrayOptions.ClearMemory);

            for (int chunkId = newChunkCount - 1; chunkId >= startChunkId; chunkId--)
            {
                _freeChunkIds.Add(chunkId);
            }
        }

        private bool IsValidChunkId(int chunkId)
        {
            return (uint)chunkId < (uint)_chunkUsage.Length;
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        private static void ValidateChunkSize(int chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
            }
        }

        private static void ValidateChunkCount(int chunkCount)
        {
            if (chunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), "Chunk count must be greater than zero.");
            }
        }

        public readonly struct Reader
        {
            private readonly NativeArray<byte>.ReadOnly _chunkUsage;

            [ReadOnly]
            private readonly NativeArray<T> _chunkData;

            private readonly int _chunkSize;

            internal Reader(
                NativeArray<byte>.ReadOnly chunkUsage,
                NativeArray<T> chunkData,
                int chunkSize)
            {
                _chunkUsage = chunkUsage;
                _chunkData = chunkData;
                _chunkSize = chunkSize;
            }

            public bool IsCreated => _chunkUsage.IsCreated && _chunkData.IsCreated;

            public int ChunkSize => _chunkSize;

            public int ChunkCount
            {
                get
                {
                    return _chunkUsage.Length;
                }
            }

            public int DataLength
            {
                get
                {
                    return _chunkData.Length;
                }
            }

            public bool IsChunkAllocated(int chunkId)
            {
                return IsCreated && IsValidChunkId(chunkId) && _chunkUsage[chunkId] != 0;
            }

            public bool TryRead(int chunkId, int localIndex, out T value)
            {
                if (!IsCreated || !IsValidLocalIndex(localIndex) || !IsChunkAllocated(chunkId))
                {
                    value = default;
                    return false;
                }

                value = _chunkData[GetChunkDataBaseIndex(chunkId) + localIndex];
                return true;
            }

            public T Read(int chunkId, int localIndex)
            {
                return _chunkData[GetChunkDataBaseIndex(chunkId) + localIndex];
            }

            public bool TryGetChunkSlice(int chunkId, out NativeSlice<T> chunkData)
            {
                if (!IsChunkAllocated(chunkId))
                {
                    chunkData = default;
                    return false;
                }

                chunkData = GetChunkSlice(chunkId);
                return true;
            }

            public NativeSlice<T> GetChunkSlice(int chunkId)
            {
                return new NativeSlice<T>(_chunkData, GetChunkDataBaseIndex(chunkId), _chunkSize);
            }

            private bool IsValidChunkId(int chunkId)
            {
                return (uint)chunkId < (uint)_chunkUsage.Length;
            }

            private bool IsValidLocalIndex(int localIndex)
            {
                return (uint)localIndex < (uint)_chunkSize;
            }

            private int GetChunkDataBaseIndex(int chunkId)
            {
                return chunkId * _chunkSize;
            }
        }

        public struct Writer
        {
            private readonly NativeArray<byte>.ReadOnly _chunkUsage;

            [NativeDisableParallelForRestriction]
            private NativeArray<T> _chunkData;

            private readonly int _chunkSize;

            internal Writer(
                NativeArray<byte>.ReadOnly chunkUsage,
                NativeArray<T> chunkData,
                int chunkSize)
            {
                _chunkUsage = chunkUsage;
                _chunkData = chunkData;
                _chunkSize = chunkSize;
            }

            public readonly bool IsCreated => _chunkUsage.IsCreated && _chunkData.IsCreated;

            public readonly int ChunkSize => _chunkSize;

            public readonly int ChunkCount
            {
                get
                {
                    return _chunkUsage.Length;
                }
            }

            public readonly int DataLength
            {
                get
                {
                    return _chunkData.Length;
                }
            }

            public readonly bool IsChunkAllocated(int chunkId)
            {
                return IsCreated && IsValidChunkId(chunkId) && _chunkUsage[chunkId] != 0;
            }

            public bool TryOverwriteChunk(int chunkId, NativeSlice<T> source)
            {
                if (!IsCreated || source.Length != _chunkSize || !IsChunkAllocated(chunkId))
                {
                    return false;
                }

                OverwriteChunk(chunkId, source);
                return true;
            }

            public void OverwriteChunk(int chunkId, NativeSlice<T> source)
            {
                NativeSlice<T> target = new NativeSlice<T>(
                    _chunkData,
                    GetChunkDataBaseIndex(chunkId),
                    _chunkSize);

                target.CopyFrom(source);
            }

            public bool TryGetChunkSlice(int chunkId, out NativeSlice<T> chunkData)
            {
                if (!IsChunkAllocated(chunkId))
                {
                    chunkData = default;
                    return false;
                }

                chunkData = GetChunkSlice(chunkId);
                return true;
            }

            public NativeSlice<T> GetChunkSlice(int chunkId)
            {
                return new NativeSlice<T>(
                    _chunkData,
                    GetChunkDataBaseIndex(chunkId),
                    _chunkSize);
            }

            private readonly bool IsValidChunkId(int chunkId)
            {
                return (uint)chunkId < (uint)_chunkUsage.Length;
            }

            private readonly int GetChunkDataBaseIndex(int chunkId)
            {
                return chunkId * _chunkSize;
            }
        }
    }
}
