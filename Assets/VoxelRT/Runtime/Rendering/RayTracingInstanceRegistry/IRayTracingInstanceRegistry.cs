using System;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RayTracingGeometryProvider;

namespace VoxelRT.Runtime.Rendering.RayTracingInstanceRegistry
{
    public interface IRayTracingInstanceRegistry : IDisposable
    {
        RayTracingAccelerationStructure AccelerationStructure { get; }

        bool HasPendingBuild { get; }

        int RegisterInstance(
            IRayTracingGeometryProvider geometryProvider,
            int sharedGeometryId,
            in RayTracingSceneInstanceRegistration registration);

        void UnregisterInstance(int sceneInstanceId);

        void Clear();

        void UpdateInstanceTransform(int sceneInstanceId, Matrix4x4 localToWorld);

        void UpdateInstanceMask(int sceneInstanceId, uint mask);

        void UpdateInstanceShaderId(int sceneInstanceId, uint shaderInstanceId);

        void UpdateInstanceLayer(int sceneInstanceId, int layer);

        void UpdateInstancePropertyBlock(int sceneInstanceId, MaterialPropertyBlock materialProperties);

        void RebindSharedGeometry(int sceneInstanceId, IRayTracingGeometryProvider geometryProvider, int sharedGeometryId);

        void Build();

        void Build(Vector3 relativeOrigin);

        void Build(CommandBuffer commandBuffer);

        void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin);
    }
}
