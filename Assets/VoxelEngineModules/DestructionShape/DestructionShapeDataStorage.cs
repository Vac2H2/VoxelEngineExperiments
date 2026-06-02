using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngineModules.DestructionShape
{
    public sealed class DestructionShapeDataStorage : IDisposable
    {
        public const int InvalidHandle = -1;

        private const string ContainerName = "DestructionShapeDataStorage";

        private NativeArray<int> _freeShapeHandles;
        private NativeArray<DestructionShapeMetadata> _shapes;
        private DestructionDataContainer _destructionDataContainer;
        private int _freeShapeHandleCount;
        private bool _isDisposed;

        public DestructionShapeDataStorage(
            int shapeCapacity,
            AllocatorManager.AllocatorHandle allocator)
        {
            ValidateShapeCapacity(shapeCapacity);

            _freeShapeHandles = CollectionHelper.CreateNativeArray<int>(
                shapeCapacity,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            _shapes = CollectionHelper.CreateNativeArray<DestructionShapeMetadata>(
                shapeCapacity,
                allocator,
                NativeArrayOptions.ClearMemory);
            _destructionDataContainer = new DestructionDataContainer(shapeCapacity, allocator);
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
            _destructionDataContainer != null &&
            _destructionDataContainer.IsCreated;

        public int ShapeSlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _destructionDataContainer.ShapeCapacity;
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

        public int Acquire()
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
                    "Destruction shape metadata failed to mark an acquired handle used.");
            }

            _shapes[shapeHandle] = new DestructionShapeMetadata
            {
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

            DestructionShapeDataView dataView = _destructionDataContainer.GetView(_shapes);
            ClearShape(shapeHandle, dataView);
            _shapes[shapeHandle] = default;

            _freeShapeHandles[_freeShapeHandleCount] = shapeHandle;
            _freeShapeHandleCount++;
            return true;
        }

        public DestructionShapeDataView GetDataView(int shapeHandle)
        {
            ThrowIfDisposed();
            ValidateShapeHandle(shapeHandle);
            return _destructionDataContainer.GetView(_shapes);
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

            if (_destructionDataContainer != null)
            {
                _destructionDataContainer.Dispose();
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

        private void ClearShape(int shapeHandle, DestructionShapeDataView dataView)
        {
            NativeArray<byte> destructionMasks = dataView.DestructionMasks;
            NativeArray<int3> chunkPositions = dataView.ChunkPositions;
            NativeArray<byte> chunkUsed = dataView.ChunkUsed;

            int maskStart = shapeHandle * DestructionDataContainer.MaskBytesPerShape;
            for (int i = 0; i < DestructionDataContainer.MaskBytesPerShape; i++)
            {
                destructionMasks[maskStart + i] = 0;
            }

            int chunkStart = shapeHandle * DestructionDataContainer.ChunksPerShape;
            for (int i = 0; i < DestructionDataContainer.ChunksPerShape; i++)
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
                    "Destruction shape handle must be used and inside the fixed shape capacity.");
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
                    "Destruction shape capacity must be greater than zero.");
            }
        }

        #endregion
    }
}
