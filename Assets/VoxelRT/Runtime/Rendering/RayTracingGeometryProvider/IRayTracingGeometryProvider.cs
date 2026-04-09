namespace VoxelRT.Runtime.Rendering.RayTracingGeometryProvider
{
    public interface IRayTracingGeometryProvider
    {
        RayTracingGeometryKind GeometryKind { get; }

        bool TryGetGeometryDescriptor(int sharedGeometryId, out RayTracingGeometryDescriptor descriptor);
    }
}
