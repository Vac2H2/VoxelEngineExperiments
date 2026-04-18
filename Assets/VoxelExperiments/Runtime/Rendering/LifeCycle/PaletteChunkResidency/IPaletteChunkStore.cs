using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.PaletteChunkResidency
{
    internal interface IPaletteChunkStore : IDisposable
    {
        uint ChunkStrideBytes { get; }

        GraphicsBuffer Buffer { get; }

        void EnsureCapacity(uint minChunkCapacity);

        void Upload(uint slot, NativeArray<byte> rawBytes);
    }
}
