using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VoxelRT.Runtime.Rendering.RayTracingSceneService
{
    public sealed class RayTracingSceneService : IRayTracingSceneService
    {
        private readonly Dictionary<int, InstanceRecord> _instances = new Dictionary<int, InstanceRecord>();
        private RayTracingAccelerationStructure _accelerationStructure;

        public RayTracingSceneService()
            : this(RayTracingSceneServiceConfiguration.Default)
        {
        }

        public RayTracingSceneService(RayTracingSceneServiceConfiguration configuration)
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

        public void RegisterMeshInstance(int sceneInstanceId, in RayTracingMeshInstanceRegistration registration)
        {
            EnsureNotDisposed();
            ValidateSceneInstanceId(sceneInstanceId);
            ValidateMeshRegistration(in registration);

            if (_instances.ContainsKey(sceneInstanceId))
            {
                throw new InvalidOperationException($"Scene instance {sceneInstanceId} is already registered.");
            }

            RayTracingMeshInstanceConfig config = CreateMeshConfig(in registration);
            int handle = _accelerationStructure.AddInstance(
                config,
                registration.LocalToWorld,
                registration.PreviousLocalToWorld,
                registration.ShaderInstanceId);

            RegisterHandle(sceneInstanceId, handle, InstanceKind.Mesh, registration.ShaderInstanceId, registration.Mask);
        }

        public void RegisterProceduralInstance(int sceneInstanceId, in RayTracingProceduralInstanceRegistration registration)
        {
            EnsureNotDisposed();
            ValidateSceneInstanceId(sceneInstanceId);
            ValidateProceduralRegistration(in registration);

            if (_instances.ContainsKey(sceneInstanceId))
            {
                throw new InvalidOperationException($"Scene instance {sceneInstanceId} is already registered.");
            }

            RayTracingAABBsInstanceConfig config = CreateProceduralConfig(in registration);
            int handle = _accelerationStructure.AddInstance(
                config,
                registration.LocalToWorld,
                registration.ShaderInstanceId);

            RegisterHandle(sceneInstanceId, handle, InstanceKind.Procedural, registration.ShaderInstanceId, registration.Mask);
        }

        public void UnregisterInstance(int sceneInstanceId)
        {
            EnsureNotDisposed();
            InstanceRecord record = GetInstanceRecord(sceneInstanceId);
            _accelerationStructure.RemoveInstance(record.Handle);
            _instances.Remove(sceneInstanceId);
            HasPendingBuild = true;
        }

        public void Clear()
        {
            EnsureNotDisposed();
            _accelerationStructure.ClearInstances();
            _instances.Clear();
            HasPendingBuild = true;
        }

        public void UpdateInstanceTransform(int sceneInstanceId, Matrix4x4 localToWorld)
        {
            EnsureNotDisposed();
            InstanceRecord record = GetInstanceRecord(sceneInstanceId);
            _accelerationStructure.UpdateInstanceTransform(record.Handle, localToWorld);
            HasPendingBuild = true;
        }

        public void UpdateInstanceMask(int sceneInstanceId, uint mask)
        {
            EnsureNotDisposed();
            InstanceRecord record = GetInstanceRecord(sceneInstanceId);
            _accelerationStructure.UpdateInstanceMask(record.Handle, mask);
            record.Mask = mask;
            HasPendingBuild = true;
        }

        public void UpdateInstanceShaderId(int sceneInstanceId, uint shaderInstanceId)
        {
            EnsureNotDisposed();
            InstanceRecord record = GetInstanceRecord(sceneInstanceId);
            _accelerationStructure.UpdateInstanceID(record.Handle, shaderInstanceId);
            record.ShaderInstanceId = shaderInstanceId;
            HasPendingBuild = true;
        }

        public void UpdateInstancePropertyBlock(int sceneInstanceId, MaterialPropertyBlock materialProperties)
        {
            EnsureNotDisposed();
            InstanceRecord record = GetInstanceRecord(sceneInstanceId);
            _accelerationStructure.UpdateInstancePropertyBlock(record.Handle, materialProperties);
            HasPendingBuild = true;
        }

        public void MarkInstanceGeometryDirty(int sceneInstanceId)
        {
            EnsureNotDisposed();
            InstanceRecord record = GetInstanceRecord(sceneInstanceId);
            _accelerationStructure.UpdateInstanceGeometry(record.Handle);
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
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            commandBuffer.BuildRayTracingAccelerationStructure(_accelerationStructure);
            HasPendingBuild = false;
        }

        public void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin)
        {
            EnsureNotDisposed();
            if (commandBuffer == null)
            {
                throw new ArgumentNullException(nameof(commandBuffer));
            }

            commandBuffer.BuildRayTracingAccelerationStructure(_accelerationStructure, relativeOrigin);
            HasPendingBuild = false;
        }

        public void Dispose()
        {
            _accelerationStructure?.Dispose();
            _accelerationStructure = null;
            _instances.Clear();
            HasPendingBuild = false;
        }

        private static RayTracingMeshInstanceConfig CreateMeshConfig(in RayTracingMeshInstanceRegistration registration)
        {
            RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig
            {
                mesh = registration.Mesh,
                material = registration.Material,
                materialProperties = registration.MaterialProperties,
                enableTriangleCulling = registration.EnableTriangleCulling,
                frontTriangleCounterClockwise = registration.FrontTriangleCounterClockwise,
                layer = registration.Layer,
                lightProbeProxyVolume = registration.LightProbeProxyVolume,
                lightProbeUsage = registration.LightProbeUsage,
                mask = registration.Mask,
                meshLod = registration.MeshLod,
                motionVectorMode = registration.MotionVectorMode,
                renderingLayerMask = registration.RenderingLayerMask,
                subMeshFlags = registration.SubMeshFlags,
                subMeshIndex = registration.SubMeshIndex,
            };

            config.rayTracingMode = registration.RayTracingMode;
            config.accelerationStructureBuildFlagsOverride = registration.OverrideBuildFlags;
            config.accelerationStructureBuildFlags = registration.BuildFlags;
            return config;
        }

        private static RayTracingAABBsInstanceConfig CreateProceduralConfig(in RayTracingProceduralInstanceRegistration registration)
        {
            RayTracingAABBsInstanceConfig config = new RayTracingAABBsInstanceConfig
            {
                aabbBuffer = registration.AabbBuffer,
                aabbCount = registration.AabbCount,
                aabbOffset = registration.AabbOffset,
                material = registration.Material,
                materialProperties = registration.MaterialProperties,
                layer = registration.Layer,
                mask = registration.Mask,
                opaqueMaterial = registration.OpaqueMaterial,
            };

            config.dynamicGeometry = registration.DynamicGeometry;
            config.accelerationStructureBuildFlagsOverride = registration.OverrideBuildFlags;
            config.accelerationStructureBuildFlags = registration.BuildFlags;
            return config;
        }

        private static void ValidateSceneInstanceId(int sceneInstanceId)
        {
            if (sceneInstanceId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sceneInstanceId), "Scene instance id must be non-negative.");
            }
        }

        private static void ValidateMeshRegistration(in RayTracingMeshInstanceRegistration registration)
        {
            if (registration.Mesh == null)
            {
                throw new ArgumentNullException(nameof(registration.Mesh));
            }

            if (registration.Material == null)
            {
                throw new ArgumentNullException(nameof(registration.Material));
            }
        }

        private static void ValidateProceduralRegistration(in RayTracingProceduralInstanceRegistration registration)
        {
            if (registration.AabbBuffer == null)
            {
                throw new ArgumentNullException(nameof(registration.AabbBuffer));
            }

            if (registration.AabbCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(registration.AabbCount), "AABB count must be greater than zero.");
            }

            if (registration.Material == null)
            {
                throw new ArgumentNullException(nameof(registration.Material));
            }
        }

        private void RegisterHandle(
            int sceneInstanceId,
            int handle,
            InstanceKind kind,
            uint shaderInstanceId,
            uint mask)
        {
            if (handle == 0)
            {
                throw new InvalidOperationException($"Failed to add scene instance {sceneInstanceId} to the ray tracing acceleration structure.");
            }

            _instances.Add(sceneInstanceId, new InstanceRecord(handle, kind, shaderInstanceId, mask));
            HasPendingBuild = true;
        }

        private InstanceRecord GetInstanceRecord(int sceneInstanceId)
        {
            ValidateSceneInstanceId(sceneInstanceId);

            if (_instances.TryGetValue(sceneInstanceId, out InstanceRecord record))
            {
                return record;
            }

            throw new KeyNotFoundException($"Scene instance {sceneInstanceId} is not registered.");
        }

        private void EnsureNotDisposed()
        {
            if (_accelerationStructure == null)
            {
                throw new ObjectDisposedException(nameof(RayTracingSceneService));
            }
        }

        private enum InstanceKind
        {
            Mesh,
            Procedural,
        }

        private sealed class InstanceRecord
        {
            public InstanceRecord(int handle, InstanceKind kind, uint shaderInstanceId, uint mask)
            {
                Handle = handle;
                Kind = kind;
                ShaderInstanceId = shaderInstanceId;
                Mask = mask;
            }

            public int Handle { get; }

            public InstanceKind Kind { get; }

            public uint ShaderInstanceId { get; set; }

            public uint Mask { get; set; }
        }
    }
}
