using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingSceneService
{
    public struct RayTracingProceduralInstanceRegistration
    {
        public RayTracingProceduralInstanceRegistration(
            GraphicsBuffer aabbBuffer,
            int aabbCount,
            Material material,
            Matrix4x4 localToWorld,
            uint shaderInstanceId)
        {
            AabbBuffer = aabbBuffer ?? throw new ArgumentNullException(nameof(aabbBuffer));
            AabbCount = aabbCount;
            AabbOffset = 0;
            Material = material ?? throw new ArgumentNullException(nameof(material));
            MaterialProperties = null;
            LocalToWorld = localToWorld;
            ShaderInstanceId = shaderInstanceId;
            Mask = 0xFFu;
            Layer = 0;
            OpaqueMaterial = true;
            DynamicGeometry = false;
            OverrideBuildFlags = false;
            BuildFlags = RayTracingAccelerationStructureBuildFlags.None;
        }

        public GraphicsBuffer AabbBuffer { get; set; }

        public int AabbCount { get; set; }

        public uint AabbOffset { get; set; }

        public Material Material { get; set; }

        public MaterialPropertyBlock MaterialProperties { get; set; }

        public Matrix4x4 LocalToWorld { get; set; }

        public uint ShaderInstanceId { get; set; }

        public uint Mask { get; set; }

        public int Layer { get; set; }

        public bool OpaqueMaterial { get; set; }

        public bool DynamicGeometry { get; set; }

        public bool OverrideBuildFlags { get; set; }

        public RayTracingAccelerationStructureBuildFlags BuildFlags { get; set; }
    }
}
