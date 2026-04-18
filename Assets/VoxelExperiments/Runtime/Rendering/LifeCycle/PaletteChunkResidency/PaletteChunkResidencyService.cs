using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.PaletteChunkResidency
{
    public sealed class PaletteChunkResidencyService : IPaletteChunkResidencyService
    {
        private const uint PaletteChunkStartStrideBytesValue = sizeof(uint);
        private const uint InvalidStartSlot = uint.MaxValue;

        private readonly IPaletteSlotAllocator _allocator;
        private readonly IPaletteChunkStore _paletteChunkStore;
        private readonly bool _ownsDependencies;
        private readonly Dictionary<object, ResidencyEntry> _entries = new Dictionary<object, ResidencyEntry>();
        private readonly List<ResidencyEntry> _entriesByResidencyId = new List<ResidencyEntry>();
        private readonly Stack<int> _freeResidencyIds = new Stack<int>();
        private GraphicsBuffer _paletteChunkStartBuffer;
        private uint[] _paletteChunkStartSlots = Array.Empty<uint>();

        public PaletteChunkResidencyService()
            : this(new PaletteSlotAllocator(), new PaletteChunkStore(), true)
        {
        }

        internal PaletteChunkResidencyService(
            IPaletteSlotAllocator allocator,
            IPaletteChunkStore paletteChunkStore,
            bool ownsDependencies)
        {
            _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
            _paletteChunkStore = paletteChunkStore ?? throw new ArgumentNullException(nameof(paletteChunkStore));
            _ownsDependencies = ownsDependencies;
            ResizePaletteChunkStartStorage(0);
        }

        public GraphicsBuffer PaletteChunkBuffer => _paletteChunkStore.Buffer;

        public GraphicsBuffer PaletteChunkStartBuffer => _paletteChunkStartBuffer;

        public uint PaletteChunkStartStrideBytes => PaletteChunkStartStrideBytesValue;

        public int Retain(
            object paletteKey,
            NativeArray<byte> paletteBytes)
        {
            if (paletteKey == null)
            {
                throw new ArgumentNullException(nameof(paletteKey));
            }

            if (_entries.TryGetValue(paletteKey, out ResidencyEntry existingEntry))
            {
                if (existingEntry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("Palette residency refcount overflow.");
                }

                existingEntry.RefCount++;
                return checked((int)existingEntry.ResidencyId);
            }

            ValidateRawLength(nameof(paletteBytes), paletteBytes, _paletteChunkStore.ChunkStrideBytes);

            uint slot = _allocator.Allocate();
            bool committed = false;

            try
            {
                _paletteChunkStore.EnsureCapacity(checked(slot + 1u));
                _paletteChunkStore.Upload(slot, paletteBytes);

                int residencyId = AllocateResidencyId();
                ResidencyEntry newEntry = new ResidencyEntry(
                    paletteKey,
                    checked((uint)residencyId),
                    slot,
                    paletteBytes.ToArray());
                _entries.Add(paletteKey, newEntry);
                _entriesByResidencyId[residencyId] = newEntry;
                SetPaletteChunkStart(newEntry);
                UploadAllPaletteChunkStarts();
                committed = true;
                return residencyId;
            }
            finally
            {
                if (!committed)
                {
                    _allocator.Free(slot);
                }
            }
        }

        public void Release(int residencyId)
        {
            if (residencyId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(residencyId), "Residency id must be non-negative.");
            }

            if (residencyId >= _entriesByResidencyId.Count)
            {
                throw new KeyNotFoundException("The specified residency id is not valid.");
            }

            ResidencyEntry entry = _entriesByResidencyId[residencyId];
            if (entry == null || !entry.IsResident)
            {
                throw new KeyNotFoundException("The specified residency id is not resident.");
            }

            if (entry.RefCount > 1)
            {
                entry.RefCount--;
                return;
            }

            _entries.Remove(entry.PaletteKey);
            entry.MarkReleased();
            _paletteChunkStartSlots[residencyId] = InvalidStartSlot;
            _entriesByResidencyId[residencyId] = null;
            _freeResidencyIds.Push(residencyId);
            TrimTrailingFreeResidencyIds();
            CompactResidentData();
        }

        public void Dispose()
        {
            _paletteChunkStartBuffer?.Dispose();
            _paletteChunkStartBuffer = null;

            if (_ownsDependencies)
            {
                _paletteChunkStore.Dispose();
            }
        }

        private static void ValidateRawLength(
            string paramName,
            NativeArray<byte> rawBytes,
            uint strideBytes)
        {
            if ((uint)rawBytes.Length != strideBytes)
            {
                throw new ArgumentException(
                    $"Expected {strideBytes} bytes for a single palette chunk (256 entries x 4 bytes), got {rawBytes.Length}.",
                    paramName);
            }
        }

        private void CompactResidentData()
        {
            List<ResidencyEntry> residentEntries = new List<ResidencyEntry>();
            foreach (ResidencyEntry entry in _entriesByResidencyId)
            {
                if (entry != null && entry.IsResident)
                {
                    residentEntries.Add(entry);
                }
            }

            residentEntries.Sort((left, right) => left.Slot.CompareTo(right.Slot));

            _allocator.Reset();
            _paletteChunkStore.Dispose();

            _paletteChunkStore.EnsureCapacity(Math.Max(checked((uint)residentEntries.Count), 1u));

            for (int i = 0; i < _paletteChunkStartSlots.Length; i++)
            {
                _paletteChunkStartSlots[i] = InvalidStartSlot;
            }

            for (int i = 0; i < residentEntries.Count; i++)
            {
                ResidencyEntry entry = residentEntries[i];
                uint compactedSlot = _allocator.Allocate();

                using NativeArray<byte> paletteBytes = new NativeArray<byte>(entry.PaletteBytes, Allocator.Temp);
                _paletteChunkStore.Upload(compactedSlot, paletteBytes);

                entry.UpdateSlot(compactedSlot);
                SetPaletteChunkStart(entry);
            }

            UploadAllPaletteChunkStarts();
        }

        private int AllocateResidencyId()
        {
            if (_freeResidencyIds.Count > 0)
            {
                return _freeResidencyIds.Pop();
            }

            int residencyId = _entriesByResidencyId.Count;
            EnsurePaletteChunkStartCapacity(checked((uint)residencyId + 1u));
            _entriesByResidencyId.Add(null);
            return residencyId;
        }

        private void EnsurePaletteChunkStartCapacity(uint minEntryCapacity)
        {
            if (minEntryCapacity <= _paletteChunkStartSlots.Length)
            {
                return;
            }

            uint newCapacity = _paletteChunkStartSlots.Length == 0 ? 1u : (uint)_paletteChunkStartSlots.Length;
            while (newCapacity < minEntryCapacity)
            {
                newCapacity = checked(newCapacity * 2u);
            }

            uint[] newStartSlots = new uint[newCapacity];
            for (int i = 0; i < newStartSlots.Length; i++)
            {
                newStartSlots[i] = InvalidStartSlot;
            }

            Array.Copy(_paletteChunkStartSlots, newStartSlots, _paletteChunkStartSlots.Length);
            _paletteChunkStartSlots = newStartSlots;

            GraphicsBuffer newBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                checked((int)newCapacity),
                checked((int)PaletteChunkStartStrideBytesValue));

            if (_paletteChunkStartBuffer != null)
            {
                _paletteChunkStartBuffer.Dispose();
            }

            _paletteChunkStartBuffer = newBuffer;
        }

        private void SetPaletteChunkStart(ResidencyEntry entry)
        {
            _paletteChunkStartSlots[checked((int)entry.ResidencyId)] = entry.Slot;
        }

        private void UploadAllPaletteChunkStarts()
        {
            if (_paletteChunkStartBuffer == null || _paletteChunkStartSlots.Length == 0)
            {
                return;
            }

            _paletteChunkStartBuffer.SetData(_paletteChunkStartSlots);
        }

        private void TrimTrailingFreeResidencyIds()
        {
            int newCount = _entriesByResidencyId.Count;
            while (newCount > 0 && _entriesByResidencyId[newCount - 1] == null)
            {
                newCount--;
            }

            if (newCount == _entriesByResidencyId.Count)
            {
                return;
            }

            _entriesByResidencyId.RemoveRange(newCount, _entriesByResidencyId.Count - newCount);

            int[] retainedFreeIds = _freeResidencyIds.ToArray();
            _freeResidencyIds.Clear();
            for (int i = retainedFreeIds.Length - 1; i >= 0; i--)
            {
                if (retainedFreeIds[i] < newCount)
                {
                    _freeResidencyIds.Push(retainedFreeIds[i]);
                }
            }

            ResizePaletteChunkStartStorage(newCount);
        }

        private void ResizePaletteChunkStartStorage(int entryCount)
        {
            int storageEntryCount = Math.Max(entryCount, 1);
            uint[] resizedStarts = new uint[storageEntryCount];
            for (int i = 0; i < resizedStarts.Length; i++)
            {
                resizedStarts[i] = InvalidStartSlot;
            }

            int copyCount = Math.Min(_paletteChunkStartSlots.Length, resizedStarts.Length);
            Array.Copy(_paletteChunkStartSlots, resizedStarts, copyCount);
            _paletteChunkStartSlots = resizedStarts;

            _paletteChunkStartBuffer?.Dispose();
            _paletteChunkStartBuffer = null;

            _paletteChunkStartBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                storageEntryCount,
                checked((int)PaletteChunkStartStrideBytesValue));
            UploadAllPaletteChunkStarts();
        }

        private sealed class ResidencyEntry
        {
            public ResidencyEntry(
                object paletteKey,
                uint residencyId,
                uint slot,
                byte[] paletteBytes)
            {
                PaletteKey = paletteKey ?? throw new ArgumentNullException(nameof(paletteKey));
                ResidencyId = residencyId;
                Slot = slot;
                PaletteBytes = paletteBytes ?? throw new ArgumentNullException(nameof(paletteBytes));
                IsResident = true;
                RefCount = 1;
            }

            public object PaletteKey { get; }

            public uint ResidencyId { get; }

            public uint Slot { get; private set; }

            public byte[] PaletteBytes { get; }

            public bool IsResident { get; private set; }

            public uint RefCount { get; set; }

            public void UpdateSlot(uint slot)
            {
                Slot = slot;
            }

            public void MarkReleased()
            {
                IsResident = false;
            }
        }
    }
}
