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

        public int AddInstance(in RayTracingSceneInstanceDescriptor descriptor)
        {
            EnsureNotDisposed();
            descriptor.Validate();

            int handle;
            switch (descriptor.Geometry.Kind)
            {
                case RayTracingGeometryKind.Mesh:
                    handle = _accelerationStructure.AddInstance(
                        descriptor.CreateMeshConfig(),
                        descriptor.LocalToWorld,
                        descriptor.PreviousLocalToWorld,
                        descriptor.ShaderInstanceId);
                    break;

                case RayTracingGeometryKind.Procedural:
                    handle = _accelerationStructure.AddInstance(
                        descriptor.CreateProceduralConfig(),
                        descriptor.LocalToWorld,
                        descriptor.ShaderInstanceId);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported geometry kind {descriptor.Geometry.Kind}.");
            }

            HasPendingBuild = true;
            return ValidateHandle(handle);
        }

        public int RecreateInstance(int handle, in RayTracingSceneInstanceDescriptor descriptor)
        {
            EnsureNotDisposed();
            int newHandle = AddInstance(in descriptor);
            RemoveInstance(handle);
            return newHandle;
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

        public void UpdateShaderId(int handle, uint shaderInstanceId)
        {
            EnsureNotDisposed();
            _accelerationStructure.UpdateInstanceID(ValidateHandle(handle), shaderInstanceId);
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
            if (handle < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), "RTAS instance handle must be non-negative.");
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
    }
}
