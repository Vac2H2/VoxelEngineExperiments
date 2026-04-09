using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingGeometryProvider
{
    public readonly struct RayTracingMeshGeometryDescriptor
    {
        public RayTracingMeshGeometryDescriptor(
            Mesh mesh,
            Material material,
            MaterialPropertyBlock materialProperties,
            RayTracingMode rayTracingMode,
            uint renderingLayerMask,
            bool enableTriangleCulling,
            bool frontTriangleCounterClockwise,
            uint subMeshIndex,
            RayTracingSubMeshFlags subMeshFlags,
            int meshLod,
            MotionVectorGenerationMode motionVectorMode,
            LightProbeUsage lightProbeUsage,
            LightProbeProxyVolume lightProbeProxyVolume,
            bool overrideBuildFlags,
            RayTracingAccelerationStructureBuildFlags buildFlags)
        {
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            Material = material ?? throw new ArgumentNullException(nameof(material));
            MaterialProperties = materialProperties;
            RayTracingMode = rayTracingMode;
            RenderingLayerMask = renderingLayerMask;
            EnableTriangleCulling = enableTriangleCulling;
            FrontTriangleCounterClockwise = frontTriangleCounterClockwise;
            SubMeshIndex = subMeshIndex;
            SubMeshFlags = subMeshFlags;
            MeshLod = meshLod;
            MotionVectorMode = motionVectorMode;
            LightProbeUsage = lightProbeUsage;
            LightProbeProxyVolume = lightProbeProxyVolume;
            OverrideBuildFlags = overrideBuildFlags;
            BuildFlags = buildFlags;
        }

        public Mesh Mesh { get; }

        public Material Material { get; }

        public MaterialPropertyBlock MaterialProperties { get; }

        public RayTracingMode RayTracingMode { get; }

        public uint RenderingLayerMask { get; }

        public bool EnableTriangleCulling { get; }

        public bool FrontTriangleCounterClockwise { get; }

        public uint SubMeshIndex { get; }

        public RayTracingSubMeshFlags SubMeshFlags { get; }

        public int MeshLod { get; }

        public MotionVectorGenerationMode MotionVectorMode { get; }

        public LightProbeUsage LightProbeUsage { get; }

        public LightProbeProxyVolume LightProbeProxyVolume { get; }

        public bool OverrideBuildFlags { get; }

        public RayTracingAccelerationStructureBuildFlags BuildFlags { get; }
    }
}
