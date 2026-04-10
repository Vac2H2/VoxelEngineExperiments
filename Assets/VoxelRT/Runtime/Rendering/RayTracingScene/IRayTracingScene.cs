using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingScene
{
    public interface IRayTracingScene : IDisposable
    {
        RayTracingAccelerationStructure AccelerationStructure { get; }

        bool HasPendingBuild { get; }

        int AddInstance(in RayTracingSceneInstanceDescriptor descriptor);

        int RecreateInstance(int handle, in RayTracingSceneInstanceDescriptor descriptor);

        void RemoveInstance(int handle);

        void Clear();

        void UpdateTransform(int handle, Matrix4x4 localToWorld);

        void UpdateMask(int handle, uint mask);

        void UpdateShaderId(int handle, uint shaderInstanceId);

        void UpdateMaterialPropertyBlock(int handle, MaterialPropertyBlock materialProperties);

        void MarkGeometryDirty(int handle);

        void Build();

        void Build(Vector3 relativeOrigin);

        void Build(CommandBuffer commandBuffer);

        void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin);
    }
}
