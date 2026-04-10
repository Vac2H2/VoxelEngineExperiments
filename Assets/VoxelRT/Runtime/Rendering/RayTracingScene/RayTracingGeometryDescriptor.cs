using System;

namespace VoxelRT.Runtime.Rendering.RayTracingScene
{
    public readonly struct RayTracingGeometryDescriptor
    {
        private RayTracingGeometryDescriptor(
            RayTracingGeometryKind kind,
            RayTracingMeshGeometryDescriptor mesh,
            RayTracingProceduralGeometryDescriptor procedural)
        {
            Kind = kind;
            Mesh = mesh;
            Procedural = procedural;
        }

        public RayTracingGeometryKind Kind { get; }

        public RayTracingMeshGeometryDescriptor Mesh { get; }

        public RayTracingProceduralGeometryDescriptor Procedural { get; }

        public static RayTracingGeometryDescriptor FromMesh(RayTracingMeshGeometryDescriptor mesh)
        {
            return new RayTracingGeometryDescriptor(RayTracingGeometryKind.Mesh, mesh, default);
        }

        public static RayTracingGeometryDescriptor FromProcedural(RayTracingProceduralGeometryDescriptor procedural)
        {
            return new RayTracingGeometryDescriptor(RayTracingGeometryKind.Procedural, default, procedural);
        }

        public RayTracingMeshGeometryDescriptor RequireMesh()
        {
            if (Kind != RayTracingGeometryKind.Mesh)
            {
                throw new InvalidOperationException("This geometry descriptor does not contain mesh geometry.");
            }

            return Mesh;
        }

        public RayTracingProceduralGeometryDescriptor RequireProcedural()
        {
            if (Kind != RayTracingGeometryKind.Procedural)
            {
                throw new InvalidOperationException("This geometry descriptor does not contain procedural geometry.");
            }

            return Procedural;
        }
    }
}
