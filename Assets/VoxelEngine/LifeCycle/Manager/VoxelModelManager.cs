using System;
using System.Collections.Generic;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.LifeCycle.Manager
{
    public sealed class VoxelModelManager : IDisposable
    {
        public const int VolumeBufferChunkStrideBytes = VoxelVolume.VoxelsPerChunk;
        public const int VolumeBufferChunkStrideWords = VolumeBufferChunkStrideBytes / sizeof(uint);
        public const int AabbDescBufferStrideBytes = sizeof(int) * 7;
        public const int AabbBufferStrideBytes = 24;
        private readonly Dictionary<VoxelModelKey, Entry> _entriesByKey = new Dictionary<VoxelModelKey, Entry>();
        private readonly Dictionary<int, Entry> _entriesByHandle = new Dictionary<int, Entry>();
        private readonly Stack<int> _freeHandleValues = new Stack<int>();
        private bool _isDisposed;

        public VoxelModelHandle Add(VoxelModelKey key, VoxelModel model)
        {
            EnsureNotDisposed();
            ValidateKey(key);

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (_entriesByKey.ContainsKey(key))
            {
                throw new InvalidOperationException("The specified VoxelModelKey is already resident in VoxelModelManager.");
            }

            VoxelModelGpuView gpuView = BuildGpuView(model);
            VoxelModelHandle handle = new VoxelModelHandle(AllocateHandleValue());

            try
            {
                Entry entry = new Entry(key, sourceRuntimeKey: null, handle, gpuView);
                _entriesByKey.Add(key, entry);
                _entriesByHandle.Add(handle.Value, entry);
                return handle;
            }
            catch
            {
                _entriesByKey.Remove(key);
                _entriesByHandle.Remove(handle.Value);
                DisposeGpuView(in gpuView);
                _freeHandleValues.Push(handle.Value);
                throw;
            }
        }

        public VoxelModelHandle Add(AssetReferenceVoxelModel modelReference)
        {
            EnsureNotDisposed();
            ValidateReference(modelReference);
            VoxelModelKey key = CreateKey(modelReference);

            if (_entriesByKey.ContainsKey(key))
            {
                throw new InvalidOperationException("The specified VoxelModelKey is already resident in VoxelModelManager.");
            }

            string sourceRuntimeKey = modelReference.RuntimeKey.ToString();
            VoxelModelGpuView gpuView = BuildGpuViewFromSource(sourceRuntimeKey);
            VoxelModelHandle handle = new VoxelModelHandle(AllocateHandleValue());

            try
            {
                Entry entry = new Entry(key, sourceRuntimeKey, handle, gpuView);
                _entriesByKey.Add(key, entry);
                _entriesByHandle.Add(handle.Value, entry);
                return handle;
            }
            catch
            {
                _entriesByKey.Remove(key);
                _entriesByHandle.Remove(handle.Value);
                DisposeGpuView(in gpuView);
                _freeHandleValues.Push(handle.Value);
                throw;
            }
        }

        public bool TryRetain(VoxelModelKey key, out VoxelModelHandle handle)
        {
            EnsureNotDisposed();
            ValidateKey(key);

            if (_entriesByKey.TryGetValue(key, out Entry entry))
            {
                if (entry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("VoxelModelManager handle refcount overflow.");
                }

                entry.RefCount++;
                handle = entry.Handle;
                return true;
            }

            handle = default;
            return false;
        }

        public bool TryRetain(AssetReferenceVoxelModel modelReference, out VoxelModelHandle handle)
        {
            EnsureNotDisposed();
            ValidateReference(modelReference);
            VoxelModelKey key = CreateKey(modelReference);

            if (_entriesByKey.TryGetValue(key, out Entry entry))
            {
                if (entry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("VoxelModelManager handle refcount overflow.");
                }

                entry.RefCount++;
                handle = entry.Handle;
                return true;
            }

            handle = default;
            return false;
        }

        public void Synchronize(VoxelModelHandle handle)
        {
            EnsureNotDisposed();

            Entry entry = GetEntry(handle);
            if (string.IsNullOrWhiteSpace(entry.SourceRuntimeKey))
            {
                throw new InvalidOperationException("Runtime-created VoxelModel entries cannot be synchronized from Addressables.");
            }

            VoxelModelGpuView replacementView = BuildGpuViewFromSource(entry.SourceRuntimeKey);
            VoxelModelGpuView previousView = entry.GpuView;
            entry.GpuView = replacementView;
            DisposeGpuView(in previousView);
        }

        public VoxelModelGpuView GetGpuView(VoxelModelHandle handle)
        {
            EnsureNotDisposed();
            return GetEntry(handle).GpuView;
        }

        public void Release(VoxelModelHandle handle)
        {
            EnsureNotDisposed();

            Entry entry = GetEntry(handle);
            if (entry.RefCount > 1)
            {
                entry.RefCount--;
                return;
            }

            _entriesByKey.Remove(entry.Key);
            _entriesByHandle.Remove(handle.Value);
            VoxelModelGpuView gpuView = entry.GpuView;
            DisposeGpuView(in gpuView);
            _freeHandleValues.Push(handle.Value);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            foreach (Entry entry in _entriesByHandle.Values)
            {
                VoxelModelGpuView gpuView = entry.GpuView;
                DisposeGpuView(in gpuView);
            }

            _entriesByKey.Clear();
            _entriesByHandle.Clear();
            _freeHandleValues.Clear();
            _isDisposed = true;
        }

        private static VoxelModelKey CreateKey(AssetReference modelReference)
        {
            return new VoxelModelKey(modelReference.AssetGUID);
        }

        private static VoxelModelGpuView BuildGpuViewFromSource(string sourceRuntimeKey)
        {
            VoxelModel model = null;

            try
            {
                model = LoadModelFromSource(sourceRuntimeKey);
                return BuildGpuView(model);
            }
            finally
            {
                model?.Dispose();
            }
        }

        private static VoxelModel LoadModelFromSource(string sourceRuntimeKey)
        {
            if (string.IsNullOrWhiteSpace(sourceRuntimeKey))
            {
                throw new ArgumentException("Source runtime key must be a non-empty string.", nameof(sourceRuntimeKey));
            }

            AsyncOperationHandle<VoxelModelAsset> loadHandle = default;

            try
            {
                loadHandle = Addressables.LoadAssetAsync<VoxelModelAsset>(sourceRuntimeKey);
                VoxelModelAsset modelAsset = loadHandle.WaitForCompletion();
                if (loadHandle.Status != AsyncOperationStatus.Succeeded || modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load VoxelModelAsset from Addressables runtime key '{sourceRuntimeKey}'.");
                }

                return VoxelModelSerializer.Deserialize(modelAsset, Allocator.Temp);
            }
            finally
            {
                if (loadHandle.IsValid())
                {
                    Addressables.Release(loadHandle);
                }
            }
        }

        private static VoxelModelGpuView BuildGpuView(VoxelModel model)
        {
            return new VoxelModelGpuView(
                BuildVolumeGpuView(model.OpaqueVolume),
                BuildVolumeGpuView(model.TransparentVolume));
        }

        private static VoxelVolumeGpuView BuildVolumeGpuView(VoxelVolume volume)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            int chunkCount = volume.ChunkCount;
            int activeAabbCount = CountActiveAabbs(volume);
            GraphicsBuffer volumeBuffer = null;
            GraphicsBuffer aabbDescBuffer = null;
            GraphicsBuffer aabbBuffer = null;

            try
            {
                volumeBuffer = CreateVolumeBuffer(volume, chunkCount, out int[] packedChunkIndexBySlot);
                aabbDescBuffer = CreateAabbDescBuffer(volume, packedChunkIndexBySlot, activeAabbCount);
                aabbBuffer = CreateAabbBuffer(volume, activeAabbCount);
                return new VoxelVolumeGpuView(volumeBuffer, chunkCount, aabbDescBuffer, aabbBuffer, activeAabbCount);
            }
            catch
            {
                volumeBuffer?.Dispose();
                aabbDescBuffer?.Dispose();
                aabbBuffer?.Dispose();
                throw;
            }
        }

        private static GraphicsBuffer CreateVolumeBuffer(
            VoxelVolume volume,
            int chunkCount,
            out int[] packedChunkIndexBySlot)
        {
            packedChunkIndexBySlot = new int[volume.ChunkCapacity];
            Array.Fill(packedChunkIndexBySlot, -1);

            int bufferWordCount = Math.Max(1, checked(chunkCount * VolumeBufferChunkStrideWords));
            GraphicsBuffer volumeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bufferWordCount, sizeof(uint));

            if (chunkCount == 0)
            {
                return volumeBuffer;
            }

            byte[] rawBytes = new byte[checked(chunkCount * VolumeBufferChunkStrideBytes)];
            int packedChunkIndex = 0;

            for (int chunkSlotIndex = 0; chunkSlotIndex < volume.ChunkCapacity; chunkSlotIndex++)
            {
                if (!volume.IsChunkAllocated(chunkSlotIndex))
                {
                    continue;
                }

                packedChunkIndexBySlot[chunkSlotIndex] = packedChunkIndex;
                int writeOffset = checked(packedChunkIndex * VolumeBufferChunkStrideBytes);

                var chunkVoxelData = volume.GetChunkVoxelDataSlice(chunkSlotIndex);
                for (int voxelIndex = 0; voxelIndex < VoxelVolume.VoxelsPerChunk; voxelIndex++)
                {
                    rawBytes[writeOffset + voxelIndex] = chunkVoxelData[voxelIndex];
                }

                packedChunkIndex++;
            }

            uint[] rawWords = new uint[bufferWordCount];
            Buffer.BlockCopy(rawBytes, 0, rawWords, 0, rawBytes.Length);
            volumeBuffer.SetData(rawWords);
            return volumeBuffer;
        }

        private static GraphicsBuffer CreateAabbDescBuffer(
            VoxelVolume volume,
            int[] packedChunkIndexBySlot,
            int activeAabbCount)
        {
            GraphicsBuffer aabbDescBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                Math.Max(1, activeAabbCount),
                AabbDescBufferStrideBytes);

            if (activeAabbCount == 0)
            {
                return aabbDescBuffer;
            }

            VoxelBlasAabbDesc[] descriptors = new VoxelBlasAabbDesc[activeAabbCount];
            int descriptorIndex = 0;

            for (int chunkSlotIndex = 0; chunkSlotIndex < volume.ChunkCapacity; chunkSlotIndex++)
            {
                if (!volume.IsChunkAllocated(chunkSlotIndex))
                {
                    continue;
                }

                int packedChunkIndex = packedChunkIndexBySlot[chunkSlotIndex];
                for (int localAabbIndex = 0; localAabbIndex < VoxelVolume.MaxAabbsPerChunk; localAabbIndex++)
                {
                    if (!volume.IsAabbActive(chunkSlotIndex, localAabbIndex))
                    {
                        continue;
                    }

                    VoxelChunkAabb chunkAabb = volume.GetAabb(chunkSlotIndex, localAabbIndex);
                    descriptors[descriptorIndex++] = new VoxelBlasAabbDesc(
                        packedChunkIndex,
                        chunkAabb.Min,
                        chunkAabb.Max);
                }
            }

            aabbDescBuffer.SetData(descriptors);
            return aabbDescBuffer;
        }

        private static GraphicsBuffer CreateAabbBuffer(VoxelVolume volume, int activeAabbCount)
        {
            GraphicsBuffer aabbBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                Math.Max(1, activeAabbCount),
                AabbBufferStrideBytes);

            if (activeAabbCount == 0)
            {
                return aabbBuffer;
            }

            VoxelBlasAabb[] aabbs = new VoxelBlasAabb[activeAabbCount];
            int aabbWriteIndex = 0;

            for (int chunkSlotIndex = 0; chunkSlotIndex < volume.ChunkCapacity; chunkSlotIndex++)
            {
                if (!volume.IsChunkAllocated(chunkSlotIndex))
                {
                    continue;
                }

                var chunkCoordinate = volume.ChunkCoordinates[chunkSlotIndex];
                for (int localAabbIndex = 0; localAabbIndex < VoxelVolume.MaxAabbsPerChunk; localAabbIndex++)
                {
                    if (!volume.IsAabbActive(chunkSlotIndex, localAabbIndex))
                    {
                        continue;
                    }

                    VoxelChunkAabb chunkAabb = volume.GetAabb(chunkSlotIndex, localAabbIndex);
                    aabbs[aabbWriteIndex++] = new VoxelBlasAabb(
                        ToGridPosition(chunkCoordinate, chunkAabb.Min),
                        ToGridPosition(chunkCoordinate, chunkAabb.Max));
                }
            }

            aabbBuffer.SetData(aabbs);
            return aabbBuffer;
        }

        private static int CountActiveAabbs(VoxelVolume volume)
        {
            int activeAabbCount = 0;
            for (int chunkSlotIndex = 0; chunkSlotIndex < volume.ChunkCapacity; chunkSlotIndex++)
            {
                if (!volume.IsChunkAllocated(chunkSlotIndex))
                {
                    continue;
                }

                for (int localAabbIndex = 0; localAabbIndex < VoxelVolume.MaxAabbsPerChunk; localAabbIndex++)
                {
                    if (volume.IsAabbActive(chunkSlotIndex, localAabbIndex))
                    {
                        activeAabbCount++;
                    }
                }
            }

            return activeAabbCount;
        }

        private static Vector3 ToGridPosition(Unity.Mathematics.int3 chunkCoordinate, Unity.Mathematics.int3 localPosition)
        {
            Unity.Mathematics.int3 chunkOrigin = chunkCoordinate * VoxelVolume.ChunkDimension;
            return new Vector3(
                chunkOrigin.x + localPosition.x,
                chunkOrigin.y + localPosition.y,
                chunkOrigin.z + localPosition.z);
        }

        private static void DisposeGpuView(in VoxelModelGpuView gpuView)
        {
            gpuView.Opaque.DisposeBuffers();
            gpuView.Transparent.DisposeBuffers();
        }

        private int AllocateHandleValue()
        {
            if (_freeHandleValues.Count > 0)
            {
                return _freeHandleValues.Pop();
            }

            return checked(_entriesByHandle.Count + 1);
        }

        private Entry GetEntry(VoxelModelHandle handle)
        {
            ValidateHandle(handle);

            if (!_entriesByHandle.TryGetValue(handle.Value, out Entry entry))
            {
                throw new KeyNotFoundException("The specified VoxelModelHandle is not resident in VoxelModelManager.");
            }

            return entry;
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelModelManager));
            }
        }

        private static void ValidateHandle(VoxelModelHandle handle)
        {
            if (!handle.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), "VoxelModelHandle must be non-zero.");
            }
        }

        private static void ValidateKey(VoxelModelKey key)
        {
            if (!key.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(key), "VoxelModelKey must contain a non-empty asset GUID.");
            }
        }

        private static void ValidateReference(AssetReferenceVoxelModel modelReference)
        {
            if (modelReference == null)
            {
                throw new ArgumentNullException(nameof(modelReference));
            }

            if (!modelReference.RuntimeKeyIsValid())
            {
                throw new ArgumentException("VoxelModel asset reference must contain a valid Addressables runtime key.", nameof(modelReference));
            }

            ValidateKey(CreateKey(modelReference));
        }

        private sealed class Entry
        {
            public Entry(VoxelModelKey key, string sourceRuntimeKey, VoxelModelHandle handle, VoxelModelGpuView gpuView)
            {
                Key = key;
                SourceRuntimeKey = sourceRuntimeKey;
                Handle = handle;
                GpuView = gpuView;
                RefCount = 1;
            }

            public VoxelModelKey Key { get; }

            public string SourceRuntimeKey { get; }

            public VoxelModelHandle Handle { get; }

            public VoxelModelGpuView GpuView { get; set; }

            public uint RefCount { get; set; }
        }
    }

    public readonly struct VoxelModelKey : IEquatable<VoxelModelKey>
    {
        public VoxelModelKey(string assetGuid)
        {
            if (string.IsNullOrWhiteSpace(assetGuid))
            {
                throw new ArgumentException("Asset GUID must be a non-empty string.", nameof(assetGuid));
            }

            AssetGuid = assetGuid;
        }

        public string AssetGuid { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(AssetGuid);

        public bool Equals(VoxelModelKey other)
        {
            return StringComparer.Ordinal.Equals(AssetGuid, other.AssetGuid);
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelModelKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return AssetGuid == null ? 0 : StringComparer.Ordinal.GetHashCode(AssetGuid);
        }

        public override string ToString()
        {
            return AssetGuid ?? string.Empty;
        }

        public static bool operator ==(VoxelModelKey left, VoxelModelKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelModelKey left, VoxelModelKey right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct VoxelModelGpuView
    {
        public VoxelModelGpuView(VoxelVolumeGpuView opaque, VoxelVolumeGpuView transparent)
        {
            Opaque = opaque;
            Transparent = transparent;
        }

        public VoxelVolumeGpuView Opaque { get; }

        public VoxelVolumeGpuView Transparent { get; }
    }

    public readonly struct VoxelVolumeGpuView
    {
        public VoxelVolumeGpuView(
            GraphicsBuffer volumeBuffer,
            int chunkCount,
            GraphicsBuffer aabbDescBuffer,
            GraphicsBuffer aabbBuffer,
            int aabbCount)
        {
            if (chunkCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), "Chunk count must be non-negative.");
            }

            if (aabbCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(aabbCount), "AABB count must be non-negative.");
            }

            VolumeBuffer = volumeBuffer ?? throw new ArgumentNullException(nameof(volumeBuffer));
            AabbDescBuffer = aabbDescBuffer ?? throw new ArgumentNullException(nameof(aabbDescBuffer));
            AabbBuffer = aabbBuffer ?? throw new ArgumentNullException(nameof(aabbBuffer));
            ChunkCount = chunkCount;
            AabbCount = aabbCount;
        }

        public GraphicsBuffer VolumeBuffer { get; }

        public GraphicsBuffer AabbDescBuffer { get; }

        public GraphicsBuffer AabbBuffer { get; }

        public int ChunkCount { get; }

        public int AabbCount { get; }

        internal void DisposeBuffers()
        {
            VolumeBuffer.Dispose();
            AabbDescBuffer.Dispose();
            AabbBuffer.Dispose();
        }
    }

    public readonly struct VoxelModelHandle : IEquatable<VoxelModelHandle>
    {
        public const int InvalidValue = 0;

        internal VoxelModelHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != InvalidValue;

        public bool Equals(VoxelModelHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelModelHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(VoxelModelHandle left, VoxelModelHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelModelHandle left, VoxelModelHandle right)
        {
            return !left.Equals(right);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelBlasAabbDesc
    {
        public VoxelBlasAabbDesc(int chunkIndex, Unity.Mathematics.int3 localMin, Unity.Mathematics.int3 localMax)
        {
            ChunkIndex = chunkIndex;
            LocalMinX = localMin.x;
            LocalMinY = localMin.y;
            LocalMinZ = localMin.z;
            LocalMaxX = localMax.x;
            LocalMaxY = localMax.y;
            LocalMaxZ = localMax.z;
        }

        public int ChunkIndex;
        public int LocalMinX;
        public int LocalMinY;
        public int LocalMinZ;
        public int LocalMaxX;
        public int LocalMaxY;
        public int LocalMaxZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelBlasAabb
    {
        public VoxelBlasAabb(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Vector3 Min;

        public Vector3 Max;
    }
}
