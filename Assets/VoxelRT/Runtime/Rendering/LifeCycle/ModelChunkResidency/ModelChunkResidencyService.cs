using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.ModelChunkResidency
{
    public sealed class ModelChunkResidencyService : IModelChunkResidencyService
    {
        private const uint ModelChunkStartStrideBytesValue = sizeof(uint);
        private const uint InvalidStartSlot = uint.MaxValue;

        private readonly IChunkSpanAllocator _allocator;
        private readonly IOccupancyChunkStore _occupancyStore;
        private readonly IVoxelDataChunkStore _voxelDataStore;
        private readonly bool _ownsDependencies;
        private readonly Dictionary<object, ResidencyEntry> _entries = new Dictionary<object, ResidencyEntry>();
        private readonly List<ResidencyEntry> _entriesByResidencyId = new List<ResidencyEntry>();
        private readonly Stack<int> _freeResidencyIds = new Stack<int>();
        private GraphicsBuffer _modelChunkStartBuffer;
        private uint[] _modelChunkStartSlots = Array.Empty<uint>();

        public ModelChunkResidencyService()
            : this(new ChunkSpanAllocator(), new OccupancyChunkStore(), new VoxelDataChunkStore(), true)
        {
        }

        internal ModelChunkResidencyService(
            IChunkSpanAllocator allocator,
            IOccupancyChunkStore occupancyStore,
            IVoxelDataChunkStore voxelDataStore,
            bool ownsDependencies)
        {
            _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
            _occupancyStore = occupancyStore ?? throw new ArgumentNullException(nameof(occupancyStore));
            _voxelDataStore = voxelDataStore ?? throw new ArgumentNullException(nameof(voxelDataStore));
            _ownsDependencies = ownsDependencies;
            ResizeModelChunkStartStorage(0);
        }

        public GraphicsBuffer OccupancyChunkBuffer => _occupancyStore.Buffer;

        public GraphicsBuffer VoxelDataChunkBuffer => _voxelDataStore.Buffer;

        public GraphicsBuffer ModelChunkStartBuffer => _modelChunkStartBuffer;

        public uint ModelChunkStartStrideBytes => ModelChunkStartStrideBytesValue;

        public int Retain(
            object modelKey,
            uint chunkCount,
            NativeArray<byte> occupancyBytes,
            NativeArray<byte> voxelBytes)
        {
            if (modelKey == null)
            {
                throw new ArgumentNullException(nameof(modelKey));
            }

            if (chunkCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), "Chunk count must be greater than zero.");
            }

            if (_entries.TryGetValue(modelKey, out ResidencyEntry existingEntry))
            {
                if (existingEntry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("Model residency refcount overflow.");
                }

                existingEntry.RefCount++;
                return checked((int)existingEntry.ResidencyId);
            }

            ValidateRawLength(nameof(occupancyBytes), occupancyBytes, chunkCount, _occupancyStore.ChunkStrideBytes);
            ValidateRawLength(nameof(voxelBytes), voxelBytes, chunkCount, _voxelDataStore.ChunkStrideBytes);

            ChunkSpan span = _allocator.Allocate(chunkCount);
            bool committed = false;

            try
            {
                uint requiredChunkCapacity = span.EndSlotExclusive;
                _occupancyStore.EnsureCapacity(requiredChunkCapacity);
                _voxelDataStore.EnsureCapacity(requiredChunkCapacity);

                _occupancyStore.Upload(span, occupancyBytes);
                _voxelDataStore.Upload(span, voxelBytes);

                int residencyId = AllocateResidencyId();
                ResidencyEntry newEntry = new ResidencyEntry(
                    modelKey,
                    checked((uint)residencyId),
                    span,
                    chunkCount,
                    occupancyBytes.ToArray(),
                    voxelBytes.ToArray());
                _entries.Add(modelKey, newEntry);
                _entriesByResidencyId[residencyId] = newEntry;
                SetModelChunkStart(newEntry);
                UploadAllModelChunkStarts();
                committed = true;
                return residencyId;
            }
            finally
            {
                if (!committed)
                {
                    _allocator.Free(span);
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

            _entries.Remove(entry.ModelKey);
            entry.MarkReleased();
            _modelChunkStartSlots[residencyId] = InvalidStartSlot;
            _entriesByResidencyId[residencyId] = null;
            _freeResidencyIds.Push(residencyId);
            TrimTrailingFreeResidencyIds();
            CompactResidentData();
        }

        public void Dispose()
        {
            _modelChunkStartBuffer?.Dispose();
            _modelChunkStartBuffer = null;

            if (_ownsDependencies)
            {
                _occupancyStore.Dispose();
                _voxelDataStore.Dispose();
            }
        }

        private static void ValidateRawLength(
            string paramName,
            NativeArray<byte> rawBytes,
            uint chunkCount,
            uint strideBytes)
        {
            uint expectedByteLength = checked(chunkCount * strideBytes);
            if ((uint)rawBytes.Length != expectedByteLength)
            {
                throw new ArgumentException(
                    $"Expected {expectedByteLength} bytes for {chunkCount} chunks at stride {strideBytes}, got {rawBytes.Length}.",
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

            residentEntries.Sort((left, right) => left.Span.StartSlot.CompareTo(right.Span.StartSlot));

            _allocator.Reset();
            _occupancyStore.Dispose();
            _voxelDataStore.Dispose();

            uint totalChunkCount = 0;
            for (int i = 0; i < residentEntries.Count; i++)
            {
                totalChunkCount = checked(totalChunkCount + residentEntries[i].ChunkCount);
            }

            uint targetChunkCapacity = Math.Max(totalChunkCount, 1u);
            _occupancyStore.EnsureCapacity(targetChunkCapacity);
            _voxelDataStore.EnsureCapacity(targetChunkCapacity);

            for (int i = 0; i < _modelChunkStartSlots.Length; i++)
            {
                _modelChunkStartSlots[i] = InvalidStartSlot;
            }

            for (int i = 0; i < residentEntries.Count; i++)
            {
                ResidencyEntry entry = residentEntries[i];
                ChunkSpan compactedSpan = _allocator.Allocate(entry.ChunkCount);

                using NativeArray<byte> occupancyBytes = new NativeArray<byte>(entry.OccupancyBytes, Allocator.Temp);
                using NativeArray<byte> voxelBytes = new NativeArray<byte>(entry.VoxelBytes, Allocator.Temp);

                _occupancyStore.Upload(compactedSpan, occupancyBytes);
                _voxelDataStore.Upload(compactedSpan, voxelBytes);

                entry.UpdateSpan(compactedSpan);
                SetModelChunkStart(entry);
            }

            UploadAllModelChunkStarts();
        }

        private int AllocateResidencyId()
        {
            if (_freeResidencyIds.Count > 0)
            {
                return _freeResidencyIds.Pop();
            }

            int residencyId = _entriesByResidencyId.Count;
            EnsureModelChunkStartCapacity(checked((uint)residencyId + 1u));
            _entriesByResidencyId.Add(null);
            return residencyId;
        }

        private void EnsureModelChunkStartCapacity(uint minEntryCapacity)
        {
            if (minEntryCapacity <= _modelChunkStartSlots.Length)
            {
                return;
            }

            uint newCapacity = _modelChunkStartSlots.Length == 0 ? 1u : (uint)_modelChunkStartSlots.Length;
            while (newCapacity < minEntryCapacity)
            {
                newCapacity = checked(newCapacity * 2u);
            }

            uint[] newStartSlots = new uint[newCapacity];
            for (int i = 0; i < newStartSlots.Length; i++)
            {
                newStartSlots[i] = InvalidStartSlot;
            }

            Array.Copy(_modelChunkStartSlots, newStartSlots, _modelChunkStartSlots.Length);
            _modelChunkStartSlots = newStartSlots;

            GraphicsBuffer newBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                checked((int)newCapacity),
                checked((int)ModelChunkStartStrideBytesValue));

            if (_modelChunkStartBuffer != null)
            {
                _modelChunkStartBuffer.Dispose();
            }

            _modelChunkStartBuffer = newBuffer;
        }

        private void SetModelChunkStart(ResidencyEntry entry)
        {
            _modelChunkStartSlots[checked((int)entry.ResidencyId)] = entry.Span.StartSlot;
        }

        private void UploadAllModelChunkStarts()
        {
            if (_modelChunkStartBuffer == null || _modelChunkStartSlots.Length == 0)
            {
                return;
            }

            _modelChunkStartBuffer.SetData(_modelChunkStartSlots);
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

            ResizeModelChunkStartStorage(newCount);
        }

        private void ResizeModelChunkStartStorage(int entryCount)
        {
            int storageEntryCount = Math.Max(entryCount, 1);
            uint[] resizedStarts = new uint[storageEntryCount];
            for (int i = 0; i < resizedStarts.Length; i++)
            {
                resizedStarts[i] = InvalidStartSlot;
            }

            int copyCount = Math.Min(_modelChunkStartSlots.Length, resizedStarts.Length);
            Array.Copy(_modelChunkStartSlots, resizedStarts, copyCount);
            _modelChunkStartSlots = resizedStarts;

            _modelChunkStartBuffer?.Dispose();
            _modelChunkStartBuffer = null;

            _modelChunkStartBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                storageEntryCount,
                checked((int)ModelChunkStartStrideBytesValue));
            UploadAllModelChunkStarts();
        }

        private sealed class ResidencyEntry
        {
            public ResidencyEntry(
                object modelKey,
                uint residencyId,
                ChunkSpan span,
                uint chunkCount,
                byte[] occupancyBytes,
                byte[] voxelBytes)
            {
                ModelKey = modelKey ?? throw new ArgumentNullException(nameof(modelKey));
                ResidencyId = residencyId;
                Span = span;
                ChunkCount = chunkCount;
                OccupancyBytes = occupancyBytes ?? throw new ArgumentNullException(nameof(occupancyBytes));
                VoxelBytes = voxelBytes ?? throw new ArgumentNullException(nameof(voxelBytes));
                IsResident = true;
                RefCount = 1;
            }

            public object ModelKey { get; }

            public uint ResidencyId { get; }

            public ChunkSpan Span { get; private set; }

            public uint ChunkCount { get; }

            public byte[] OccupancyBytes { get; }

            public byte[] VoxelBytes { get; }

            public bool IsResident { get; private set; }

            public uint RefCount { get; set; }

            public void UpdateSpan(ChunkSpan span)
            {
                Span = span;
            }

            public void MarkReleased()
            {
                IsResident = false;
            }
        }

    }
}
