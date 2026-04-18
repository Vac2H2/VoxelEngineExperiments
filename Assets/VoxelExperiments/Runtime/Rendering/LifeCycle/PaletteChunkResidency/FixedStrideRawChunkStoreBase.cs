using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.PaletteChunkResidency
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

        public void Upload(uint slot, NativeArray<byte> rawBytes)
        {
            ValidateStride();

            if (!rawBytes.IsCreated)
            {
                throw new ArgumentException("Upload data must be created.", nameof(rawBytes));
            }

            if ((uint)rawBytes.Length != FixedStrideBytes)
            {
                throw new ArgumentException(
                    $"Upload byte count mismatch. Expected {FixedStrideBytes} bytes, got {rawBytes.Length}.",
                    nameof(rawBytes));
            }

            EnsureCapacity(checked(slot + 1u));

            NativeArray<uint> rawWords = rawBytes.Reinterpret<uint>(sizeof(byte));
            uint[] uploadWords = rawWords.ToArray();
            int startWordIndex = GetWordIndex(slot);
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

        private int GetWordIndex(uint slot)
        {
            return checked((int)(slot * GetWordsPerChunk()));
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
