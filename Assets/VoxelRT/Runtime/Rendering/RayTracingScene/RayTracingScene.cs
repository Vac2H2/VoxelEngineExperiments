using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace VoxelRT.Runtime.Rendering.RayTracingScene
{
    public sealed class RayTracingScene : IRayTracingScene
    {
        private RayTracingAccelerationStructure _accelerationStructure;

        public RayTracingScene()
            : this(RayTracingSceneConfiguration.Default)
        {
        }

        public RayTracingScene(RayTracingSceneConfiguration configuration)
        {
            _accelerationStructure = new RayTracingAccelerationStructure(configuration.ToUnitySettings());
        }

        public RayTracingAccelerationStructure AccelerationStructure
        {
            get
            {
                EnsureNotDisposed();
                return _accelerationStructure;
            }
        }

        public bool HasPendingBuild { get; private set; }

        public int AddInstance(in RayTracingAABBsInstanceConfig config, Matrix4x4 localToWorld)
        {
            EnsureNotDisposed();
            ValidateProceduralConfig(in config);
            int handle = _accelerationStructure.AddInstance(config, localToWorld, 0u);

            if (handle == 0)
            {
                throw CreateAddInstanceFailure(handle, in config);
            }

            HasPendingBuild = true;
            return handle;
        }

        public void RemoveInstance(int handle)
        {
            EnsureNotDisposed();
            _accelerationStructure.RemoveInstance(ValidateHandle(handle));
            HasPendingBuild = true;
        }

        public void Clear()
        {
            EnsureNotDisposed();
            _accelerationStructure.ClearInstances();
            HasPendingBuild = true;
        }

        public void UpdateTransform(int handle, Matrix4x4 localToWorld)
        {
            EnsureNotDisposed();
            _accelerationStructure.UpdateInstanceTransform(ValidateHandle(handle), localToWorld);
            HasPendingBuild = true;
        }

        public void UpdateMask(int handle, uint mask)
        {
            EnsureNotDisposed();
            _accelerationStructure.UpdateInstanceMask(ValidateHandle(handle), mask);
            HasPendingBuild = true;
        }

        public void UpdateMaterialPropertyBlock(int handle, MaterialPropertyBlock materialProperties)
        {
            EnsureNotDisposed();
            _accelerationStructure.UpdateInstancePropertyBlock(ValidateHandle(handle), materialProperties);
            HasPendingBuild = true;
        }

        public void MarkGeometryDirty(int handle)
        {
            EnsureNotDisposed();
            _accelerationStructure.UpdateInstanceGeometry(ValidateHandle(handle));
            HasPendingBuild = true;
        }

        public void Build()
        {
            EnsureNotDisposed();
            _accelerationStructure.Build();
            HasPendingBuild = false;
        }

        public void Build(Vector3 relativeOrigin)
        {
            EnsureNotDisposed();
            _accelerationStructure.Build(relativeOrigin);
            HasPendingBuild = false;
        }

        public void Build(CommandBuffer commandBuffer)
        {
            EnsureNotDisposed();
            ValidateCommandBuffer(commandBuffer);

            commandBuffer.BuildRayTracingAccelerationStructure(_accelerationStructure);
            HasPendingBuild = false;
        }

        public void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin)
        {
            EnsureNotDisposed();
            ValidateCommandBuffer(commandBuffer);

            commandBuffer.BuildRayTracingAccelerationStructure(_accelerationStructure, relativeOrigin);
            HasPendingBuild = false;
        }

        public void Dispose()
        {
            _accelerationStructure?.Dispose();
            _accelerationStructure = null;
            HasPendingBuild = false;
        }

        private static int ValidateHandle(int handle)
        {
            if (handle == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), "RTAS instance handle must be non-zero.");
            }

            return handle;
        }

        private static void ValidateCommandBuffer(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }
        }

        private void EnsureNotDisposed()
        {
            if (_accelerationStructure == null)
            {
                throw new ObjectDisposedException(nameof(RayTracingScene));
            }
        }

        private static void ValidateProceduralConfig(in RayTracingAABBsInstanceConfig config)
        {
            if (config.aabbBuffer == null)
            {
                throw new ArgumentNullException(nameof(config.aabbBuffer));
            }

            if (config.aabbCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config.aabbCount), "AABB count must be greater than zero.");
            }

            if (config.material == null)
            {
                throw new ArgumentNullException(nameof(config.material));
            }
        }

        private static InvalidOperationException CreateAddInstanceFailure(
            int handle,
            in RayTracingAABBsInstanceConfig config)
        {
            string materialName = config.material != null ? config.material.name : "<null>";
            string shaderName = config.material != null && config.material.shader != null
                ? config.material.shader.name
                : "<null>";

            return new InvalidOperationException(
                "Unity returned 0 while adding a procedural RTAS instance. " +
                $"Handle={handle}, Material='{materialName}', Shader='{shaderName}', AabbCount={config.aabbCount}. " +
                "This wrapper does not know the engine-side reason for the failure. " +
                "Inspect the preceding Unity Console or Editor.log output for Unity's own diagnostics.");
        }
    }
}
