using System;
using System.Collections.Generic;

namespace VoxelRT.Runtime.Rendering.PaletteChunkResidency
{
    internal sealed class PaletteSlotAllocator : IPaletteSlotAllocator
    {
        private readonly Stack<uint> _freeSlots = new Stack<uint>();
        private uint _nextUnallocatedSlot;

        public uint Allocate()
        {
            if (_freeSlots.Count > 0)
            {
                return _freeSlots.Pop();
            }

            uint slot = _nextUnallocatedSlot;
            _nextUnallocatedSlot = checked(_nextUnallocatedSlot + 1u);
            return slot;
        }

        public void Free(uint slot)
        {
            if (slot >= _nextUnallocatedSlot)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), "Slot must have been previously allocated.");
            }

            _freeSlots.Push(slot);
        }

        public void Reset()
        {
            _freeSlots.Clear();
            _nextUnallocatedSlot = 0;
        }
    }
}
