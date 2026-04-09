using UnityEngine;

namespace VoxelRT.Runtime.Rendering.RayTracingInstanceRegistry
{
    public struct RayTracingSceneInstanceRegistration
    {
        public RayTracingSceneInstanceRegistration(Matrix4x4 localToWorld, uint shaderInstanceId)
        {
            LocalToWorld = localToWorld;
            PreviousLocalToWorld = null;
            ShaderInstanceId = shaderInstanceId;
            Mask = 0xFFu;
            Layer = 0;
            MaterialProperties = null;
        }

        public Matrix4x4 LocalToWorld { get; set; }

        public Matrix4x4? PreviousLocalToWorld { get; set; }

        public uint ShaderInstanceId { get; set; }

        public uint Mask { get; set; }

        public int Layer { get; set; }

        public MaterialPropertyBlock MaterialProperties { get; set; }
    }
}
