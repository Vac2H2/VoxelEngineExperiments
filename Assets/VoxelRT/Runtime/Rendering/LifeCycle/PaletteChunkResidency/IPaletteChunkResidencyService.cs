using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.PaletteChunkResidency
{
    public interface IPaletteChunkResidencyService : IDisposable
    {
        GraphicsBuffer PaletteChunkBuffer { get; }

        GraphicsBuffer PaletteChunkStartBuffer { get; }

        uint PaletteChunkStartStrideBytes { get; }

        int Retain(
            object paletteKey,
            NativeArray<byte> paletteBytes);

        void Release(int residencyId);
    }
}
