using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.ModelChunkResidency
{
    internal interface IRawChunkBufferBackend : IDisposable
    {
        uint StrideBytes { get; }

        uint CapacityChunks { get; }

        GraphicsBuffer Buffer { get; }

        void EnsureCapacity(uint minChunkCapacity);

        void Upload(ChunkSpan span, NativeArray<byte> rawBytes);
    }
}
