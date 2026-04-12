using UnityEngine;

namespace VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem
{
    public interface IVoxelGpuResourceView
    {
        GraphicsBuffer OccupancyChunkBuffer { get; }

        GraphicsBuffer VoxelDataChunkBuffer { get; }

        GraphicsBuffer ModelChunkStartBuffer { get; }

        GraphicsBuffer PaletteChunkBuffer { get; }

        GraphicsBuffer PaletteChunkStartBuffer { get; }

        GraphicsBuffer SurfaceTypeTableBuffer { get; }

        uint ModelChunkStartStrideBytes { get; }

        uint PaletteChunkStartStrideBytes { get; }

        uint SurfaceTypeTableStrideBytes { get; }

        uint SurfaceTypeEntryCount { get; }

        VoxelModelResourceDescriptor GetModelResourceDescriptor(int modelResidencyId);
    }
}
