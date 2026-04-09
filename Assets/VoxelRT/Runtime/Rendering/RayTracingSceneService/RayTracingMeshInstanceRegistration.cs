using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingSceneService
{
    public struct RayTracingMeshInstanceRegistration
    {
        public RayTracingMeshInstanceRegistration(
            Mesh mesh,
            Material material,
            Matrix4x4 localToWorld,
            uint shaderInstanceId)
        {
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            Material = material ?? throw new ArgumentNullException(nameof(material));
            LocalToWorld = localToWorld;
            PreviousLocalToWorld = null;
            ShaderInstanceId = shaderInstanceId;
            Mask = 0xFFu;
            Layer = 0;
            RenderingLayerMask = 1u;
            EnableTriangleCulling = false;
            FrontTriangleCounterClockwise = false;
            RayTracingMode = RayTracingMode.Static;
            SubMeshIndex = 0u;
            SubMeshFlags = RayTracingSubMeshFlags.Enabled;
            MeshLod = -1;
            MotionVectorMode = MotionVectorGenerationMode.Object;
            LightProbeUsage = LightProbeUsage.Off;
            LightProbeProxyVolume = null;
            MaterialProperties = null;
            OverrideBuildFlags = false;
            BuildFlags = RayTracingAccelerationStructureBuildFlags.None;
        }

        public Mesh Mesh { get; set; }

        public Material Material { get; set; }

        public MaterialPropertyBlock MaterialProperties { get; set; }

        public Matrix4x4 LocalToWorld { get; set; }

        public Matrix4x4? PreviousLocalToWorld { get; set; }

        public uint ShaderInstanceId { get; set; }

        public uint Mask { get; set; }

        public int Layer { get; set; }

        public uint RenderingLayerMask { get; set; }

        public bool EnableTriangleCulling { get; set; }

        public bool FrontTriangleCounterClockwise { get; set; }

        public RayTracingMode RayTracingMode { get; set; }

        public uint SubMeshIndex { get; set; }

        public RayTracingSubMeshFlags SubMeshFlags { get; set; }

        public int MeshLod { get; set; }

        public MotionVectorGenerationMode MotionVectorMode { get; set; }

        public LightProbeUsage LightProbeUsage { get; set; }

        public LightProbeProxyVolume LightProbeProxyVolume { get; set; }

        public bool OverrideBuildFlags { get; set; }

        public RayTracingAccelerationStructureBuildFlags BuildFlags { get; set; }
    }
}
