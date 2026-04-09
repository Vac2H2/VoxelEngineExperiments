using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.ModelChunkResidency
{
    internal abstract class FixedStrideRawChunkStoreBase : IRawChunkBufferBackend
    {
        private GraphicsBuffer _buffer;

        protected abstract uint FixedStrideBytes { get; }

        public uint StrideBytes => FixedStrideBytes;

        public uint CapacityChunks { get; private set; }

        public GraphicsBuffer Buffer => _buffer;

        public void EnsureCapacity(uint minChunkCapacity)
        {
            if (minChunkCapacity <= CapacityChunks)
            {
                return;
            }

            if (CapacityChunks == 0)
            {
                Resize(minChunkCapacity);
                return;
            }

            uint newChunkCapacity = CapacityChunks;
            while (newChunkCapacity < minChunkCapacity)
            {
                newChunkCapacity = checked(newChunkCapacity * 2u);
            }

            Resize(newChunkCapacity);
        }

        public void Upload(ChunkSpan span, NativeArray<byte> rawBytes)
        {
            ValidateStride();

            uint expectedByteLength = checked(span.Count * FixedStrideBytes);
            if (expectedByteLength == 0)
            {
                if (rawBytes.Length != 0)
                {
                    throw new ArgumentException("Empty spans must be paired with empty uploads.", nameof(rawBytes));
                }

                return;
            }

            if (!rawBytes.IsCreated)
            {
                throw new ArgumentException("Upload data must be created for non-empty spans.", nameof(rawBytes));
            }

            if ((uint)rawBytes.Length != expectedByteLength)
            {
                throw new ArgumentException(
                    $"Upload byte count mismatch. Expected {expectedByteLength} bytes, got {rawBytes.Length}.",
                    nameof(rawBytes));
            }

            EnsureCapacity(span.EndSlotExclusive);

            NativeArray<uint> rawWords = rawBytes.Reinterpret<uint>(sizeof(byte));
            uint[] uploadWords = rawWords.ToArray();
            int startWordIndex = GetWordIndex(span.StartSlot);
            _buffer.SetData(uploadWords, 0, startWordIndex, uploadWords.Length);
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
            CapacityChunks = 0;
        }

        private void Resize(uint newChunkCapacity)
        {
            int wordCapacity = GetWordCount(newChunkCapacity);
            GraphicsBuffer newBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, wordCapacity, sizeof(uint));

            if (_buffer != null)
            {
                int previousWordCount = GetWordCount(CapacityChunks);
                if (previousWordCount > 0)
                {
                    uint[] previousWords = new uint[previousWordCount];
                    _buffer.GetData(previousWords);
                    newBuffer.SetData(previousWords, 0, 0, previousWords.Length);
                }

                _buffer.Dispose();
            }

            _buffer = newBuffer;
            CapacityChunks = newChunkCapacity;
        }

        private int GetWordIndex(uint startSlot)
        {
            return checked((int)(startSlot * GetWordsPerChunk()));
        }

        private int GetWordCount(uint chunkCapacity)
        {
            return checked((int)(chunkCapacity * GetWordsPerChunk()));
        }

        private uint GetWordsPerChunk()
        {
            return FixedStrideBytes / sizeof(uint);
        }

        private void ValidateStride()
        {
            if ((FixedStrideBytes % sizeof(uint)) != 0)
            {
                throw new InvalidOperationException("Raw GraphicsBuffer stride must be a multiple of 4 bytes.");
            }
        }
    }
}
