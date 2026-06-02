using System;

namespace VoxelEngineModules.ShapeManagement
{
    public sealed class ShapeDataStorage : IDisposable
    {
        public const int InvalidHandle = -1;

        private const string ContainerName = "ShapeManagement.ShapeDataStorage";

        private int[] _freeShapeHandles;
        private ShapeMetadata[] _shapes;
        private ShapeDataContainer[] _containers;
        private int _freeShapeHandleCount;
        private bool _containersInitialized;
        private bool _isDisposed;

        public ShapeDataStorage(int shapeCapacity)
        {
            ValidateShapeCapacity(shapeCapacity);

            _freeShapeHandles = new int[shapeCapacity];
            _shapes = new ShapeMetadata[shapeCapacity];
            _containers = new ShapeDataContainer[shapeCapacity];
            _freeShapeHandleCount = shapeCapacity;

            for (int shapeHandle = 0; shapeHandle < shapeCapacity; shapeHandle++)
            {
                _freeShapeHandles[shapeHandle] = shapeCapacity - 1 - shapeHandle;
            }

            _containersInitialized = true;
        }

        public bool IsCreated =>
            !_isDisposed &&
            _freeShapeHandles != null &&
            _shapes != null &&
            _containers != null &&
            _containersInitialized;

        public int ShapeSlotCount
        {
            get
            {
                ThrowIfDisposed();
                return _containers.Length;
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

        public ShapeMetadata[] Shapes
        {
            get
            {
                ThrowIfDisposed();
                return _shapes;
            }
        }

        public int Acquire(int bodyHandle, int chunkCapacity)
        {
            ThrowIfDisposed();
            ShapeDataContainer.ValidateChunkCapacity(chunkCapacity);

            if (_freeShapeHandleCount == 0)
            {
                return InvalidHandle;
            }

            int shapeHandle = AcquireReleasedHandle();
            if (!IsValidShapeHandle(shapeHandle) ||
                _shapes[shapeHandle].Used ||
                _containers[shapeHandle] != null)
            {
                throw new InvalidOperationException(
                    "Shape metadata failed to mark an acquired handle used.");
            }

            _containers[shapeHandle] = new ShapeDataContainer(chunkCapacity);
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

            ShapeDataContainer container = _containers[shapeHandle];
            if (container != null)
            {
                container.Dispose();
                _containers[shapeHandle] = null;
            }

            _shapes[shapeHandle] = default;

            _freeShapeHandles[_freeShapeHandleCount] = shapeHandle;
            _freeShapeHandleCount++;
            return true;
        }

        public ShapeDataContainer GetShape(int shapeHandle)
        {
            ThrowIfDisposed();
            ValidateShapeHandle(shapeHandle);
            return _containers[shapeHandle];
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_containers != null)
            {
                if (_containersInitialized)
                {
                    for (int i = 0; i < _containers.Length; i++)
                    {
                        ShapeDataContainer container = _containers[i];
                        if (container != null && container.IsCreated)
                        {
                            container.Dispose();
                        }
                    }
                }
            }

            _freeShapeHandleCount = 0;
            _freeShapeHandles = null;
            _shapes = null;
            _containers = null;
            _containersInitialized = false;
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
            return (uint)shapeHandle < (uint)_containers.Length;
        }

        private bool IsUsedShapeHandle(int shapeHandle)
        {
            return IsValidShapeHandle(shapeHandle) &&
                   _shapes[shapeHandle].Used;
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
