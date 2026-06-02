using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    public sealed class ShapeDataStorage : IDisposable
    {
        public const int InvalidHandle = -1;

        private const string ContainerName = "ShapeDataStorage";

        private NativeArray<int> _freeShapeHandles;
        private NativeArray<ShapeMetadata> _shapes;
        private ShapeDataContainer _shapeDataContainer;
        private int _freeShapeHandleCount;
        private bool _isDisposed;

        public ShapeDataStorage(
            int shapeCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateShapeCapacity(shapeCapacity);

            _freeShapeHandles = CollectionHelper.CreateNativeArray<int>(
                shapeCapacity,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            _shapes = CollectionHelper.CreateNativeArray<ShapeMetadata>(
                shapeCapacity,
                allocator,
                NativeArrayOptions.ClearMemory);
            _shapeDataContainer = new ShapeDataContainer(shapeCapacity, allocator);
            _freeShapeHandleCount = shapeCapacity;

            for (int shapeHandle = 0; shapeHandle < shapeCapacity; shapeHandle++)
            {
                _freeShapeHandles[shapeHandle] = shapeCapacity - 1 - shapeHandle;
            }
        }

        public bool IsCreated =>
            !_isDisposed &&
            _freeShapeHandles.IsCreated &&
            _shapes.IsCreated &&
            _shapeDataContainer != null &&
            _shapeDataContainer.IsCreated;

        public int ShapeSlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _shapeDataContainer.ShapeCapacity;
            }
        }

        public int FreeShapeSlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _freeShapeHandleCount;
            }
        }

        public int Acquire(int bodyHandle)
        {
            ThrowIfDisposed();

            if (_freeShapeHandleCount == 0)
            {
                return InvalidHandle;
            }

            int shapeHandle = AcquireReleasedHandle();
            if (!IsValidShapeHandle(shapeHandle) || _shapes[shapeHandle].Used)
            {
                throw new InvalidOperationException(
                    "Shape metadata failed to mark an acquired handle used.");
            }

            _shapes[shapeHandle] = new ShapeMetadata
            {
                BodyHandle = bodyHandle,
                IsUsed = 1
            };
            return shapeHandle;
        }

        public bool Release(int shapeHandle)
        {
            ThrowIfDisposed();

            if (!IsUsedShapeHandle(shapeHandle) ||
                _freeShapeHandleCount >= _freeShapeHandles.Length)
            {
                return false;
            }

            ShapeDataView dataView = _shapeDataContainer.GetView(_shapes);
            ClearShapeChunks(shapeHandle, dataView);
            _shapes[shapeHandle] = default;

            _freeShapeHandles[_freeShapeHandleCount] = shapeHandle;
            _freeShapeHandleCount++;
            return true;
        }

        public ShapeDataView GetShapeDataView(int shapeHandle)
        {
            ThrowIfDisposed();
            ValidateShapeHandle(shapeHandle);
            return _shapeDataContainer.GetView(_shapes);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_freeShapeHandles.IsCreated)
            {
                _freeShapeHandles.Dispose();
            }

            if (_shapes.IsCreated)
            {
                _shapes.Dispose();
            }

            if (_shapeDataContainer != null)
            {
                _shapeDataContainer.Dispose();
            }

            _freeShapeHandleCount = 0;
            _isDisposed = true;
        }


        #region Helpers

        private int AcquireReleasedHandle()
        {
            _freeShapeHandleCount--;
            return _freeShapeHandles[_freeShapeHandleCount];
        }

        private bool IsValidShapeHandle(int shapeHandle)
        {
            return (uint)shapeHandle < (uint)_freeShapeHandles.Length;
        }

        private bool IsUsedShapeHandle(int shapeHandle)
        {
            return IsValidShapeHandle(shapeHandle) &&
                   _shapes[shapeHandle].Used;
        }

        private void ClearShapeChunks(int shapeHandle, ShapeDataView dataView)
        {
            NativeArray<int3> chunkPositions = dataView.ChunkPositions;
            NativeArray<byte> chunkUsed = dataView.ChunkUsed;
            int chunkStart = shapeHandle * ShapeDataContainer.ChunksPerShape;
            for (int i = 0; i < ShapeDataContainer.ChunksPerShape; i++)
            {
                int chunkSlotIndex = chunkStart + i;
                chunkPositions[chunkSlotIndex] = default;
                chunkUsed[chunkSlotIndex] = 0;
            }
        }

        private void ValidateShapeHandle(int shapeHandle)
        {
            if (!IsUsedShapeHandle(shapeHandle))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shapeHandle),
                    "Shape handle must be used and inside the fixed shape capacity.");
            }
        }

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
                    "Shape capacity must be greater than zero.");
            }
        }

        #endregion
    }
}
