using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    public sealed class ShapeDataContainer : IDisposable
    {
        public const int ChunkSize = 8;
        public const int ChunksPerShape = 8;
        public const int BitPlaneBytesPerChunk = ChunkSize * ChunkSize;
        public const int BitPlaneBytesPerShape = ChunksPerShape * BitPlaneBytesPerChunk;

        private const string ContainerName = "ShapeDataContainer";

        private NativeArray<byte> _isOccupied;
        private NativeArray<byte> _isFace;
        private NativeArray<byte> _isEdge;
        private NativeArray<byte> _isCorner;
        private NativeArray<int3> _chunkPositions;
        private NativeArray<byte> _chunkUsed;
        private bool _isDisposed;

        public ShapeDataContainer(
            int shapeCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateShapeCapacity(shapeCapacity);

            int bitPlaneLength = checked(shapeCapacity * BitPlaneBytesPerShape);
            _isOccupied = CreateBitPlane(bitPlaneLength, allocator);
            _isFace = CreateBitPlane(bitPlaneLength, allocator);
            _isEdge = CreateBitPlane(bitPlaneLength, allocator);
            _isCorner = CreateBitPlane(bitPlaneLength, allocator);

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
            _isOccupied.IsCreated &&
            _isFace.IsCreated &&
            _isEdge.IsCreated &&
            _isCorner.IsCreated &&
            _chunkPositions.IsCreated &&
            _chunkUsed.IsCreated;

        public int ShapeCapacity
        {
            get
            {
                ThrowIfDisposed();
                return _isOccupied.Length / BitPlaneBytesPerShape;
            }
        }

        public int BitPlaneLength
        {
            get
            {
                ThrowIfDisposed();
                return _isOccupied.Length;
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

        public ShapeDataView GetView(NativeArray<ShapeMetadata> shapes)
        {
            ThrowIfDisposed();

            return new ShapeDataView(
                _isOccupied,
                _isFace,
                _isEdge,
                _isCorner,
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

        private static NativeArray<byte> CreateBitPlane(
            int bitPlaneLength,
            AllocatorManager.AllocatorHandle allocator)
        {
            return CollectionHelper.CreateNativeArray<byte>(
                bitPlaneLength,
                allocator,
                NativeArrayOptions.ClearMemory);
        }

        private static void ValidateShapeCapacity(int shapeCapacity)
        {
            if (shapeCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shapeCapacity),
                    "Shape capacity must be greater than zero.");
            }
        }

        #endregion
    }
}
