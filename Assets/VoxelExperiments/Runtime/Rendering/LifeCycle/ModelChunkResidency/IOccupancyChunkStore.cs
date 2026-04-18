using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.ModelChunkResidency
{
    internal interface IOccupancyChunkStore : IDisposable
    {
        uint ChunkStrideBytes { get; }

        GraphicsBuffer Buffer { get; }

        void EnsureCapacity(uint minChunkCapacity);

        void Upload(ChunkSpan span, NativeArray<byte> rawBytes);
    }
}
