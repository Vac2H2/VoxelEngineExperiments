using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.SurfaceTypeTableService
{
    public interface ISurfaceTypeTableService : IDisposable
    {
        GraphicsBuffer SurfaceTypeTableBuffer { get; }

        uint SurfaceTypeTableStrideBytes { get; }

        uint SurfaceTypeEntryCount { get; }

        void Update(NativeArray<uint> packedEntries);
    }
}
