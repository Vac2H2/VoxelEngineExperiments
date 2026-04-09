using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Rendering.ModelChunkResidency
{
    public interface IModelChunkResidencyService : IDisposable
    {
        GraphicsBuffer OccupancyChunkBuffer { get; }

        GraphicsBuffer VoxelDataChunkBuffer { get; }

        GraphicsBuffer ModelChunkStartBuffer { get; }

        uint ModelChunkStartStrideBytes { get; }

        int Retain(
            object modelKey,
            uint chunkCount,
            NativeArray<byte> occupancyBytes,
            NativeArray<byte> voxelBytes);

        void Release(int residencyId);
    }
}
