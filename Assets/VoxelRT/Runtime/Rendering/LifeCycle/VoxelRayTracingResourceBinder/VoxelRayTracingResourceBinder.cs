using System;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem;

namespace VoxelRT.Runtime.Rendering.VoxelRayTracingResourceBinder
{
    public sealed class VoxelRayTracingResourceBinder : IVoxelRayTracingResourceBinder
    {
        private readonly IVoxelGpuResourceView _resourceView;
        private readonly VoxelRayTracingResourceBindingLayout _layout;

        public VoxelRayTracingResourceBinder(IVoxelGpuResourceView resourceView)
            : this(resourceView, VoxelRayTracingResourceBindingLayout.Default)
        {
        }

        public VoxelRayTracingResourceBinder(
            IVoxelGpuResourceView resourceView,
            VoxelRayTracingResourceBindingLayout layout)
        {
            _resourceView = resourceView ?? throw new ArgumentNullException(nameof(resourceView));
            _layout = layout;
        }

        public void BindGlobals()
        {
            ValidateBuffers();

            Shader.SetGlobalBuffer(_layout.OccupancyChunkBufferPropertyId, _resourceView.OccupancyChunkBuffer);
            Shader.SetGlobalBuffer(_layout.VoxelDataChunkBufferPropertyId, _resourceView.VoxelDataChunkBuffer);
            Shader.SetGlobalBuffer(_layout.ModelChunkStartBufferPropertyId, _resourceView.ModelChunkStartBuffer);
            Shader.SetGlobalBuffer(_layout.PaletteChunkBufferPropertyId, _resourceView.PaletteChunkBuffer);
            Shader.SetGlobalBuffer(_layout.PaletteChunkStartBufferPropertyId, _resourceView.PaletteChunkStartBuffer);
            Shader.SetGlobalBuffer(_layout.SurfaceTypeTableBufferPropertyId, _resourceView.SurfaceTypeTableBuffer);

            Shader.SetGlobalInteger(_layout.ModelChunkStartStrideBytesPropertyId, checked((int)_resourceView.ModelChunkStartStrideBytes));
            Shader.SetGlobalInteger(_layout.PaletteChunkStartStrideBytesPropertyId, checked((int)_resourceView.PaletteChunkStartStrideBytes));
            Shader.SetGlobalInteger(_layout.SurfaceTypeTableStrideBytesPropertyId, checked((int)_resourceView.SurfaceTypeTableStrideBytes));
            Shader.SetGlobalInteger(_layout.SurfaceTypeEntryCountPropertyId, checked((int)_resourceView.SurfaceTypeEntryCount));
        }

        public void BindGlobals(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            ValidateBuffers();

            commandBuffer.SetGlobalBuffer(_layout.OccupancyChunkBufferPropertyId, _resourceView.OccupancyChunkBuffer);
            commandBuffer.SetGlobalBuffer(_layout.VoxelDataChunkBufferPropertyId, _resourceView.VoxelDataChunkBuffer);
            commandBuffer.SetGlobalBuffer(_layout.ModelChunkStartBufferPropertyId, _resourceView.ModelChunkStartBuffer);
            commandBuffer.SetGlobalBuffer(_layout.PaletteChunkBufferPropertyId, _resourceView.PaletteChunkBuffer);
            commandBuffer.SetGlobalBuffer(_layout.PaletteChunkStartBufferPropertyId, _resourceView.PaletteChunkStartBuffer);
            commandBuffer.SetGlobalBuffer(_layout.SurfaceTypeTableBufferPropertyId, _resourceView.SurfaceTypeTableBuffer);

            commandBuffer.SetGlobalInteger(_layout.ModelChunkStartStrideBytesPropertyId, checked((int)_resourceView.ModelChunkStartStrideBytes));
            commandBuffer.SetGlobalInteger(_layout.PaletteChunkStartStrideBytesPropertyId, checked((int)_resourceView.PaletteChunkStartStrideBytes));
            commandBuffer.SetGlobalInteger(_layout.SurfaceTypeTableStrideBytesPropertyId, checked((int)_resourceView.SurfaceTypeTableStrideBytes));
            commandBuffer.SetGlobalInteger(_layout.SurfaceTypeEntryCountPropertyId, checked((int)_resourceView.SurfaceTypeEntryCount));
        }

        public void BindRayTracingShader(RayTracingShader rayTracingShader)
        {
            if (rayTracingShader == null)
            {
                throw new ArgumentNullException(nameof(rayTracingShader));
            }

            ValidateBuffers();

            rayTracingShader.SetBuffer(_layout.OccupancyChunkBufferPropertyId, _resourceView.OccupancyChunkBuffer);
            rayTracingShader.SetBuffer(_layout.VoxelDataChunkBufferPropertyId, _resourceView.VoxelDataChunkBuffer);
            rayTracingShader.SetBuffer(_layout.ModelChunkStartBufferPropertyId, _resourceView.ModelChunkStartBuffer);
            rayTracingShader.SetBuffer(_layout.PaletteChunkBufferPropertyId, _resourceView.PaletteChunkBuffer);
            rayTracingShader.SetBuffer(_layout.PaletteChunkStartBufferPropertyId, _resourceView.PaletteChunkStartBuffer);
            rayTracingShader.SetBuffer(_layout.SurfaceTypeTableBufferPropertyId, _resourceView.SurfaceTypeTableBuffer);

            rayTracingShader.SetInt(_layout.ModelChunkStartStrideBytesPropertyId, checked((int)_resourceView.ModelChunkStartStrideBytes));
            rayTracingShader.SetInt(_layout.PaletteChunkStartStrideBytesPropertyId, checked((int)_resourceView.PaletteChunkStartStrideBytes));
            rayTracingShader.SetInt(_layout.SurfaceTypeTableStrideBytesPropertyId, checked((int)_resourceView.SurfaceTypeTableStrideBytes));
            rayTracingShader.SetInt(_layout.SurfaceTypeEntryCountPropertyId, checked((int)_resourceView.SurfaceTypeEntryCount));
        }

        public void BindRayTracingShader(CommandBuffer commandBuffer, RayTracingShader rayTracingShader)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            if (rayTracingShader == null)
            {
                throw new ArgumentNullException(nameof(rayTracingShader));
            }

            ValidateBuffers();

            commandBuffer.SetRayTracingBufferParam(rayTracingShader, _layout.OccupancyChunkBufferPropertyId, _resourceView.OccupancyChunkBuffer);
            commandBuffer.SetRayTracingBufferParam(rayTracingShader, _layout.VoxelDataChunkBufferPropertyId, _resourceView.VoxelDataChunkBuffer);
            commandBuffer.SetRayTracingBufferParam(rayTracingShader, _layout.ModelChunkStartBufferPropertyId, _resourceView.ModelChunkStartBuffer);
            commandBuffer.SetRayTracingBufferParam(rayTracingShader, _layout.PaletteChunkBufferPropertyId, _resourceView.PaletteChunkBuffer);
            commandBuffer.SetRayTracingBufferParam(rayTracingShader, _layout.PaletteChunkStartBufferPropertyId, _resourceView.PaletteChunkStartBuffer);
            commandBuffer.SetRayTracingBufferParam(rayTracingShader, _layout.SurfaceTypeTableBufferPropertyId, _resourceView.SurfaceTypeTableBuffer);

            commandBuffer.SetGlobalInteger(_layout.ModelChunkStartStrideBytesPropertyId, checked((int)_resourceView.ModelChunkStartStrideBytes));
            commandBuffer.SetGlobalInteger(_layout.PaletteChunkStartStrideBytesPropertyId, checked((int)_resourceView.PaletteChunkStartStrideBytes));
            commandBuffer.SetGlobalInteger(_layout.SurfaceTypeTableStrideBytesPropertyId, checked((int)_resourceView.SurfaceTypeTableStrideBytes));
            commandBuffer.SetGlobalInteger(_layout.SurfaceTypeEntryCountPropertyId, checked((int)_resourceView.SurfaceTypeEntryCount));
        }

        private void ValidateBuffers()
        {
            if (_resourceView.OccupancyChunkBuffer == null)
            {
                throw new InvalidOperationException("Voxel occupancy chunk buffer is not available.");
            }

            if (_resourceView.VoxelDataChunkBuffer == null)
            {
                throw new InvalidOperationException("Voxel data chunk buffer is not available.");
            }

            if (_resourceView.ModelChunkStartBuffer == null)
            {
                throw new InvalidOperationException("Voxel model chunk start buffer is not available.");
            }

            if (_resourceView.PaletteChunkBuffer == null)
            {
                throw new InvalidOperationException("Voxel palette chunk buffer is not available.");
            }

            if (_resourceView.PaletteChunkStartBuffer == null)
            {
                throw new InvalidOperationException("Voxel palette chunk start buffer is not available.");
            }

            if (_resourceView.SurfaceTypeTableBuffer == null)
            {
                throw new InvalidOperationException("Voxel surface type table buffer is not available.");
            }
        }
    }
}
