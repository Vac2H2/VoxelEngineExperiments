using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingSceneService
{
    public interface IRayTracingSceneService : IDisposable
    {
        RayTracingAccelerationStructure AccelerationStructure { get; }

        bool HasPendingBuild { get; }

        void RegisterMeshInstance(int sceneInstanceId, in RayTracingMeshInstanceRegistration registration);

        void RegisterProceduralInstance(int sceneInstanceId, in RayTracingProceduralInstanceRegistration registration);

        void UnregisterInstance(int sceneInstanceId);

        void Clear();

        void UpdateInstanceTransform(int sceneInstanceId, Matrix4x4 localToWorld);

        void UpdateInstanceMask(int sceneInstanceId, uint mask);

        void UpdateInstanceShaderId(int sceneInstanceId, uint shaderInstanceId);

        void UpdateInstancePropertyBlock(int sceneInstanceId, MaterialPropertyBlock materialProperties);

        void MarkInstanceGeometryDirty(int sceneInstanceId);

        void Build();

        void Build(Vector3 relativeOrigin);

        void Build(CommandBuffer commandBuffer);

        void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin);
    }
}
