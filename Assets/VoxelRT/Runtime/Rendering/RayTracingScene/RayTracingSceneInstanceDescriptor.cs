using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
namespace VoxelRT.Runtime.Rendering.RayTracingScene
{
    public struct RayTracingSceneInstanceDescriptor
    {
        public RayTracingSceneInstanceDescriptor(
            RayTracingGeometryDescriptor geometry,
            Matrix4x4 localToWorld,
            uint shaderInstanceId)
        {
            Geometry = geometry;
            LocalToWorld = localToWorld;
            PreviousLocalToWorld = null;
            ShaderInstanceId = shaderInstanceId;
            Mask = 0xFFu;
            Layer = 0;
            MaterialProperties = null;
        }

        public RayTracingGeometryDescriptor Geometry { get; }

        public Matrix4x4 LocalToWorld { get; set; }

        public Matrix4x4? PreviousLocalToWorld { get; set; }

        public uint ShaderInstanceId { get; set; }

        public uint Mask { get; set; }

        public int Layer { get; set; }

        public MaterialPropertyBlock MaterialProperties { get; set; }

        internal RayTracingMeshInstanceConfig CreateMeshConfig()
        {
            RayTracingMeshGeometryDescriptor mesh = Geometry.RequireMesh();

            return new RayTracingMeshInstanceConfig
            {
                mesh = mesh.Mesh,
                material = mesh.Material,
                materialProperties = MaterialProperties ?? mesh.MaterialProperties,
                enableTriangleCulling = mesh.EnableTriangleCulling,
                frontTriangleCounterClockwise = mesh.FrontTriangleCounterClockwise,
                layer = Layer,
                lightProbeProxyVolume = mesh.LightProbeProxyVolume,
                lightProbeUsage = mesh.LightProbeUsage,
                mask = Mask,
                meshLod = mesh.MeshLod,
                motionVectorMode = mesh.MotionVectorMode,
                renderingLayerMask = mesh.RenderingLayerMask,
                subMeshFlags = mesh.SubMeshFlags,
                subMeshIndex = mesh.SubMeshIndex,
                rayTracingMode = mesh.RayTracingMode,
                accelerationStructureBuildFlagsOverride = mesh.OverrideBuildFlags,
                accelerationStructureBuildFlags = mesh.BuildFlags,
            };
        }

        internal RayTracingAABBsInstanceConfig CreateProceduralConfig()
        {
            RayTracingProceduralGeometryDescriptor procedural = Geometry.RequireProcedural();

            return new RayTracingAABBsInstanceConfig
            {
                aabbBuffer = procedural.AabbBuffer,
                aabbCount = procedural.AabbCount,
                aabbOffset = procedural.AabbOffset,
                material = procedural.Material,
                materialProperties = MaterialProperties ?? procedural.MaterialProperties,
                layer = Layer,
                mask = Mask,
                opaqueMaterial = procedural.OpaqueMaterial,
                dynamicGeometry = procedural.DynamicGeometry,
                accelerationStructureBuildFlagsOverride = procedural.OverrideBuildFlags,
                accelerationStructureBuildFlags = procedural.BuildFlags,
            };
        }

        internal void Validate()
        {
            switch (Geometry.Kind)
            {
                case RayTracingGeometryKind.Mesh:
                    {
                        RayTracingMeshGeometryDescriptor mesh = Geometry.RequireMesh();
                        if (mesh.Mesh == null)
                        {
                            throw new ArgumentNullException(nameof(mesh.Mesh));
                        }

                        if (mesh.Material == null)
                        {
                            throw new ArgumentNullException(nameof(mesh.Material));
                        }

                        return;
                    }

                case RayTracingGeometryKind.Procedural:
                    {
                        RayTracingProceduralGeometryDescriptor procedural = Geometry.RequireProcedural();
                        if (procedural.AabbBuffer == null)
                        {
                            throw new ArgumentNullException(nameof(procedural.AabbBuffer));
                        }

                        if (procedural.AabbCount <= 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(procedural.AabbCount), "AABB count must be greater than zero.");
                        }

                        if (procedural.Material == null)
                        {
                            throw new ArgumentNullException(nameof(procedural.Material));
                        }

                        return;
                    }

                default:
                    throw new InvalidOperationException($"Unsupported geometry kind {Geometry.Kind}.");
            }
        }
    }
}
