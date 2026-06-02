using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.ShapeManagement
{
    public sealed class ChunkDataContainer : IDisposable
    {
        private const string ContainerName = "ShapeManagement.ChunkDataContainer";

        private NativeArray<byte> _isOccupied;
        private NativeArray<byte> _isFace;
        private NativeArray<byte> _isEdge;
        private NativeArray<byte> _isCorner;
        private NativeArray<int3> _positions;
        private NativeArray<byte> _used;
        private NativeParallelHashMap<int3, int> _indexByPosition;
        private bool _isDisposed;

        public ChunkDataContainer(int chunkCapacity)
        {
            ShapeDataContainer.ValidateChunkCapacity(chunkCapacity);

            _isOccupied = ShapeDataContainer.CreateChunkBitPlane(chunkCapacity);
            _isFace = ShapeDataContainer.CreateChunkBitPlane(chunkCapacity);
            _isEdge = ShapeDataContainer.CreateChunkBitPlane(chunkCapacity);
            _isCorner = ShapeDataContainer.CreateChunkBitPlane(chunkCapacity);
            _positions = CollectionHelper.CreateNativeArray<int3>(
                chunkCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _used = CollectionHelper.CreateNativeArray<byte>(
                chunkCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _indexByPosition = new NativeParallelHashMap<int3, int>(
                chunkCapacity,
                Allocator.Persistent);
        }

        public bool IsCreated =>
            !_isDisposed &&
            _isOccupied.IsCreated &&
            _isFace.IsCreated &&
            _isEdge.IsCreated &&
            _isCorner.IsCreated &&
            _positions.IsCreated &&
            _used.IsCreated &&
            _indexByPosition.IsCreated;

        public int BitPlaneLength
        {
            get
            {
                ThrowIfDisposed();
                return _isOccupied.Length;
            }
        }

        public int Capacity
        {
            get
            {
                ThrowIfDisposed();
                return _used.Length;
            }
        }

        public NativeArray<byte> IsOccupied
        {
            get
            {
                ThrowIfDisposed();
                return _isOccupied;
            }
        }

        public NativeArray<byte> IsFace
        {
            get
            {
                ThrowIfDisposed();
                return _isFace;
            }
        }

        public NativeArray<byte> IsEdge
        {
            get
            {
                ThrowIfDisposed();
                return _isEdge;
            }
        }

        public NativeArray<byte> IsCorner
        {
            get
            {
                ThrowIfDisposed();
                return _isCorner;
            }
        }

        public NativeArray<int3> Positions
        {
            get
            {
                ThrowIfDisposed();
                return _positions;
            }
        }

        public NativeArray<byte> Used
        {
            get
            {
                ThrowIfDisposed();
                return _used;
            }
        }

        public NativeParallelHashMap<int3, int> IndexByPosition
        {
            get
            {
                ThrowIfDisposed();
                return _indexByPosition;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isOccupied.IsCreated)
            {
                _isOccupied.Dispose();
            }

            if (_isFace.IsCreated)
            {
                _isFace.Dispose();
            }

            if (_isEdge.IsCreated)
            {
                _isEdge.Dispose();
            }

            if (_isCorner.IsCreated)
            {
                _isCorner.Dispose();
            }

            if (_positions.IsCreated)
            {
                _positions.Dispose();
            }

            if (_used.IsCreated)
            {
                _used.Dispose();
            }

            if (_indexByPosition.IsCreated)
            {
                _indexByPosition.Dispose();
            }

            _isDisposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }
    }
}
