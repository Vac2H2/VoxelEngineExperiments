using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.ShapeManagement
{
    public sealed class FragmentDataContainer : IDisposable
    {
        private const string ContainerName = "ShapeManagement.FragmentDataContainer";

        private NativeList<byte> _isOccupied;
        private NativeParallelMultiHashMap<int3, int> _indicesByChunkPosition;
        private NativeList<int> _connectionOffsets;
        private NativeList<int> _connectionTargets;
        private bool _isDisposed;

        public FragmentDataContainer(int chunkCapacity)
        {
            ShapeDataContainer.ValidateChunkCapacity(chunkCapacity);

            _isOccupied = new NativeList<byte>(
                ShapeDataContainer.BitPlaneBytesPerChunk,
                Allocator.Persistent);
            _indicesByChunkPosition =
                new NativeParallelMultiHashMap<int3, int>(
                    chunkCapacity,
                    Allocator.Persistent);
            _connectionOffsets = new NativeList<int>(
                chunkCapacity + 1,
                Allocator.Persistent);
            _connectionTargets = new NativeList<int>(
                chunkCapacity,
                Allocator.Persistent);
        }

        public bool IsCreated =>
            !_isDisposed &&
            _isOccupied.IsCreated &&
            _indicesByChunkPosition.IsCreated &&
            _connectionOffsets.IsCreated &&
            _connectionTargets.IsCreated;

        public NativeList<byte> IsOccupied
        {
            get
            {
                ThrowIfDisposed();
                return _isOccupied;
            }
        }

        public NativeParallelMultiHashMap<int3, int> IndicesByChunkPosition
        {
            get
            {
                ThrowIfDisposed();
                return _indicesByChunkPosition;
            }
        }

        public NativeList<int> ConnectionOffsets
        {
            get
            {
                ThrowIfDisposed();
                return _connectionOffsets;
            }
        }

        public NativeList<int> ConnectionTargets
        {
            get
            {
                ThrowIfDisposed();
                return _connectionTargets;
            }
        }

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return _isOccupied.Length / ShapeDataContainer.BitPlaneBytesPerChunk;
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

            if (_indicesByChunkPosition.IsCreated)
            {
                _indicesByChunkPosition.Dispose();
            }

            if (_connectionOffsets.IsCreated)
            {
                _connectionOffsets.Dispose();
            }

            if (_connectionTargets.IsCreated)
            {
                _connectionTargets.Dispose();
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
