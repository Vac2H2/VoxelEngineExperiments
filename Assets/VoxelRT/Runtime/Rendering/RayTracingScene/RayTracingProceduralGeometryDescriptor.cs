using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingScene
{
    public readonly struct RayTracingProceduralGeometryDescriptor
    {
        public RayTracingProceduralGeometryDescriptor(
            GraphicsBuffer aabbBuffer,
            int aabbCount,
            uint aabbOffset,
            Material material,
            MaterialPropertyBlock materialProperties,
            bool opaqueMaterial,
            bool dynamicGeometry,
            bool overrideBuildFlags,
            RayTracingAccelerationStructureBuildFlags buildFlags)
        {
            if (aabbCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(aabbCount), "AABB count must be greater than zero.");
            }

            AabbBuffer = aabbBuffer ?? throw new ArgumentNullException(nameof(aabbBuffer));
            AabbCount = aabbCount;
            AabbOffset = aabbOffset;
            Material = material ?? throw new ArgumentNullException(nameof(material));
            MaterialProperties = materialProperties;
            OpaqueMaterial = opaqueMaterial;
            DynamicGeometry = dynamicGeometry;
            OverrideBuildFlags = overrideBuildFlags;
            BuildFlags = buildFlags;
        }

        public GraphicsBuffer AabbBuffer { get; }

        public int AabbCount { get; }

        public uint AabbOffset { get; }

        public Material Material { get; }

        public MaterialPropertyBlock MaterialProperties { get; }

        public bool OpaqueMaterial { get; }

        public bool DynamicGeometry { get; }

        public bool OverrideBuildFlags { get; }

        public RayTracingAccelerationStructureBuildFlags BuildFlags { get; }
    }
}
