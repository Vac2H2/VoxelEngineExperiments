using UnityEngine;

namespace VoxelRT.Runtime.Rendering.VoxelRayTracingResourceBinder
{
    public readonly struct VoxelRayTracingResourceBindingLayout
    {
        public VoxelRayTracingResourceBindingLayout(
            int occupancyChunkBufferPropertyId,
            int voxelDataChunkBufferPropertyId,
            int modelChunkStartBufferPropertyId,
            int paletteChunkBufferPropertyId,
            int paletteChunkStartBufferPropertyId,
            int surfaceTypeTableBufferPropertyId,
            int modelChunkStartStrideBytesPropertyId,
            int paletteChunkStartStrideBytesPropertyId,
            int surfaceTypeTableStrideBytesPropertyId,
            int surfaceTypeEntryCountPropertyId)
        {
            OccupancyChunkBufferPropertyId = occupancyChunkBufferPropertyId;
            VoxelDataChunkBufferPropertyId = voxelDataChunkBufferPropertyId;
            ModelChunkStartBufferPropertyId = modelChunkStartBufferPropertyId;
            PaletteChunkBufferPropertyId = paletteChunkBufferPropertyId;
            PaletteChunkStartBufferPropertyId = paletteChunkStartBufferPropertyId;
            SurfaceTypeTableBufferPropertyId = surfaceTypeTableBufferPropertyId;
            ModelChunkStartStrideBytesPropertyId = modelChunkStartStrideBytesPropertyId;
            PaletteChunkStartStrideBytesPropertyId = paletteChunkStartStrideBytesPropertyId;
            SurfaceTypeTableStrideBytesPropertyId = surfaceTypeTableStrideBytesPropertyId;
            SurfaceTypeEntryCountPropertyId = surfaceTypeEntryCountPropertyId;
        }

        public int OccupancyChunkBufferPropertyId { get; }

        public int VoxelDataChunkBufferPropertyId { get; }

        public int ModelChunkStartBufferPropertyId { get; }

        public int PaletteChunkBufferPropertyId { get; }

        public int PaletteChunkStartBufferPropertyId { get; }

        public int SurfaceTypeTableBufferPropertyId { get; }

        public int ModelChunkStartStrideBytesPropertyId { get; }

        public int PaletteChunkStartStrideBytesPropertyId { get; }

        public int SurfaceTypeTableStrideBytesPropertyId { get; }

        public int SurfaceTypeEntryCountPropertyId { get; }

        public static VoxelRayTracingResourceBindingLayout Default =>
            new VoxelRayTracingResourceBindingLayout(
                Shader.PropertyToID("_VoxelOccupancyChunkBuffer"),
                Shader.PropertyToID("_VoxelDataChunkBuffer"),
                Shader.PropertyToID("_VoxelModelChunkStartBuffer"),
                Shader.PropertyToID("_VoxelPaletteChunkBuffer"),
                Shader.PropertyToID("_VoxelPaletteChunkStartBuffer"),
                Shader.PropertyToID("_VoxelSurfaceTypeTableBuffer"),
                Shader.PropertyToID("_VoxelModelChunkStartStrideBytes"),
                Shader.PropertyToID("_VoxelPaletteChunkStartStrideBytes"),
                Shader.PropertyToID("_VoxelSurfaceTypeTableStrideBytes"),
                Shader.PropertyToID("_VoxelSurfaceTypeEntryCount"));
    }
}
