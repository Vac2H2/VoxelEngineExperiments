using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingScene
{
    public interface IRayTracingScene : IDisposable
    {
        RayTracingAccelerationStructure AccelerationStructure { get; }

        bool HasPendingBuild { get; }

        int AddInstance(in RayTracingAABBsInstanceConfig config, Matrix4x4 localToWorld);

        void RemoveInstance(int handle);

        void Clear();

        void UpdateTransform(int handle, Matrix4x4 localToWorld);

        void UpdateMask(int handle, uint mask);

        void UpdateMaterialPropertyBlock(int handle, MaterialPropertyBlock materialProperties);

        void MarkGeometryDirty(int handle);

        void Build();

        void Build(Vector3 relativeOrigin);

        void Build(CommandBuffer commandBuffer);

        void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin);
    }
}
