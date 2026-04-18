using System;
using System.Collections.Generic;

namespace VoxelExperiments.Runtime.Rendering.ModelChunkResidency
{
    internal sealed class ChunkSpanAllocator : IChunkSpanAllocator
    {
        private readonly List<ChunkSpan> _freeSpans = new List<ChunkSpan>();
        private uint _nextUnallocatedSlot;

        public ChunkSpan Allocate(uint chunkCount)
        {
            if (chunkCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), "Chunk count must be greater than zero.");
            }

            for (int i = 0; i < _freeSpans.Count; i++)
            {
                ChunkSpan freeSpan = _freeSpans[i];
                if (freeSpan.Count < chunkCount)
                {
                    continue;
                }

                ChunkSpan allocated = new ChunkSpan(freeSpan.StartSlot, chunkCount);
                if (freeSpan.Count == chunkCount)
                {
                    _freeSpans.RemoveAt(i);
                }
                else
                {
                    _freeSpans[i] = new ChunkSpan(freeSpan.StartSlot + chunkCount, freeSpan.Count - chunkCount);
                }

                return allocated;
            }

            uint startSlot = _nextUnallocatedSlot;
            _nextUnallocatedSlot = checked(_nextUnallocatedSlot + chunkCount);
            return new ChunkSpan(startSlot, chunkCount);
        }

        public void Free(ChunkSpan span)
        {
            if (span.IsEmpty)
            {
                return;
            }

            int insertIndex = 0;
            while (insertIndex < _freeSpans.Count && _freeSpans[insertIndex].StartSlot < span.StartSlot)
            {
                insertIndex++;
            }

            if (insertIndex > 0)
            {
                ChunkSpan previous = _freeSpans[insertIndex - 1];
                if (previous.EndSlotExclusive > span.StartSlot)
                {
                    throw new InvalidOperationException("Cannot free an overlapping chunk span.");
                }

                if (previous.EndSlotExclusive == span.StartSlot)
                {
                    span = new ChunkSpan(previous.StartSlot, checked(previous.Count + span.Count));
                    _freeSpans.RemoveAt(insertIndex - 1);
                    insertIndex--;
                }
            }

            if (insertIndex < _freeSpans.Count)
            {
                ChunkSpan next = _freeSpans[insertIndex];
                if (span.EndSlotExclusive > next.StartSlot)
                {
                    throw new InvalidOperationException("Cannot free an overlapping chunk span.");
                }

                if (span.EndSlotExclusive == next.StartSlot)
                {
                    span = new ChunkSpan(span.StartSlot, checked(span.Count + next.Count));
                    _freeSpans.RemoveAt(insertIndex);
                }
            }

            _freeSpans.Insert(insertIndex, span);
        }

        public void Reset()
        {
            _freeSpans.Clear();
            _nextUnallocatedSlot = 0;
        }
    }
}
