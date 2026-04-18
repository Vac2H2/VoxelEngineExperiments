using System;
using Unity.Collections;

namespace VoxelExperiments.Runtime.Rendering.VoxelGpuResourceSystem
{
    public interface IVoxelGpuResourceSystem : IVoxelGpuResourceView, IDisposable
    {
        int RetainModel(
            object modelKey,
            in VoxelModelUpload upload);

        void ReleaseModel(int modelResidencyId);

        int RetainPalette(
            object paletteKey,
            NativeArray<byte> paletteBytes);

        void ReleasePalette(int paletteResidencyId);

        void UpdateSurfaceTypes(NativeArray<uint> packedEntries);
    }
}
