using System;

namespace VoxelEngine.Physics.Refactor
{
    public sealed class PhysicsEntityContainer
    {
        public const int DefaultInitialCapacity = 64;

        private const int InvalidIndex = -1;

        private VoxelPhysicsEntity[] _entities;
        private int[] _denseToSlot;
        private int[] _slotToDense;
        private int[] _slotVersions;
        private int[] _nextFreeSlot;
        private int _count;
        private int _slotCount;
        private int _freeSlotHead = InvalidIndex;

        public PhysicsEntityContainer()
            : this(DefaultInitialCapacity)
        {
        }

        public PhysicsEntityContainer(int initialCapacity)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    "Initial capacity must be non-negative.");
            }

            int capacity = Math.Max(1, initialCapacity);
            _entities = new VoxelPhysicsEntity[capacity];
            _denseToSlot = CreateIndexArray(capacity);
            _slotToDense = CreateIndexArray(capacity);
            _slotVersions = new int[capacity];
            _nextFreeSlot = CreateIndexArray(capacity);
        }

        public int Count => _count;

        public int Capacity => _entities.Length;

        public int SlotCapacity => _slotToDense.Length;

        public int SlotCount => _slotCount;

        public bool IsEmpty => _count == 0;

        public ReadOnlySpan<VoxelPhysicsEntity> Entities =>
            new ReadOnlySpan<VoxelPhysicsEntity>(_entities, 0, _count);

        public VoxelPhysicsEntity this[int denseIndex] => GetEntityAt(denseIndex);

        public PhysicsEntityHandle CreateEntity()
        {
            return CreateEntity(VoxelPhysicsEntityDescriptor.Dynamic);
        }

        public PhysicsEntityHandle CreateEntity(VoxelPhysicsEntityArchetype archetype)
        {
            return CreateEntity(new VoxelPhysicsEntityDescriptor(archetype));
        }

        public PhysicsEntityHandle CreateEntity(
            in VoxelPhysicsEntityDescriptor descriptor)
        {
            VoxelPhysicsEntityDescriptor.ValidateArchetype(descriptor.Archetype);

            int slot = AllocateSlot();
            EnsureEntityCapacity(_count + 1);

            PhysicsEntityHandle handle = new PhysicsEntityHandle(
                slot,
                _slotVersions[slot]);
            int denseIndex = _count++;

            _entities[denseIndex] = new VoxelPhysicsEntity(
                handle,
                descriptor.Archetype,
                descriptor.IsEnabled);
            _denseToSlot[denseIndex] = slot;
            _slotToDense[slot] = denseIndex;

            return handle;
        }

        public bool DestroyEntity(PhysicsEntityHandle handle)
        {
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            int removedSlot = handle.Index;
            int lastDenseIndex = _count - 1;

            if (denseIndex != lastDenseIndex)
            {
                VoxelPhysicsEntity movedEntity = _entities[lastDenseIndex];
                int movedSlot = _denseToSlot[lastDenseIndex];

                _entities[denseIndex] = movedEntity;
                _denseToSlot[denseIndex] = movedSlot;
                _slotToDense[movedSlot] = denseIndex;
            }

            _entities[lastDenseIndex] = default;
            _denseToSlot[lastDenseIndex] = InvalidIndex;
            _count--;

            ReleaseSlot(removedSlot);
            return true;
        }

        public void Clear()
        {
            for (int denseIndex = 0; denseIndex < _count; denseIndex++)
            {
                int slot = _denseToSlot[denseIndex];

                _entities[denseIndex] = default;
                _denseToSlot[denseIndex] = InvalidIndex;
                ReleaseSlot(slot);
            }

            _count = 0;
        }

        public void EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "Capacity must be non-negative.");
            }

            EnsureEntityCapacity(capacity);
            EnsureSlotCapacity(capacity);
        }

        public bool Contains(PhysicsEntityHandle handle)
        {
            return TryGetDenseIndex(handle, out _);
        }

        public VoxelPhysicsEntity GetEntity(PhysicsEntityHandle handle)
        {
            if (!TryGetEntity(handle, out VoxelPhysicsEntity entity))
            {
                throw new ArgumentException(
                    "Physics entity handle does not reference a live entity.",
                    nameof(handle));
            }

            return entity;
        }

        public VoxelPhysicsEntity GetEntityAt(int denseIndex)
        {
            ValidateDenseIndex(denseIndex);
            return _entities[denseIndex];
        }

        public bool TryGetEntity(
            PhysicsEntityHandle handle,
            out VoxelPhysicsEntity entity)
        {
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                entity = default;
                return false;
            }

            entity = _entities[denseIndex];
            return true;
        }

        public bool TryGetHandleAt(
            int denseIndex,
            out PhysicsEntityHandle handle)
        {
            if ((uint)denseIndex >= (uint)_count)
            {
                handle = PhysicsEntityHandle.Invalid;
                return false;
            }

            handle = _entities[denseIndex].Handle;
            return true;
        }

        public bool TryGetDenseIndex(
            PhysicsEntityHandle handle,
            out int denseIndex)
        {
            if (!handle.IsValid ||
                (uint)handle.Index >= (uint)_slotCount ||
                _slotVersions[handle.Index] != handle.Version)
            {
                denseIndex = InvalidIndex;
                return false;
            }

            denseIndex = _slotToDense[handle.Index];
            if ((uint)denseIndex >= (uint)_count ||
                _entities[denseIndex].Handle != handle)
            {
                denseIndex = InvalidIndex;
                return false;
            }

            return true;
        }

        public bool TrySetEntityEnabled(
            PhysicsEntityHandle handle,
            bool isEnabled)
        {
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            _entities[denseIndex] = _entities[denseIndex].WithEnabled(isEnabled);
            return true;
        }

        public bool TrySetEntityArchetype(
            PhysicsEntityHandle handle,
            VoxelPhysicsEntityArchetype archetype)
        {
            VoxelPhysicsEntityDescriptor.ValidateArchetype(archetype);

            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            _entities[denseIndex] = _entities[denseIndex].WithArchetype(archetype);
            return true;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_entities, _count);
        }

        private int AllocateSlot()
        {
            if (_freeSlotHead != InvalidIndex)
            {
                int slot = _freeSlotHead;
                _freeSlotHead = _nextFreeSlot[slot];
                _nextFreeSlot[slot] = InvalidIndex;
                return slot;
            }

            EnsureSlotCapacity(_slotCount + 1);

            int newSlot = _slotCount++;
            _slotToDense[newSlot] = InvalidIndex;
            _slotVersions[newSlot] = 1;
            _nextFreeSlot[newSlot] = InvalidIndex;
            return newSlot;
        }

        private void ReleaseSlot(int slot)
        {
            _slotToDense[slot] = InvalidIndex;
            _slotVersions[slot] = GetNextVersion(_slotVersions[slot]);
            _nextFreeSlot[slot] = _freeSlotHead;
            _freeSlotHead = slot;
        }

        private void EnsureEntityCapacity(int capacity)
        {
            if (capacity <= _entities.Length)
            {
                return;
            }

            int newCapacity = GetExpandedCapacity(_entities.Length, capacity);
            Array.Resize(ref _entities, newCapacity);
            ResizeIndexArray(ref _denseToSlot, newCapacity);
        }

        private void EnsureSlotCapacity(int capacity)
        {
            if (capacity <= _slotToDense.Length)
            {
                return;
            }

            int newCapacity = GetExpandedCapacity(_slotToDense.Length, capacity);
            ResizeIndexArray(ref _slotToDense, newCapacity);
            Array.Resize(ref _slotVersions, newCapacity);
            ResizeIndexArray(ref _nextFreeSlot, newCapacity);
        }

        private void ValidateDenseIndex(int denseIndex)
        {
            if ((uint)denseIndex >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(denseIndex),
                    "Dense entity index is out of range.");
            }
        }

        private static int GetNextVersion(int version)
        {
            return version == int.MaxValue ? 1 : version + 1;
        }

        private static int GetExpandedCapacity(int currentCapacity, int requiredCapacity)
        {
            int newCapacity = Math.Max(1, currentCapacity);
            while (newCapacity < requiredCapacity)
            {
                newCapacity = checked(newCapacity * 2);
            }

            return newCapacity;
        }

        private static int[] CreateIndexArray(int capacity)
        {
            int[] values = new int[capacity];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = InvalidIndex;
            }

            return values;
        }

        private static void ResizeIndexArray(ref int[] values, int newCapacity)
        {
            int oldLength = values.Length;
            Array.Resize(ref values, newCapacity);
            for (int index = oldLength; index < values.Length; index++)
            {
                values[index] = InvalidIndex;
            }
        }

        public struct Enumerator
        {
            private readonly VoxelPhysicsEntity[] _entities;
            private readonly int _count;
            private int _index;

            internal Enumerator(VoxelPhysicsEntity[] entities, int count)
            {
                _entities = entities;
                _count = count;
                _index = -1;
            }

            public VoxelPhysicsEntity Current => _entities[_index];

            public bool MoveNext()
            {
                int nextIndex = _index + 1;
                if (nextIndex >= _count)
                {
                    return false;
                }

                _index = nextIndex;
                return true;
            }
        }
    }
}
