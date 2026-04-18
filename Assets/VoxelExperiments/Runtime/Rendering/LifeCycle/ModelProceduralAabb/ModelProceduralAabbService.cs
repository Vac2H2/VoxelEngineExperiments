using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.ModelProceduralAabb
{
    public sealed class ModelProceduralAabbService : IModelProceduralAabbService
    {
        private const int AabbStrideBytesConst = 24;

        private readonly Dictionary<object, Entry> _entriesByKey = new Dictionary<object, Entry>();
        private readonly List<Entry> _entriesById = new List<Entry>();
        private readonly Stack<int> _freeResidencyIds = new Stack<int>();
        private bool _isDisposed;

        public uint AabbStrideBytes => AabbStrideBytesConst;

        public int Retain(
            object modelKey,
            NativeArray<ModelChunkAabb> chunkAabbs)
        {
            EnsureNotDisposed();

            if (modelKey == null)
            {
                throw new ArgumentNullException(nameof(modelKey));
            }

            if (chunkAabbs.Length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkAabbs), "Chunk AABB count must be greater than zero.");
            }

            if (_entriesByKey.TryGetValue(modelKey, out Entry existingEntry))
            {
                if (existingEntry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("Model procedural AABB refcount overflow.");
                }

                existingEntry.RefCount++;
                return existingEntry.ResidencyId;
            }

            GraphicsBuffer aabbBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                chunkAabbs.Length,
                AabbStrideBytesConst);
            aabbBuffer.SetData(chunkAabbs);

            int residencyId = AllocateResidencyId();
            Entry newEntry = new Entry(
                modelKey,
                residencyId,
                aabbBuffer,
                chunkAabbs.Length);

            _entriesByKey.Add(modelKey, newEntry);
            _entriesById[residencyId] = newEntry;
            return residencyId;
        }

        public void Release(int residencyId)
        {
            EnsureNotDisposed();

            Entry entry = GetEntry(residencyId);
            if (entry.RefCount > 1)
            {
                entry.RefCount--;
                return;
            }

            _entriesByKey.Remove(entry.ModelKey);
            _entriesById[residencyId] = null;
            _freeResidencyIds.Push(residencyId);
            entry.AabbBuffer.Dispose();
        }

        public ModelProceduralAabbDescriptor GetDescriptor(int residencyId)
        {
            EnsureNotDisposed();

            Entry entry = GetEntry(residencyId);
            return new ModelProceduralAabbDescriptor(
                entry.ResidencyId,
                entry.AabbBuffer,
                entry.AabbCount);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            foreach (Entry entry in _entriesById)
            {
                entry?.AabbBuffer.Dispose();
            }

            _entriesByKey.Clear();
            _entriesById.Clear();
            _freeResidencyIds.Clear();
            _isDisposed = true;
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ModelProceduralAabbService));
            }
        }

        private int AllocateResidencyId()
        {
            if (_freeResidencyIds.Count > 0)
            {
                return _freeResidencyIds.Pop();
            }

            int residencyId = _entriesById.Count;
            _entriesById.Add(null);
            return residencyId;
        }

        private Entry GetEntry(int residencyId)
        {
            if (residencyId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(residencyId), "Residency id must be non-negative.");
            }

            if (residencyId >= _entriesById.Count)
            {
                throw new KeyNotFoundException("The specified model procedural AABB residency id is not valid.");
            }

            Entry entry = _entriesById[residencyId];
            if (entry == null)
            {
                throw new KeyNotFoundException("The specified model procedural AABB residency id is not resident.");
            }

            return entry;
        }

        private sealed class Entry
        {
            public Entry(
                object modelKey,
                int residencyId,
                GraphicsBuffer aabbBuffer,
                int aabbCount)
            {
                ModelKey = modelKey ?? throw new ArgumentNullException(nameof(modelKey));
                ResidencyId = residencyId;
                AabbBuffer = aabbBuffer ?? throw new ArgumentNullException(nameof(aabbBuffer));
                AabbCount = aabbCount;
                RefCount = 1;
            }

            public object ModelKey { get; }

            public int ResidencyId { get; }

            public GraphicsBuffer AabbBuffer { get; }

            public int AabbCount { get; }

            public uint RefCount { get; set; }
        }
    }
}
