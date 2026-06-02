using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Data.Voxel;
using VoxelEngine.LifeCycle.Manager;
using VoxelEngine.Render.RenderBackend;
using VoxelEngine.Render.RenderPipeline;

namespace VoxelEngine.Physics.Destruction
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("VoxelEngine/Physics/Destruction Debug Renderer")]
    public sealed class DestructionDebugRenderer : MonoBehaviour
    {
        [SerializeField] private bool _syncOnEnable = true;
        [SerializeField] private bool _autoSyncDirtyData;
        [SerializeField] private bool _logStateChanges = true;
        [SerializeField] private List<DestructionDebugObject> _objects = new List<DestructionDebugObject>();

        private readonly Dictionary<int, Entry> _entriesByObjectId = new Dictionary<int, Entry>();
        private readonly List<DestructionDamageBatch> _pendingDamageBatches = new List<DestructionDamageBatch>();
        private readonly Dictionary<int, DestructionDamageBatch> _pendingDamageByObjectId =
            new Dictionary<int, DestructionDamageBatch>();
        private readonly List<int3> _scratchRemovedVoxels = new List<int3>();
        private int _nextObjectId = 1;
        private bool _renderDataDirty = true;
        private VoxelEngineRenderBackend _registeredBackend;
        private string _lastLoggedState = string.Empty;

        public IReadOnlyList<DestructionDebugObject> Objects => _objects;

        public float VoxelSize => VoxelEngineSettings.GlobalVoxelSize;

        public bool AutoSyncDirtyData
        {
            get => _autoSyncDirtyData;
            set => _autoSyncDirtyData = value;
        }

        public DestructionDebugObject CreateObject(Color32 color)
        {
            DestructionDebugObject debugObject = new DestructionDebugObject(AllocateObjectId(), color);
            _objects.Add(debugObject);
            MarkRenderDataDirty();
            return debugObject;
        }

        public bool TryGetObject(int runtimeId, out DestructionDebugObject debugObject)
        {
            SanitizeSerializedState();
            for (int index = 0; index < _objects.Count; index++)
            {
                debugObject = _objects[index];
                if (debugObject != null && debugObject.RuntimeId == runtimeId)
                {
                    return true;
                }
            }

            debugObject = null;
            return false;
        }

        public bool RemoveObject(DestructionDebugObject debugObject)
        {
            if (debugObject == null)
            {
                return false;
            }

            return RemoveObject(debugObject.RuntimeId);
        }

        public bool RemoveObject(int runtimeId)
        {
            for (int index = 0; index < _objects.Count; index++)
            {
                DestructionDebugObject debugObject = _objects[index];
                if (debugObject == null || debugObject.RuntimeId != runtimeId)
                {
                    continue;
                }

                _objects.RemoveAt(index);
                RemovePendingDamage(runtimeId);
                MarkRenderDataDirty();
                return true;
            }

            return false;
        }

        public void ClearObjects()
        {
            if (_objects.Count == 0)
            {
                return;
            }

            _objects.Clear();
            ClearPendingDamage();
            MarkRenderDataDirty();
        }

        public void MarkRenderDataDirty()
        {
            _renderDataDirty = true;
        }

        public int RemoveVoxelsInRadius(
            DestructionDebugObject debugObject,
            float3 voxelGridCenter,
            float radiusInVoxels)
        {
            if (debugObject == null)
            {
                return 0;
            }

            _scratchRemovedVoxels.Clear();
            int removedCount = debugObject.RemoveVoxelsInRadius(
                voxelGridCenter,
                radiusInVoxels,
                _scratchRemovedVoxels);
            if (removedCount <= 0)
            {
                return 0;
            }

            QueueDamage(debugObject, _scratchRemovedVoxels);
            MarkRenderDataDirty();
            return removedCount;
        }

        public int ConsumeDamageBatches(List<DestructionDamageBatch> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (_pendingDamageBatches.Count == 0)
            {
                return 0;
            }

            int startCount = target.Count;
            for (int batchIndex = 0; batchIndex < _pendingDamageBatches.Count; batchIndex++)
            {
                DestructionDamageBatch batch = _pendingDamageBatches[batchIndex];
                if (batch != null && batch.DeletedVoxels.Count > 0)
                {
                    target.Add(batch);
                }
            }

            ClearPendingDamage();
            return target.Count - startCount;
        }

        public float3 WorldToVoxelGridPoint(Vector3 worldPosition)
        {
            float voxelSize = VoxelEngineSettings.SanitizeVoxelSize(VoxelEngineSettings.GlobalVoxelSize);
            Vector3 localPosition = Quaternion.Inverse(transform.rotation) * (worldPosition - transform.position);
            return new float3(localPosition.x, localPosition.y, localPosition.z) / voxelSize;
        }

        public float3 WorldToVoxelGridDirection(Vector3 worldDirection)
        {
            Vector3 localDirection = Quaternion.Inverse(transform.rotation) * worldDirection;
            return new float3(localDirection.x, localDirection.y, localDirection.z);
        }

        public Vector3 VoxelGridToWorldPoint(float3 voxelGridPosition)
        {
            float voxelSize = VoxelEngineSettings.SanitizeVoxelSize(VoxelEngineSettings.GlobalVoxelSize);
            Vector3 localPosition = new Vector3(voxelGridPosition.x, voxelGridPosition.y, voxelGridPosition.z) * voxelSize;
            return transform.position + (transform.rotation * localPosition);
        }

        public void SyncRenderBackend()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            SanitizeSerializedState();
            VoxelEngineRenderBackend currentBackend = TryGetCurrentBackend();
            if (currentBackend == null)
            {
                LogStateOnce("missing-backend", "Waiting for an active VoxelEngineRenderPipeline before syncing destruction debug voxels.");
                ReleaseAllRegistrations(logRelease: false);
                _renderDataDirty = true;
                return;
            }

            SyncRenderBackend(currentBackend);
        }

        private void Awake()
        {
            SanitizeSerializedState();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            _renderDataDirty = true;
            if (_syncOnEnable)
            {
                SyncRenderBackend();
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            VoxelEngineRenderBackend currentBackend = TryGetCurrentBackend();
            if (currentBackend == null)
            {
                ReleaseAllRegistrations(logRelease: false);
                _renderDataDirty = true;
                return;
            }

            if (_registeredBackend != null && !ReferenceEquals(_registeredBackend, currentBackend))
            {
                ReleaseAllRegistrations(logRelease: false);
                _renderDataDirty = true;
            }

            if (_autoSyncDirtyData && _renderDataDirty)
            {
                SanitizeSerializedState();
                SyncRenderBackend(currentBackend);
                return;
            }

            UpdateRegisteredTransforms(currentBackend);
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            ReleaseAllRegistrations();
        }

        private void OnValidate()
        {
            SanitizeSerializedState();
            MarkRenderDataDirty();
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            VoxelEngineRenderBackend currentBackend = TryGetCurrentBackend();
            if (currentBackend == null)
            {
                return;
            }

            if (_registeredBackend != null && !ReferenceEquals(_registeredBackend, currentBackend))
            {
                ReleaseAllRegistrations(logRelease: false);
                _renderDataDirty = true;
            }

            if (_renderDataDirty || _registeredBackend == null)
            {
                SanitizeSerializedState();
                SyncRenderBackend(currentBackend);
                return;
            }

            UpdateRegisteredTransforms(currentBackend);
        }

        private void SyncRenderBackend(VoxelEngineRenderBackend backend)
        {
            ReleaseAllRegistrations(logRelease: false);
            _registeredBackend = backend;

            Matrix4x4 localToWorld = BuildRenderLocalToWorld();
            int registeredCount = 0;
            bool registrationFailed = false;
            for (int objectIndex = 0; objectIndex < _objects.Count; objectIndex++)
            {
                DestructionDebugObject debugObject = _objects[objectIndex];
                if (!ShouldRender(debugObject))
                {
                    continue;
                }

                try
                {
                    RegisterObject(backend, debugObject, localToWorld);
                    registeredCount++;
                }
                catch (Exception exception)
                {
                    registrationFailed = true;
                    LogStateOnce(
                        $"registration-failed:{debugObject.RuntimeId}",
                        $"Failed to register destruction debug voxel object {debugObject.RuntimeId}.");
                    Debug.LogException(exception, this);
                }
            }

            _renderDataDirty = registrationFailed;
            LogStateOnce(
                $"synced:{registeredCount}",
                $"Synced {registeredCount} destruction debug voxel instances.");
        }

        private void RegisterObject(
            VoxelEngineRenderBackend backend,
            DestructionDebugObject debugObject,
            Matrix4x4 localToWorld)
        {
            int modelSignature = BuildModelSignature(debugObject);
            int paletteSignature = BuildPaletteSignature(debugObject.Color);
            string modelKey = $"DestructionDebugModel:{GetInstanceID()}:{debugObject.RuntimeId}:{modelSignature:x8}";
            string paletteKey = $"DestructionDebugPalette:{GetInstanceID()}:{debugObject.RuntimeId}:{paletteSignature:x8}";

            using VoxelModel model = BuildVoxelModel(debugObject);
            using VoxelPalette palette = BuildPalette(debugObject.Color, Allocator.Temp);

            VoxelEngineRenderInstanceHandle handle = backend.AddInstance(
                new VoxelModelKey(modelKey),
                new VoxelPaletteKey(paletteKey),
                model,
                palette,
                localToWorld);

            _entriesByObjectId.Add(
                debugObject.RuntimeId,
                new Entry(handle, debugObject.RuntimeId, localToWorld));
        }

        private VoxelModel BuildVoxelModel(DestructionDebugObject debugObject)
        {
            int nonEmptyChunkCount = CountUniqueNonEmptyChunks(debugObject);
            VoxelModel model = VoxelModel.Create(Math.Max(1, nonEmptyChunkCount), 1, Allocator.Temp);

            try
            {
                VoxelVolume volume = model.OpaqueVolume;
                List<DestructionDebugChunk> chunks = debugObject.Chunks;
                for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    DestructionDebugChunk chunk = chunks[chunkIndex];
                    if (!TryGetUsableChunk(chunk, out byte[] voxels))
                    {
                        continue;
                    }

                    int volumeChunkIndex;
                    if (!volume.TryGetChunkIndex(chunk.Position, out volumeChunkIndex))
                    {
                        volume.TryAllocateChunk(chunk.Position, out volumeChunkIndex);
                    }

                    NativeSlice<byte> destination = volume.GetChunkVoxelDataSlice(volumeChunkIndex);
                    for (int voxelIndex = 0; voxelIndex < VoxelVolume.VoxelsPerChunk; voxelIndex++)
                    {
                        if (voxels[voxelIndex] != 0)
                        {
                            destination[voxelIndex] = voxels[voxelIndex];
                        }
                    }
                }

                AllocateChunkAabbs(volume);
                return model;
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }

        private void AllocateChunkAabbs(VoxelVolume volume)
        {
            for (int chunkIndex = 0; chunkIndex < volume.ChunkCapacity; chunkIndex++)
            {
                if (!volume.IsChunkAllocated(chunkIndex))
                {
                    continue;
                }

                NativeSlice<byte> voxels = volume.GetChunkVoxelDataSlice(chunkIndex);
                if (!TryGetOccupiedLocalVoxelMinMax(voxels, out int3 min, out int3 max))
                {
                    continue;
                }

                if (!volume.TryAllocateAabbSlot(chunkIndex, out int aabbIndex))
                {
                    throw new InvalidOperationException("Destruction debug chunk has no free AABB slot.");
                }

                volume.SetAabb(chunkIndex, aabbIndex, min, max + new int3(1, 1, 1));
            }
        }

        private void UpdateRegisteredTransforms(VoxelEngineRenderBackend backend)
        {
            if (_entriesByObjectId.Count == 0)
            {
                return;
            }

            Matrix4x4 localToWorld = BuildRenderLocalToWorld();
            foreach (Entry entry in _entriesByObjectId.Values)
            {
                if (!MatrixChanged(entry.LocalToWorld, localToWorld))
                {
                    continue;
                }

                backend.UpdateInstanceTransform(entry.Handle, localToWorld);
                entry.LocalToWorld = localToWorld;
            }
        }

        private void ReleaseAllRegistrations(bool logRelease = true)
        {
            VoxelEngineRenderBackend backend = _registeredBackend;
            if (backend != null)
            {
                foreach (Entry entry in _entriesByObjectId.Values)
                {
                    RemoveBackendInstance(backend, entry.Handle);
                }
            }

            int releasedCount = _entriesByObjectId.Count;
            _entriesByObjectId.Clear();
            _registeredBackend = null;

            if (logRelease && releasedCount > 0)
            {
                LogStateOnce(
                    $"released:{releasedCount}",
                    $"Released {releasedCount} destruction debug voxel instances.");
            }
        }

        private void QueueDamage(DestructionDebugObject debugObject, List<int3> removedVoxels)
        {
            if (removedVoxels == null || removedVoxels.Count == 0)
            {
                return;
            }

            if (!_pendingDamageByObjectId.TryGetValue(debugObject.RuntimeId, out DestructionDamageBatch batch))
            {
                batch = new DestructionDamageBatch(debugObject);
                _pendingDamageByObjectId.Add(debugObject.RuntimeId, batch);
                _pendingDamageBatches.Add(batch);
            }

            for (int voxelIndex = 0; voxelIndex < removedVoxels.Count; voxelIndex++)
            {
                batch.AddDeletedVoxel(removedVoxels[voxelIndex]);
            }
        }

        private void RemovePendingDamage(int runtimeId)
        {
            if (!_pendingDamageByObjectId.Remove(runtimeId))
            {
                return;
            }

            for (int batchIndex = _pendingDamageBatches.Count - 1; batchIndex >= 0; batchIndex--)
            {
                DestructionDamageBatch batch = _pendingDamageBatches[batchIndex];
                if (batch != null && batch.ObjectRuntimeId == runtimeId)
                {
                    _pendingDamageBatches.RemoveAt(batchIndex);
                }
            }
        }

        private void ClearPendingDamage()
        {
            _pendingDamageBatches.Clear();
            _pendingDamageByObjectId.Clear();
        }

        private int AllocateObjectId()
        {
            SanitizeSerializedState();
            return _nextObjectId++;
        }

        private void SanitizeSerializedState()
        {
            _objects ??= new List<DestructionDebugObject>();

            HashSet<int> usedIds = new HashSet<int>();
            int nextId = Math.Max(1, _nextObjectId);
            for (int objectIndex = 0; objectIndex < _objects.Count; objectIndex++)
            {
                DestructionDebugObject debugObject = _objects[objectIndex];
                if (debugObject == null)
                {
                    continue;
                }

                debugObject.Normalize();
                int runtimeId = debugObject.RuntimeId;
                if (runtimeId <= 0 || usedIds.Contains(runtimeId))
                {
                    runtimeId = nextId++;
                    debugObject.SetRuntimeId(runtimeId);
                }

                usedIds.Add(runtimeId);
                nextId = Math.Max(nextId, runtimeId + 1);
            }

            _nextObjectId = nextId;
        }

        private void LogStateOnce(string stateKey, string message)
        {
            if (!_logStateChanges || StringComparer.Ordinal.Equals(_lastLoggedState, stateKey))
            {
                return;
            }

            _lastLoggedState = stateKey;
            Debug.Log($"[{nameof(DestructionDebugRenderer)}] {message}", this);
        }

        private Matrix4x4 BuildRenderLocalToWorld()
        {
            return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one) *
                Matrix4x4.Scale(Vector3.one * VoxelEngineSettings.GlobalVoxelSize);
        }

        private static bool ShouldRender(DestructionDebugObject debugObject)
        {
            return debugObject != null && CountUniqueNonEmptyChunks(debugObject) > 0;
        }

        private static int CountUniqueNonEmptyChunks(DestructionDebugObject debugObject)
        {
            if (debugObject == null)
            {
                return 0;
            }

            HashSet<int3> chunkPositions = new HashSet<int3>();
            List<DestructionDebugChunk> chunks = debugObject.Chunks;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                DestructionDebugChunk chunk = chunks[chunkIndex];
                if (TryGetUsableChunk(chunk, out _))
                {
                    chunkPositions.Add(chunk.Position);
                }
            }

            return chunkPositions.Count;
        }

        private static VoxelPalette BuildPalette(Color32 color, Allocator allocator)
        {
            VoxelPalette palette = new VoxelPalette(allocator);
            palette[0] = new VoxelColor(0, 0, 0, 0);

            VoxelColor voxelColor = new VoxelColor(color.r, color.g, color.b, color.a);
            for (int index = 1; index < VoxelPalette.ColorCount; index++)
            {
                palette[index] = voxelColor;
            }

            return palette;
        }

        private static bool TryGetUsableChunk(DestructionDebugChunk chunk, out byte[] voxels)
        {
            voxels = null;
            if (chunk == null)
            {
                return false;
            }

            chunk.Normalize();
            voxels = chunk.Voxels;
            for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
            {
                if (voxels[voxelIndex] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetOccupiedLocalVoxelMinMax(
            NativeSlice<byte> voxels,
            out int3 min,
            out int3 max)
        {
            min = new int3(int.MaxValue, int.MaxValue, int.MaxValue);
            max = new int3(int.MinValue, int.MinValue, int.MinValue);
            bool hasOccupiedVoxel = false;

            for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
            {
                if (voxels[voxelIndex] == 0)
                {
                    continue;
                }

                UnflattenLocalVoxelIndex(voxelIndex, out int x, out int y, out int z);
                min = new int3(
                    math.min(min.x, x),
                    math.min(min.y, y),
                    math.min(min.z, z));
                max = new int3(
                    math.max(max.x, x),
                    math.max(max.y, y),
                    math.max(max.z, z));
                hasOccupiedVoxel = true;
            }

            return hasOccupiedVoxel;
        }

        private static int BuildModelSignature(DestructionDebugObject debugObject)
        {
            unchecked
            {
                int hash = 17;
                List<DestructionDebugChunk> chunks = debugObject.Chunks;
                hash = (hash * 31) + chunks.Count;
                for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    DestructionDebugChunk chunk = chunks[chunkIndex];
                    if (chunk == null)
                    {
                        hash = hash * 31;
                        continue;
                    }

                    chunk.Normalize();
                    int3 position = chunk.Position;
                    hash = (hash * 31) + position.x;
                    hash = (hash * 31) + position.y;
                    hash = (hash * 31) + position.z;

                    byte[] voxels = chunk.Voxels;
                    for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
                    {
                        hash = (hash * 31) + voxels[voxelIndex];
                    }
                }

                return hash;
            }
        }

        private static int BuildPaletteSignature(Color32 color)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + color.r;
                hash = (hash * 31) + color.g;
                hash = (hash * 31) + color.b;
                hash = (hash * 31) + color.a;
                return hash;
            }
        }

        private static void RemoveBackendInstance(
            VoxelEngineRenderBackend backend,
            VoxelEngineRenderInstanceHandle handle)
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

        private static void UnflattenLocalVoxelIndex(int voxelIndex, out int x, out int y, out int z)
        {
            int dimension = VoxelVolume.ChunkDimension;
            x = voxelIndex & (dimension - 1);
            y = (voxelIndex >> 3) & (dimension - 1);
            z = voxelIndex >> 6;
        }

        private static VoxelEngineRenderBackend TryGetCurrentBackend()
        {
            try
            {
                if (!(RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline))
                {
                    renderPipeline = VoxelEngineRenderPipeline.ActivePipeline;
                }

                return renderPipeline != null ? renderPipeline.RenderBackend : null;
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
                int objectId,
                Matrix4x4 localToWorld)
            {
                Handle = handle;
                ObjectId = objectId;
                LocalToWorld = localToWorld;
            }

            public VoxelEngineRenderInstanceHandle Handle { get; }

            public int ObjectId { get; }

            public Matrix4x4 LocalToWorld { get; set; }
        }
    }

    public sealed class DestructionDamageBatch
    {
        private readonly HashSet<int3> _deletedVoxelSet = new HashSet<int3>();

        public DestructionDamageBatch(DestructionDebugObject debugObject)
        {
            Object = debugObject;
            ObjectRuntimeId = debugObject != null ? debugObject.RuntimeId : 0;
        }

        public DestructionDebugObject Object { get; }

        public int ObjectRuntimeId { get; }

        public List<int3> DeletedVoxels { get; } = new List<int3>();

        public void AddDeletedVoxel(int3 voxelPosition)
        {
            if (!_deletedVoxelSet.Add(voxelPosition))
            {
                return;
            }

            DeletedVoxels.Add(voxelPosition);
        }
    }

    [Serializable]
    public sealed class DestructionDebugObject
    {
        public const byte GroundedVoxelMask = 1 << 7;
        public const byte VoxelStateMask = 0x7f;

        [SerializeField] private int _runtimeId;
        [SerializeField] private Color32 _color = new Color32(255, 255, 255, 255);
        [SerializeField] private List<DestructionDebugChunk> _chunks = new List<DestructionDebugChunk>();
        [SerializeField] private int _groundedVoxelCount;

        public DestructionDebugObject()
        {
        }

        public DestructionDebugObject(int runtimeId, Color32 color)
        {
            _runtimeId = runtimeId;
            _color = color;
        }

        public int RuntimeId => _runtimeId;

        public Color32 Color
        {
            get => _color;
            set => _color = value;
        }

        public List<DestructionDebugChunk> Chunks => _chunks ??= new List<DestructionDebugChunk>();

        public int GroundedVoxelCount => _groundedVoxelCount;

        public bool IsGrounded => _groundedVoxelCount > 0;

        public static byte BuildVoxelValue(byte voxelState, bool grounded)
        {
            byte sanitizedState = SanitizeVoxelState(voxelState);
            return grounded ? (byte)(sanitizedState | GroundedVoxelMask) : sanitizedState;
        }

        public static byte SanitizeVoxelState(byte voxelState)
        {
            byte sanitizedState = (byte)(voxelState & VoxelStateMask);
            return sanitizedState == 0 ? (byte)1 : sanitizedState;
        }

        public static bool IsGroundedVoxel(byte voxelValue)
        {
            return (voxelValue & GroundedVoxelMask) != 0;
        }

        public void SetRuntimeId(int runtimeId)
        {
            _runtimeId = runtimeId;
        }

        public void ClearChunks()
        {
            Chunks.Clear();
            _groundedVoxelCount = 0;
        }

        public bool TryGetChunk(int3 position, out DestructionDebugChunk chunk)
        {
            List<DestructionDebugChunk> chunks = Chunks;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                chunk = chunks[chunkIndex];
                if (chunk != null && chunk.Position.Equals(position))
                {
                    return true;
                }
            }

            chunk = null;
            return false;
        }

        public DestructionDebugChunk GetOrCreateChunk(int3 position)
        {
            List<DestructionDebugChunk> chunks = Chunks;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                DestructionDebugChunk chunk = chunks[chunkIndex];
                if (chunk != null && chunk.Position.Equals(position))
                {
                    return chunk;
                }
            }

            DestructionDebugChunk createdChunk = new DestructionDebugChunk(position);
            chunks.Add(createdChunk);
            return createdChunk;
        }

        public bool TryGetVoxel(int3 voxelPosition, out byte value)
        {
            ToChunkAndLocal(voxelPosition, out int3 chunkPosition, out int localIndex);
            if (!TryGetChunk(chunkPosition, out DestructionDebugChunk chunk))
            {
                value = 0;
                return false;
            }

            value = chunk.GetVoxel(localIndex);
            return value != 0;
        }

        public bool SetVoxel(int3 voxelPosition, byte value)
        {
            ToChunkAndLocal(voxelPosition, out int3 chunkPosition, out int localIndex);
            if (value == 0)
            {
                if (!TryGetChunk(chunkPosition, out DestructionDebugChunk existingChunk))
                {
                    return false;
                }

                if (existingChunk.GetVoxel(localIndex) == 0)
                {
                    return false;
                }

                UpdateGroundedVoxelCount(existingChunk.GetVoxel(localIndex), 0);
                existingChunk.SetVoxel(localIndex, 0);
                return true;
            }

            DestructionDebugChunk chunk = GetOrCreateChunk(chunkPosition);
            byte existingValue = chunk.GetVoxel(localIndex);
            if (existingValue == value)
            {
                return false;
            }

            UpdateGroundedVoxelCount(existingValue, value);
            chunk.SetVoxel(localIndex, value);
            return true;
        }

        public int RemoveVoxelsInRadius(float3 voxelGridCenter, float radiusInVoxels)
        {
            return RemoveVoxelsInRadius(voxelGridCenter, radiusInVoxels, null);
        }

        public int RemoveVoxelsInRadius(
            float3 voxelGridCenter,
            float radiusInVoxels,
            List<int3> removedVoxels)
        {
            float radius = math.max(0.0f, radiusInVoxels);
            float radiusSq = radius * radius;
            int minX = (int)math.floor(voxelGridCenter.x - radius);
            int minY = (int)math.floor(voxelGridCenter.y - radius);
            int minZ = (int)math.floor(voxelGridCenter.z - radius);
            int maxX = (int)math.floor(voxelGridCenter.x + radius);
            int maxY = (int)math.floor(voxelGridCenter.y + radius);
            int maxZ = (int)math.floor(voxelGridCenter.z + radius);
            int removedCount = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float3 voxelCenter = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
                        if (math.lengthsq(voxelCenter - voxelGridCenter) > radiusSq)
                        {
                            continue;
                        }

                        int3 voxelPosition = new int3(x, y, z);
                        if (SetVoxel(voxelPosition, 0))
                        {
                            removedCount++;
                            removedVoxels?.Add(voxelPosition);
                        }
                    }
                }
            }

            return removedCount;
        }

        public int OccupiedVoxelCount()
        {
            int occupiedCount = 0;
            List<DestructionDebugChunk> chunks = Chunks;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                DestructionDebugChunk chunk = chunks[chunkIndex];
                if (chunk == null)
                {
                    continue;
                }

                byte[] voxels = chunk.Voxels;
                for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
                {
                    if (voxels[voxelIndex] != 0)
                    {
                        occupiedCount++;
                    }
                }
            }

            return occupiedCount;
        }

        public bool HasAnyVoxel()
        {
            List<DestructionDebugChunk> chunks = Chunks;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                DestructionDebugChunk chunk = chunks[chunkIndex];
                if (chunk == null)
                {
                    continue;
                }

                byte[] voxels = chunk.Voxels;
                for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
                {
                    if (voxels[voxelIndex] != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool TryGetOccupiedVoxelBounds(out int3 min, out int3 maxExclusive)
        {
            const int chunkSize = VoxelVolume.ChunkDimension;
            min = new int3(int.MaxValue, int.MaxValue, int.MaxValue);
            maxExclusive = new int3(int.MinValue, int.MinValue, int.MinValue);
            bool hasOccupiedVoxel = false;

            List<DestructionDebugChunk> chunks = Chunks;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                DestructionDebugChunk chunk = chunks[chunkIndex];
                if (chunk == null)
                {
                    continue;
                }

                int3 chunkMinVoxel = chunk.Position * chunkSize;
                byte[] voxels = chunk.Voxels;
                for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
                {
                    if (voxels[voxelIndex] == 0)
                    {
                        continue;
                    }

                    UnflattenLocalVoxelIndex(voxelIndex, out int localX, out int localY, out int localZ);
                    int3 voxelPosition = chunkMinVoxel + new int3(localX, localY, localZ);
                    min = new int3(
                        math.min(min.x, voxelPosition.x),
                        math.min(min.y, voxelPosition.y),
                        math.min(min.z, voxelPosition.z));
                    maxExclusive = new int3(
                        math.max(maxExclusive.x, voxelPosition.x + 1),
                        math.max(maxExclusive.y, voxelPosition.y + 1),
                        math.max(maxExclusive.z, voxelPosition.z + 1));
                    hasOccupiedVoxel = true;
                }
            }

            return hasOccupiedVoxel;
        }

        public void Normalize()
        {
            _chunks ??= new List<DestructionDebugChunk>();
            for (int chunkIndex = 0; chunkIndex < _chunks.Count; chunkIndex++)
            {
                _chunks[chunkIndex]?.Normalize();
            }

            RecalculateGroundedVoxelCount();
        }

        private void RecalculateGroundedVoxelCount()
        {
            int groundedCount = 0;
            List<DestructionDebugChunk> chunks = Chunks;
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                DestructionDebugChunk chunk = chunks[chunkIndex];
                if (chunk == null)
                {
                    continue;
                }

                byte[] voxels = chunk.Voxels;
                for (int voxelIndex = 0; voxelIndex < voxels.Length; voxelIndex++)
                {
                    if (IsGroundedVoxel(voxels[voxelIndex]))
                    {
                        groundedCount++;
                    }
                }
            }

            _groundedVoxelCount = groundedCount;
        }

        private void UpdateGroundedVoxelCount(byte oldValue, byte newValue)
        {
            bool wasGrounded = IsGroundedVoxel(oldValue);
            bool isGrounded = IsGroundedVoxel(newValue);
            if (wasGrounded == isGrounded)
            {
                return;
            }

            _groundedVoxelCount += isGrounded ? 1 : -1;
            _groundedVoxelCount = math.max(0, _groundedVoxelCount);
        }

        private static void ToChunkAndLocal(int3 voxelPosition, out int3 chunkPosition, out int localIndex)
        {
            int dimension = VoxelVolume.ChunkDimension;
            chunkPosition = new int3(
                FloorDiv(voxelPosition.x, dimension),
                FloorDiv(voxelPosition.y, dimension),
                FloorDiv(voxelPosition.z, dimension));
            int localX = FloorMod(voxelPosition.x, dimension);
            int localY = FloorMod(voxelPosition.y, dimension);
            int localZ = FloorMod(voxelPosition.z, dimension);
            localIndex = VoxelVolume.FlattenChunkVoxelIndex(localX, localY, localZ);
        }

        private static void UnflattenLocalVoxelIndex(int voxelIndex, out int x, out int y, out int z)
        {
            int dimension = VoxelVolume.ChunkDimension;
            x = voxelIndex & (dimension - 1);
            y = (voxelIndex >> 3) & (dimension - 1);
            z = voxelIndex >> 6;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder != 0 && ((remainder < 0) != (divisor < 0))
                ? quotient - 1
                : quotient;
        }

        private static int FloorMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + math.abs(divisor) : result;
        }
    }

    [Serializable]
    public sealed class DestructionDebugChunk
    {
        [SerializeField] private int3 _position;
        [SerializeField] private byte[] _voxels = new byte[VoxelVolume.VoxelsPerChunk];

        public DestructionDebugChunk()
        {
            Normalize();
        }

        public DestructionDebugChunk(int3 position)
        {
            _position = position;
            Normalize();
        }

        public int3 Position
        {
            get => _position;
            set => _position = value;
        }

        public byte[] Voxels
        {
            get
            {
                Normalize();
                return _voxels;
            }
        }

        public byte GetVoxel(int localIndex)
        {
            ValidateLocalIndex(localIndex);
            return Voxels[localIndex];
        }

        public void SetVoxel(int localIndex, byte value)
        {
            ValidateLocalIndex(localIndex);
            Voxels[localIndex] = value;
        }

        public void SetVoxel(int x, int y, int z, byte value)
        {
            SetVoxel(VoxelVolume.FlattenChunkVoxelIndex(x, y, z), value);
        }

        public void Fill(byte value)
        {
            byte[] voxels = Voxels;
            for (int index = 0; index < voxels.Length; index++)
            {
                voxels[index] = value;
            }
        }

        public void Clear()
        {
            Fill(0);
        }

        public void Normalize()
        {
            if (_voxels != null && _voxels.Length == VoxelVolume.VoxelsPerChunk)
            {
                return;
            }

            byte[] normalizedVoxels = new byte[VoxelVolume.VoxelsPerChunk];
            if (_voxels != null)
            {
                Array.Copy(_voxels, normalizedVoxels, Math.Min(_voxels.Length, normalizedVoxels.Length));
            }

            _voxels = normalizedVoxels;
        }

        private static void ValidateLocalIndex(int localIndex)
        {
            if ((uint)localIndex >= VoxelVolume.VoxelsPerChunk)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localIndex),
                    $"Local voxel index must be in the range [0, {VoxelVolume.VoxelsPerChunk - 1}].");
            }
        }
    }
}
