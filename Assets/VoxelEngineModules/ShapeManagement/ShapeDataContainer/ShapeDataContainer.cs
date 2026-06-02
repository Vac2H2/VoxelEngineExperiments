using System;
using Unity.Collections;

namespace VoxelEngineModules.ShapeManagement
{
    public sealed class ShapeDataContainer : IDisposable
    {
        public const int ChunkSize = 8;
        public const int BitPlaneBytesPerChunk = ChunkSize * ChunkSize;

        private const string ContainerName = "ShapeManagement.ShapeDataContainer";

        private readonly ChunkDataContainer _chunks;
        private readonly FragmentDataContainer _fragments;
        private readonly int _chunkCapacity;
        private bool _isDisposed;

        public ShapeDataContainer(int chunkCapacity)
        {
            ValidateChunkCapacity(chunkCapacity);

            _chunks = new ChunkDataContainer(chunkCapacity);
            _fragments = new FragmentDataContainer(chunkCapacity);
            _chunkCapacity = chunkCapacity;
        }

        public bool IsCreated =>
            !_isDisposed &&
            _chunks != null &&
            _chunks.IsCreated &&
            _fragments != null &&
            _fragments.IsCreated;

        public int ChunkCapacity
        {
            get
            {
                ThrowIfDisposed();
                return _chunkCapacity;
            }
        }

        public ChunkDataContainer Chunks
        {
            get
            {
                ThrowIfDisposed();
                return _chunks;
            }
        }

        public FragmentDataContainer Fragments
        {
            get
            {
                ThrowIfDisposed();
                return _fragments;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_chunks != null)
            {
                _chunks.Dispose();
            }

            if (_fragments != null)
            {
                _fragments.Dispose();
            }

            _isDisposed = true;
        }

        internal static NativeArray<byte> CreateChunkBitPlane(
            int chunkCapacity)
        {
            return CollectionHelper.CreateNativeArray<byte>(
                checked(chunkCapacity * BitPlaneBytesPerChunk),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        internal static void ValidateChunkCapacity(int chunkCapacity)
        {
            if (chunkCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkCapacity),
                    "Chunk capacity must be greater than zero.");
            }
        }
    }
}
