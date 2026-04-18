using UnityEngine.Rendering;

namespace VoxelExperiments.Runtime.Rendering.RayTracingScene
{
    public readonly struct RayTracingSceneConfiguration
    {
        public RayTracingSceneConfiguration(
            RayTracingAccelerationStructure.RayTracingModeMask rayTracingModeMask,
            int layerMask,
            RayTracingAccelerationStructureBuildFlags staticBuildFlags,
            RayTracingAccelerationStructureBuildFlags dynamicBuildFlags)
        {
            RayTracingModeMask = rayTracingModeMask;
            LayerMask = layerMask;
            StaticBuildFlags = staticBuildFlags;
            DynamicBuildFlags = dynamicBuildFlags;
        }

        public RayTracingAccelerationStructure.RayTracingModeMask RayTracingModeMask { get; }

        public int LayerMask { get; }

        public RayTracingAccelerationStructureBuildFlags StaticBuildFlags { get; }

        public RayTracingAccelerationStructureBuildFlags DynamicBuildFlags { get; }

        public static RayTracingSceneConfiguration Default =>
            new RayTracingSceneConfiguration(
                RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                ~0,
                RayTracingAccelerationStructureBuildFlags.PreferFastTrace,
                RayTracingAccelerationStructureBuildFlags.PreferFastBuild);

        internal RayTracingAccelerationStructure.Settings ToUnitySettings()
        {
            return new RayTracingAccelerationStructure.Settings(
                RayTracingAccelerationStructure.ManagementMode.Manual,
                RayTracingModeMask,
                LayerMask,
                StaticBuildFlags,
                DynamicBuildFlags);
        }
    }
}
