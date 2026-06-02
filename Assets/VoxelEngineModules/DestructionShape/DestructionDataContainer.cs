using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.DestructionShape
{
    public sealed class DestructionDataContainer : IDisposable
    {
        public const int ChunkSize = 8;
        public const int ChunksPerShape = 8;
        public const int MaskBytesPerChunk = ChunkSize * ChunkSize;
        public const int MaskBytesPerShape = ChunksPerShape * MaskBytesPerChunk;

        private const string ContainerName = "DestructionDataContainer";

        private NativeArray<byte> _destructionMasks;
        private NativeArray<int3> _chunkPositions;
        private NativeArray<byte> _chunkUsed;
        private bool _isDisposed;

        public DestructionDataContainer(
            int shapeCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateShapeCapacity(shapeCapacity);

            int maskLength = checked(shapeCapacity * MaskBytesPerShape);
            _destructionMasks = CollectionHelper.CreateNativeArray<byte>(
                maskLength,
                allocator,
                NativeArrayOptions.ClearMemory);

            int chunkCapacity = checked(shapeCapacity * ChunksPerShape);
            _chunkPositions = CollectionHelper.CreateNativeArray<int3>(
                chunkCapacity,
                allocator,
                NativeArrayOptions.ClearMemory);
            _chunkUsed = CollectionHelper.CreateNativeArray<byte>(
                chunkCapacity,
                allocator,
                NativeArrayOptions.ClearMemory);
        }

        public bool IsCreated =>
            !_isDisposed &&
            _destructionMasks.IsCreated &&
            _chunkPositions.IsCreated &&
            _chunkUsed.IsCreated;

        public int ShapeCapacity
        {
            get
            {
                ThrowIfDisposed();
                return _destructionMasks.Length / MaskBytesPerShape;
            }
        }

        public int MaskLength
        {
            get
            {
                ThrowIfDisposed();
                return _destructionMasks.Length;
            }
        }

        public int ChunkCapacity
        {
            get
            {
                ThrowIfDisposed();
                return _chunkUsed.Length;
            }
        }

        public DestructionShapeDataView GetView(NativeArray<DestructionShapeMetadata> shapes)
        {
            ThrowIfDisposed();

            return new DestructionShapeDataView(
                _destructionMasks,
                _chunkPositions,
                _chunkUsed,
                shapes);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_destructionMasks.IsCreated)
            {
                _destructionMasks.Dispose();
            }

            if (_chunkPositions.IsCreated)
            {
                _chunkPositions.Dispose();
            }

            if (_chunkUsed.IsCreated)
            {
                _chunkUsed.Dispose();
            }

            _isDisposed = true;
        }


        #region Helpers

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(ContainerName);
            }
        }

        private static void ValidateShapeCapacity(int shapeCapacity)
        {
            if (shapeCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shapeCapacity),
                    "Destruction shape capacity must be greater than zero.");
            }
        }

        #endregion
    }
}
