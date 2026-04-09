using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RayTracingGeometryProvider;
using VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem;

namespace VoxelRT.Runtime.Rendering.VoxelProceduralGeometryProvider
{
    public sealed class VoxelProceduralGeometryProvider : IRayTracingGeometryProvider
    {
        private readonly IVoxelGpuResourceView _resourceView;
        private readonly Material _material;

        public VoxelProceduralGeometryProvider(
            IVoxelGpuResourceView resourceView,
            Material material)
        {
            _resourceView = resourceView ?? throw new ArgumentNullException(nameof(resourceView));
            _material = material ?? throw new ArgumentNullException(nameof(material));
        }

        public RayTracingGeometryKind GeometryKind => RayTracingGeometryKind.Procedural;

        public bool TryGetGeometryDescriptor(int sharedGeometryId, out RayTracingGeometryDescriptor descriptor)
        {
            try
            {
                VoxelModelResourceDescriptor modelResources = _resourceView.GetModelResourceDescriptor(sharedGeometryId);
                RayTracingProceduralGeometryDescriptor procedural = new RayTracingProceduralGeometryDescriptor(
                    modelResources.ProceduralAabbBuffer,
                    modelResources.ProceduralAabbCount,
                    0u,
                    _material,
                    null,
                    true,
                    false,
                    false,
                    RayTracingAccelerationStructureBuildFlags.None);

                descriptor = RayTracingGeometryDescriptor.FromProcedural(procedural);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (KeyNotFoundException)
            {
            }

            descriptor = default;
            return false;
        }
    }
}
