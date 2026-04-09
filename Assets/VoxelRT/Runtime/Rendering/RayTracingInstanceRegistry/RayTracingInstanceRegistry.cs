using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelRT.Runtime.Rendering.RayTracingGeometryProvider;
using VoxelRT.Runtime.Rendering.RayTracingSceneService;

namespace VoxelRT.Runtime.Rendering.RayTracingInstanceRegistry
{
    public sealed class RayTracingInstanceRegistry : IRayTracingInstanceRegistry
    {
        private readonly IRayTracingSceneService _sceneService;
        private readonly bool _ownsSceneService;
        private readonly Dictionary<int, InstanceRecord> _instances = new Dictionary<int, InstanceRecord>();
        private readonly Stack<int> _freeSceneInstanceIds = new Stack<int>();
        private int _nextSceneInstanceId;

        public RayTracingInstanceRegistry()
            : this(new RayTracingSceneService.RayTracingSceneService(), true)
        {
        }

        public RayTracingInstanceRegistry(IRayTracingSceneService sceneService)
            : this(sceneService, false)
        {
        }

        internal RayTracingInstanceRegistry(IRayTracingSceneService sceneService, bool ownsSceneService)
        {
            _sceneService = sceneService ?? throw new ArgumentNullException(nameof(sceneService));
            _ownsSceneService = ownsSceneService;
        }

        public RayTracingAccelerationStructure AccelerationStructure => _sceneService.AccelerationStructure;

        public bool HasPendingBuild => _sceneService.HasPendingBuild;

        public int RegisterInstance(
            IRayTracingGeometryProvider geometryProvider,
            int sharedGeometryId,
            in RayTracingSceneInstanceRegistration registration)
        {
            if (geometryProvider == null)
            {
                throw new ArgumentNullException(nameof(geometryProvider));
            }

            if (sharedGeometryId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sharedGeometryId), "Shared geometry id must be non-negative.");
            }

            RegistrationState state = new RegistrationState(
                geometryProvider,
                sharedGeometryId,
                registration.LocalToWorld,
                registration.PreviousLocalToWorld,
                registration.ShaderInstanceId,
                registration.Mask,
                registration.Layer,
                registration.MaterialProperties);

            RayTracingGeometryDescriptor geometryDescriptor = ResolveDescriptor(geometryProvider, sharedGeometryId);
            int sceneInstanceId = AllocateSceneInstanceId();
            bool committed = false;

            try
            {
                RegisterResolvedInstance(sceneInstanceId, geometryDescriptor, in state);
                committed = true;
                return sceneInstanceId;
            }
            finally
            {
                if (!committed)
                {
                    _freeSceneInstanceIds.Push(sceneInstanceId);
                }
            }
        }

        public void UnregisterInstance(int sceneInstanceId)
        {
            InstanceRecord record = GetRecord(sceneInstanceId);
            _sceneService.UnregisterInstance(sceneInstanceId);
            _instances.Remove(sceneInstanceId);
            _freeSceneInstanceIds.Push(record.SceneInstanceId);
        }

        public void Clear()
        {
            _sceneService.Clear();
            _instances.Clear();
            _freeSceneInstanceIds.Clear();
            _nextSceneInstanceId = 0;
        }

        public void UpdateInstanceTransform(int sceneInstanceId, Matrix4x4 localToWorld)
        {
            InstanceRecord record = GetRecord(sceneInstanceId);
            _sceneService.UpdateInstanceTransform(sceneInstanceId, localToWorld);
            record.PreviousLocalToWorld = record.LocalToWorld;
            record.LocalToWorld = localToWorld;
        }

        public void UpdateInstanceMask(int sceneInstanceId, uint mask)
        {
            InstanceRecord record = GetRecord(sceneInstanceId);
            _sceneService.UpdateInstanceMask(sceneInstanceId, mask);
            record.Mask = mask;
        }

        public void UpdateInstanceShaderId(int sceneInstanceId, uint shaderInstanceId)
        {
            InstanceRecord record = GetRecord(sceneInstanceId);
            _sceneService.UpdateInstanceShaderId(sceneInstanceId, shaderInstanceId);
            record.ShaderInstanceId = shaderInstanceId;
        }

        public void UpdateInstanceLayer(int sceneInstanceId, int layer)
        {
            InstanceRecord record = GetRecord(sceneInstanceId);
            RegistrationState currentState = RegistrationState.FromRecord(record);
            RegistrationState nextState = currentState.WithLayer(layer);
            ReregisterInstance(sceneInstanceId, in currentState, in nextState);
        }

        public void UpdateInstancePropertyBlock(int sceneInstanceId, MaterialPropertyBlock materialProperties)
        {
            InstanceRecord record = GetRecord(sceneInstanceId);
            _sceneService.UpdateInstancePropertyBlock(sceneInstanceId, materialProperties);
            record.MaterialProperties = materialProperties;
        }

        public void RebindSharedGeometry(int sceneInstanceId, IRayTracingGeometryProvider geometryProvider, int sharedGeometryId)
        {
            if (geometryProvider == null)
            {
                throw new ArgumentNullException(nameof(geometryProvider));
            }

            if (sharedGeometryId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sharedGeometryId), "Shared geometry id must be non-negative.");
            }

            InstanceRecord record = GetRecord(sceneInstanceId);
            RegistrationState currentState = RegistrationState.FromRecord(record);
            RegistrationState nextState = currentState.WithSharedGeometry(geometryProvider, sharedGeometryId);
            ReregisterInstance(sceneInstanceId, in currentState, in nextState);
        }

        public void Build()
        {
            _sceneService.Build();
        }

        public void Build(Vector3 relativeOrigin)
        {
            _sceneService.Build(relativeOrigin);
        }

        public void Build(CommandBuffer commandBuffer)
        {
            _sceneService.Build(commandBuffer);
        }

        public void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin)
        {
            _sceneService.Build(commandBuffer, relativeOrigin);
        }

        public void Dispose()
        {
            if (_ownsSceneService)
            {
                _sceneService.Dispose();
            }

            _instances.Clear();
            _freeSceneInstanceIds.Clear();
            _nextSceneInstanceId = 0;
        }

        private int AllocateSceneInstanceId()
        {
            if (_freeSceneInstanceIds.Count > 0)
            {
                return _freeSceneInstanceIds.Pop();
            }

            int sceneInstanceId = _nextSceneInstanceId;
            _nextSceneInstanceId = checked(_nextSceneInstanceId + 1);
            return sceneInstanceId;
        }

        private InstanceRecord GetRecord(int sceneInstanceId)
        {
            if (sceneInstanceId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sceneInstanceId), "Scene instance id must be non-negative.");
            }

            if (_instances.TryGetValue(sceneInstanceId, out InstanceRecord record))
            {
                return record;
            }

            throw new KeyNotFoundException($"Scene instance {sceneInstanceId} is not registered.");
        }

        private static RayTracingGeometryDescriptor ResolveDescriptor(IRayTracingGeometryProvider geometryProvider, int sharedGeometryId)
        {
            if (geometryProvider.TryGetGeometryDescriptor(sharedGeometryId, out RayTracingGeometryDescriptor descriptor))
            {
                return descriptor;
            }

            throw new KeyNotFoundException(
                $"Geometry provider {geometryProvider.GetType().Name} could not resolve shared geometry id {sharedGeometryId}.");
        }

        private void RegisterResolvedInstance(int sceneInstanceId, RayTracingGeometryDescriptor descriptor, in RegistrationState state)
        {
            switch (descriptor.Kind)
            {
                case RayTracingGeometryKind.Mesh:
                    {
                        RayTracingMeshGeometryDescriptor mesh = descriptor.RequireMesh();
                        RayTracingMeshInstanceRegistration meshRegistration = new RayTracingMeshInstanceRegistration(
                            mesh.Mesh,
                            mesh.Material,
                            state.LocalToWorld,
                            state.ShaderInstanceId)
                        {
                            PreviousLocalToWorld = state.PreviousLocalToWorld,
                            MaterialProperties = state.MaterialProperties ?? mesh.MaterialProperties,
                            Mask = state.Mask,
                            Layer = state.Layer,
                            RenderingLayerMask = mesh.RenderingLayerMask,
                            EnableTriangleCulling = mesh.EnableTriangleCulling,
                            FrontTriangleCounterClockwise = mesh.FrontTriangleCounterClockwise,
                            RayTracingMode = mesh.RayTracingMode,
                            SubMeshIndex = mesh.SubMeshIndex,
                            SubMeshFlags = mesh.SubMeshFlags,
                            MeshLod = mesh.MeshLod,
                            MotionVectorMode = mesh.MotionVectorMode,
                            LightProbeUsage = mesh.LightProbeUsage,
                            LightProbeProxyVolume = mesh.LightProbeProxyVolume,
                            OverrideBuildFlags = mesh.OverrideBuildFlags,
                            BuildFlags = mesh.BuildFlags,
                        };

                        _sceneService.RegisterMeshInstance(sceneInstanceId, in meshRegistration);
                        break;
                    }

                case RayTracingGeometryKind.Procedural:
                    {
                        RayTracingProceduralGeometryDescriptor procedural = descriptor.RequireProcedural();
                        RayTracingProceduralInstanceRegistration proceduralRegistration = new RayTracingProceduralInstanceRegistration(
                            procedural.AabbBuffer,
                            procedural.AabbCount,
                            procedural.Material,
                            state.LocalToWorld,
                            state.ShaderInstanceId)
                        {
                            AabbOffset = procedural.AabbOffset,
                            MaterialProperties = state.MaterialProperties ?? procedural.MaterialProperties,
                            Mask = state.Mask,
                            Layer = state.Layer,
                            OpaqueMaterial = procedural.OpaqueMaterial,
                            DynamicGeometry = procedural.DynamicGeometry,
                            OverrideBuildFlags = procedural.OverrideBuildFlags,
                            BuildFlags = procedural.BuildFlags,
                        };

                        _sceneService.RegisterProceduralInstance(sceneInstanceId, in proceduralRegistration);
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unsupported geometry kind {descriptor.Kind}.");
            }

            StoreRecord(sceneInstanceId, descriptor.Kind, in state);
        }

        private void ReregisterInstance(int sceneInstanceId, in RegistrationState currentState, in RegistrationState nextState)
        {
            RayTracingGeometryDescriptor currentDescriptor = ResolveDescriptor(currentState.GeometryProvider, currentState.SharedGeometryId);
            RayTracingGeometryDescriptor nextDescriptor = ResolveDescriptor(nextState.GeometryProvider, nextState.SharedGeometryId);

            _sceneService.UnregisterInstance(sceneInstanceId);

            try
            {
                RegisterResolvedInstance(sceneInstanceId, nextDescriptor, in nextState);
            }
            catch
            {
                try
                {
                    RegisterResolvedInstance(sceneInstanceId, currentDescriptor, in currentState);
                }
                catch
                {
                    _instances.Remove(sceneInstanceId);
                    _freeSceneInstanceIds.Push(sceneInstanceId);
                }

                throw;
            }
        }

        private void StoreRecord(int sceneInstanceId, RayTracingGeometryKind geometryKind, in RegistrationState state)
        {
            if (_instances.TryGetValue(sceneInstanceId, out InstanceRecord existingRecord))
            {
                existingRecord.ApplyState(in state, geometryKind);
                return;
            }

            InstanceRecord record = new InstanceRecord(
                sceneInstanceId,
                state.GeometryProvider,
                state.SharedGeometryId,
                geometryKind,
                state.LocalToWorld,
                state.PreviousLocalToWorld,
                state.ShaderInstanceId,
                state.Mask,
                state.Layer,
                state.MaterialProperties);

            _instances.Add(sceneInstanceId, record);
        }

        private sealed class InstanceRecord
        {
            public InstanceRecord(
                int sceneInstanceId,
                IRayTracingGeometryProvider geometryProvider,
                int sharedGeometryId,
                RayTracingGeometryKind geometryKind,
                Matrix4x4 localToWorld,
                Matrix4x4? previousLocalToWorld,
                uint shaderInstanceId,
                uint mask,
                int layer,
                MaterialPropertyBlock materialProperties)
            {
                SceneInstanceId = sceneInstanceId;
                GeometryProvider = geometryProvider;
                SharedGeometryId = sharedGeometryId;
                GeometryKind = geometryKind;
                LocalToWorld = localToWorld;
                PreviousLocalToWorld = previousLocalToWorld;
                ShaderInstanceId = shaderInstanceId;
                Mask = mask;
                Layer = layer;
                MaterialProperties = materialProperties;
            }

            public int SceneInstanceId { get; }

            public IRayTracingGeometryProvider GeometryProvider { get; set; }

            public int SharedGeometryId { get; set; }

            public RayTracingGeometryKind GeometryKind { get; private set; }

            public Matrix4x4 LocalToWorld { get; set; }

            public Matrix4x4? PreviousLocalToWorld { get; set; }

            public uint ShaderInstanceId { get; set; }

            public uint Mask { get; set; }

            public int Layer { get; set; }

            public MaterialPropertyBlock MaterialProperties { get; set; }

            public void ApplyState(in RegistrationState state, RayTracingGeometryKind geometryKind)
            {
                GeometryProvider = state.GeometryProvider;
                SharedGeometryId = state.SharedGeometryId;
                GeometryKind = geometryKind;
                LocalToWorld = state.LocalToWorld;
                PreviousLocalToWorld = state.PreviousLocalToWorld;
                ShaderInstanceId = state.ShaderInstanceId;
                Mask = state.Mask;
                Layer = state.Layer;
                MaterialProperties = state.MaterialProperties;
            }
        }

        private readonly struct RegistrationState
        {
            public RegistrationState(
                IRayTracingGeometryProvider geometryProvider,
                int sharedGeometryId,
                Matrix4x4 localToWorld,
                Matrix4x4? previousLocalToWorld,
                uint shaderInstanceId,
                uint mask,
                int layer,
                MaterialPropertyBlock materialProperties)
            {
                GeometryProvider = geometryProvider;
                SharedGeometryId = sharedGeometryId;
                LocalToWorld = localToWorld;
                PreviousLocalToWorld = previousLocalToWorld;
                ShaderInstanceId = shaderInstanceId;
                Mask = mask;
                Layer = layer;
                MaterialProperties = materialProperties;
            }

            public IRayTracingGeometryProvider GeometryProvider { get; }

            public int SharedGeometryId { get; }

            public Matrix4x4 LocalToWorld { get; }

            public Matrix4x4? PreviousLocalToWorld { get; }

            public uint ShaderInstanceId { get; }

            public uint Mask { get; }

            public int Layer { get; }

            public MaterialPropertyBlock MaterialProperties { get; }

            public static RegistrationState FromRecord(InstanceRecord record)
            {
                return new RegistrationState(
                    record.GeometryProvider,
                    record.SharedGeometryId,
                    record.LocalToWorld,
                    record.PreviousLocalToWorld,
                    record.ShaderInstanceId,
                    record.Mask,
                    record.Layer,
                    record.MaterialProperties);
            }

            public RegistrationState WithLayer(int layer)
            {
                return new RegistrationState(
                    GeometryProvider,
                    SharedGeometryId,
                    LocalToWorld,
                    PreviousLocalToWorld,
                    ShaderInstanceId,
                    Mask,
                    layer,
                    MaterialProperties);
            }

            public RegistrationState WithSharedGeometry(IRayTracingGeometryProvider geometryProvider, int sharedGeometryId)
            {
                return new RegistrationState(
                    geometryProvider,
                    sharedGeometryId,
                    LocalToWorld,
                    PreviousLocalToWorld,
                    ShaderInstanceId,
                    Mask,
                    Layer,
                    MaterialProperties);
            }
        }
    }
}
