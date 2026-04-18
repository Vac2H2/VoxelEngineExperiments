using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.VoxelRayTracingResourceBinder
{
    public interface IVoxelRayTracingResourceBinder
    {
        void BindGlobals();

        void BindGlobals(CommandBuffer commandBuffer);

        void BindRayTracingShader(RayTracingShader rayTracingShader);

        void BindRayTracingShader(CommandBuffer commandBuffer, RayTracingShader rayTracingShader);
    }
}
