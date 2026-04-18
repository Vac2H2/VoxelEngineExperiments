using System;
using UnityEngine;

namespace VoxelExperiments.Runtime.Rendering.VoxelGpuResourceSystem
{
    public readonly struct VoxelModelResourceDescriptor
    {
        public VoxelModelResourceDescriptor(
            int modelResidencyId,
            uint chunkCount,
            GraphicsBuffer proceduralAabbBuffer,
            int proceduralAabbCount)
        {
            if (proceduralAabbCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(proceduralAabbCount), "Procedural AABB count must be greater than zero.");
            }

            ModelResidencyId = modelResidencyId;
            ChunkCount = chunkCount;
            ProceduralAabbBuffer = proceduralAabbBuffer ?? throw new ArgumentNullException(nameof(proceduralAabbBuffer));
            ProceduralAabbCount = proceduralAabbCount;
        }

        public int ModelResidencyId { get; }

        public uint ChunkCount { get; }

        public GraphicsBuffer ProceduralAabbBuffer { get; }

        public int ProceduralAabbCount { get; }
    }
}
