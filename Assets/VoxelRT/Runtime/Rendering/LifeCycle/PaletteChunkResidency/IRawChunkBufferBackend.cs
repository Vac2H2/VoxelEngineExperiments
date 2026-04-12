using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.PaletteChunkResidency
{
    internal interface IRawChunkBufferBackend : IDisposable
    {
        uint StrideBytes { get; }

        uint CapacityChunks { get; }

        GraphicsBuffer Buffer { get; }

        void EnsureCapacity(uint minChunkCapacity);

        void Upload(uint slot, NativeArray<byte> rawBytes);
    }
}
