using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine;
using VoxelEngine.Data.Voxel;
using VoxelEngine.LifeCycle.Manager;
using VoxelEngine.Physics.Collider;
using VoxelEngine.Physics.World;
using VoxelEngine.Render.RenderBackend;
using VoxelEngine.Render.RenderPipeline;

namespace VoxelEngine.Physics.DebugRenderer
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("VoxelEngine/Physics/Mini Physics Debug Renderer")]
    public sealed class MiniPhysicsDebugRenderer : MonoBehaviour
    {
        [SerializeField] private MiniPhysicsDebugPaletteAsset _palette;
        [SerializeField] private byte _voxelState = MiniPhysicsDebugPaletteAsset.DefaultStateIndex;
        [SerializeField] private bool _logStateChanges = true;

        private readonly Dictionary<MiniCollider, Entry> _entriesByCollider = new Dictionary<MiniCollider, Entry>();
        private readonly HashSet<MiniCollider> _visibleColliders = new HashSet<MiniCollider>();
        private readonly List<MiniCollider> _staleColliders = new List<MiniCollider>();

        private VoxelEngineRenderBackend _registeredBackend;
        private VoxelEngineRenderBackend _lastFailedBackend;
        private int _lastFailedColliderInstanceId;
        private int _lastLoggedInstanceCount = -1;
        private string _lastLoggedState = string.Empty;
        private bool _hasRegistrationFailure;

        public MiniPhysicsDebugPaletteAsset Palette
        {
            get => _palette;
            set => _palette = value;
        }

        public byte VoxelState
        {
            get => _voxelState;
            set => _voxelState = SanitizeVoxelState(value);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _voxelState = SanitizeVoxelState(_voxelState);
            RefreshRegistrations();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RefreshRegistrations();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ReleaseAllRegistrations();
        }

        private void OnValidate()
        {
            _voxelState = SanitizeVoxelState(_voxelState);

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            RefreshRegistrations();
        }

        private void RefreshRegistrations()
        {
            VoxelEngineRenderBackend currentBackend = TryGetCurrentBackend();
            if (currentBackend == null)
            {
                LogStateOnce("missing-backend", "Waiting for an active VoxelEngineRenderPipeline before registering mini physics debug voxels.");
                ReleaseAllRegistrations(logRelease: false);
                return;
            }

            if (_registeredBackend != null && !ReferenceEquals(_registeredBackend, currentBackend))
            {
                ReleaseAllRegistrations(logRelease: false);
            }

            _registeredBackend = currentBackend;
            _visibleColliders.Clear();

            IReadOnlyList<MiniCollider> colliders = MiniPhysicsWorld.Default.Colliders;
            for (int colliderIndex = 0; colliderIndex < colliders.Count; colliderIndex++)
            {
                MiniCollider collider = colliders[colliderIndex];
                if (!ShouldRender(collider))
                {
                    continue;
                }

                _visibleColliders.Add(collider);
                RefreshColliderRegistration(currentBackend, collider);
            }

            ReleaseStaleRegistrations(currentBackend);
            LogRegisteredCountIfChanged();
        }

        private void RefreshColliderRegistration(VoxelEngineRenderBackend backend, MiniCollider collider)
        {
            int modelSignature = BuildModelSignature(collider);
            string paletteKey = BuildPaletteRuntimeKey();

            if (_entriesByCollider.TryGetValue(collider, out Entry entry))
            {
                if (entry.ModelSignature != modelSignature ||
                    !StringComparer.Ordinal.Equals(entry.PaletteKey, paletteKey) ||
                    entry.VoxelState != _voxelState)
                {
                    ReleaseRegistration(backend, collider, entry);
                }
                else
                {
                    UpdateTransformIfChanged(backend, collider, entry);
                    ClearRegistrationFailure();
                    return;
                }
            }

            if (IsSameFailure(backend, collider))
            {
                return;
            }

            try
            {
                Entry registeredEntry = RegisterCollider(backend, collider, modelSignature, paletteKey);
                _entriesByCollider[collider] = registeredEntry;
                ClearRegistrationFailure();
            }
            catch (Exception exception)
            {
                RecordRegistrationFailure(backend, collider);
                LogStateOnce(
                    $"registration-failed:{collider.GetInstanceID()}:{modelSignature}:{paletteKey}",
                    $"Failed to register mini physics debug voxels for collider '{collider.name}'.");
                Debug.LogException(exception, this);
            }
        }

        private Entry RegisterCollider(
            VoxelEngineRenderBackend backend,
            MiniCollider collider,
            int modelSignature,
            string paletteKey)
        {
            Matrix4x4 localToWorld = BuildRenderLocalToWorld(collider);
            string modelKey = BuildModelRuntimeKey(collider, modelSignature);

            using VoxelModel model = BuildVoxelModel(collider);
            using VoxelPalette palette = _palette != null
                ? _palette.CreateVoxelPalette(Allocator.Temp)
                : MiniPhysicsDebugPaletteAsset.CreateDefaultVoxelPalette(Allocator.Temp);

            VoxelEngineRenderInstanceHandle handle = backend.AddInstance(
                new VoxelModelKey(modelKey),
                new VoxelPaletteKey(paletteKey),
                model,
                palette,
                localToWorld);

            return new Entry(handle, modelSignature, paletteKey, _voxelState, localToWorld);
        }

        private VoxelModel BuildVoxelModel(MiniCollider collider)
        {
            MiniColliderData data = collider.Data;
            VoxelModel model = VoxelModel.Create(Math.Max(1, data.ChunkCount), 1, Allocator.Temp);

            try
            {
                VoxelVolume volume = model.OpaqueVolume;
                MiniColliderChunkData[] chunks = data.Chunks;
                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    int3 chunkPosition = chunks[chunkIndex].Position;
                    if (!volume.TryAllocateChunk(chunkPosition, out int volumeChunkIndex))
                    {
                        continue;
                    }

                    FillChunkVoxels(volume, volumeChunkIndex, chunks[chunkIndex], _voxelState);
                    if (!volume.TryAllocateAabbSlot(volumeChunkIndex, out int aabbIndex))
                    {
                        throw new InvalidOperationException("Mini physics debug chunk has no free AABB slot.");
                    }

                    chunks[chunkIndex].GetOccupiedLocalVoxelMinMax(out int3 min, out int3 max);
                    volume.SetAabb(
                        volumeChunkIndex,
                        aabbIndex,
                        min,
                        max + new int3(1, 1, 1));
                }

                return model;
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }

        private void UpdateTransformIfChanged(VoxelEngineRenderBackend backend, MiniCollider collider, Entry entry)
        {
            Matrix4x4 localToWorld = BuildRenderLocalToWorld(collider);
            if (!MatrixChanged(entry.LocalToWorld, localToWorld))
            {
                return;
            }

            backend.UpdateInstanceTransform(entry.Handle, localToWorld);
            entry.LocalToWorld = localToWorld;
        }

        private void ReleaseStaleRegistrations(VoxelEngineRenderBackend backend)
        {
            _staleColliders.Clear();
            foreach (MiniCollider collider in _entriesByCollider.Keys)
            {
                if (!_visibleColliders.Contains(collider))
                {
                    _staleColliders.Add(collider);
                }
            }

            for (int index = 0; index < _staleColliders.Count; index++)
            {
                MiniCollider collider = _staleColliders[index];
                ReleaseRegistration(backend, collider, _entriesByCollider[collider]);
            }

            _staleColliders.Clear();
        }

        private void ReleaseAllRegistrations(bool logRelease = true)
        {
            VoxelEngineRenderBackend backend = _registeredBackend;
            if (backend != null)
            {
                foreach (Entry entry in _entriesByCollider.Values)
                {
                    RemoveBackendInstance(backend, entry.Handle);
                }
            }

            int releasedCount = _entriesByCollider.Count;
            _entriesByCollider.Clear();
            _visibleColliders.Clear();
            _staleColliders.Clear();
            _registeredBackend = null;
            _lastLoggedInstanceCount = -1;

            if (logRelease && releasedCount > 0)
            {
                LogStateOnce(
                    $"released:{releasedCount}",
                    $"Released {releasedCount} mini physics debug voxel instances.");
            }
        }

        private void ReleaseRegistration(VoxelEngineRenderBackend backend, MiniCollider collider, Entry entry)
        {
            RemoveBackendInstance(backend, entry.Handle);
            _entriesByCollider.Remove(collider);
        }

        private void LogRegisteredCountIfChanged()
        {
            if (!_logStateChanges || _lastLoggedInstanceCount == _entriesByCollider.Count)
            {
                return;
            }

            _lastLoggedInstanceCount = _entriesByCollider.Count;
            LogStateOnce(
                $"registered-count:{_lastLoggedInstanceCount}",
                $"Registered {_lastLoggedInstanceCount} mini physics debug voxel instances.");
        }

        private void RecordRegistrationFailure(VoxelEngineRenderBackend backend, MiniCollider collider)
        {
            _lastFailedBackend = backend;
            _lastFailedColliderInstanceId = collider != null ? collider.GetInstanceID() : 0;
            _hasRegistrationFailure = true;
        }

        private bool IsSameFailure(VoxelEngineRenderBackend backend, MiniCollider collider)
        {
            return _hasRegistrationFailure &&
                ReferenceEquals(_lastFailedBackend, backend) &&
                _lastFailedColliderInstanceId == (collider != null ? collider.GetInstanceID() : 0);
        }

        private void ClearRegistrationFailure()
        {
            _lastFailedBackend = null;
            _lastFailedColliderInstanceId = 0;
            _hasRegistrationFailure = false;
        }

        private void LogStateOnce(string stateKey, string message)
        {
            if (!_logStateChanges || StringComparer.Ordinal.Equals(_lastLoggedState, stateKey))
            {
                return;
            }

            _lastLoggedState = stateKey;
            Debug.Log($"[{nameof(MiniPhysicsDebugRenderer)}] {message}", this);
        }

        private string BuildPaletteRuntimeKey()
        {
            int paletteId = _palette != null ? _palette.GetInstanceID() : 0;
            int colorVersion = _palette != null ? _palette.ColorVersion : MiniPhysicsDebugPaletteAsset.DefaultColorVersion;
            return $"MiniPhysicsDebugPalette:{paletteId}:{colorVersion:x8}";
        }

        private static string BuildModelRuntimeKey(MiniCollider collider, int modelSignature)
        {
            return $"MiniPhysicsDebugModel:{collider.GetInstanceID()}:{modelSignature:x8}";
        }

        private int BuildModelSignature(MiniCollider collider)
        {
            unchecked
            {
                MiniColliderData data = collider.Data;
                int hash = 17;
                hash = (hash * 31) + _voxelState;
                hash = (hash * 31) + data.ChunkCount;

                MiniColliderChunkData[] chunks = data.Chunks;
                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    int3 position = chunks[chunkIndex].Position;
                    hash = (hash * 31) + position.x;
                    hash = (hash * 31) + position.y;
                    hash = (hash * 31) + position.z;
                    int[] occupiedVoxelIndices = chunks[chunkIndex].OccupiedVoxelIndices;
                    hash = (hash * 31) + occupiedVoxelIndices.Length;
                    for (int voxelIndex = 0; voxelIndex < occupiedVoxelIndices.Length; voxelIndex++)
                    {
                        hash = (hash * 31) + occupiedVoxelIndices[voxelIndex];
                    }
                }

                return hash;
            }
        }

        private static Matrix4x4 BuildRenderLocalToWorld(MiniCollider collider)
        {
            return Matrix4x4.TRS(collider.transform.position, collider.transform.rotation, Vector3.one) *
                Matrix4x4.Scale(Vector3.one * VoxelEngineSettings.GlobalVoxelSize);
        }

        private static bool ShouldRender(MiniCollider collider)
        {
            return collider != null &&
                collider.isActiveAndEnabled &&
                collider.Data.ChunkCount > 0;
        }

        private static void FillChunkVoxels(VoxelVolume volume, int chunkIndex, MiniColliderChunkData chunk, byte voxelState)
        {
            NativeSlice<byte> voxels = volume.GetChunkVoxelDataSlice(chunkIndex);
            if (!chunk.HasExplicitVoxelOccupancy)
            {
                for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
                {
                    voxels[voxelIndex] = voxelState;
                }

                return;
            }

            int[] occupiedVoxelIndices = chunk.OccupiedVoxelIndices;
            for (int index = 0; index < occupiedVoxelIndices.Length; index++)
            {
                voxels[occupiedVoxelIndices[index]] = voxelState;
            }
        }

        private static byte SanitizeVoxelState(byte voxelState)
        {
            return voxelState == MiniPhysicsDebugPaletteAsset.EmptyStateIndex
                ? MiniPhysicsDebugPaletteAsset.DefaultStateIndex
                : voxelState;
        }

        private static void RemoveBackendInstance(VoxelEngineRenderBackend backend, VoxelEngineRenderInstanceHandle handle)
        {
            if (backend == null || !handle.IsValid)
            {
                return;
            }

            try
            {
                backend.RemoveInstance(handle);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (KeyNotFoundException)
            {
            }
        }

        private static bool MatrixChanged(Matrix4x4 left, Matrix4x4 right)
        {
            const float epsilon = 0.000001f;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (Mathf.Abs(left[row, column] - right[row, column]) > epsilon)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static VoxelEngineRenderBackend TryGetCurrentBackend()
        {
            try
            {
                if (!(RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline))
                {
                    return null;
                }

                return renderPipeline.RenderBackend;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        private sealed class Entry
        {
            public Entry(
                VoxelEngineRenderInstanceHandle handle,
                int modelSignature,
                string paletteKey,
                byte voxelState,
                Matrix4x4 localToWorld)
            {
                Handle = handle;
                ModelSignature = modelSignature;
                PaletteKey = paletteKey ?? string.Empty;
                VoxelState = voxelState;
                LocalToWorld = localToWorld;
            }

            public VoxelEngineRenderInstanceHandle Handle { get; }

            public int ModelSignature { get; }

            public string PaletteKey { get; }

            public byte VoxelState { get; }

            public Matrix4x4 LocalToWorld { get; set; }
        }
    }
}
