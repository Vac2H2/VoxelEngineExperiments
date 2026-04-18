using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.LifeCycle.Manager
{
    public sealed class VoxelModelManager : IDisposable
    {
        public const int VolumeBufferChunkStrideBytes = VoxelVolume.VoxelsPerChunk;
        public const int VolumeBufferChunkStrideWords = VolumeBufferChunkStrideBytes / sizeof(uint);
        public const int AabbDescBufferStrideBytes = sizeof(int);
        public const int AabbBufferStrideBytes = 24;
        private readonly Dictionary<VoxelModel, Entry> _entriesByModel = new Dictionary<VoxelModel, Entry>();
        private readonly Dictionary<int, Entry> _entriesByHandle = new Dictionary<int, Entry>();
        private readonly Stack<int> _freeHandleValues = new Stack<int>();
        private bool _isDisposed;

        public VoxelModelHandle Acquire(VoxelModel model)
        {
            EnsureNotDisposed();

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (_entriesByModel.TryGetValue(model, out Entry existingEntry))
            {
                if (existingEntry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("VoxelModelManager handle refcount overflow.");
                }

                existingEntry.RefCount++;
                return existingEntry.Handle;
            }

            VoxelModelGpuView gpuView = BuildGpuView(model);
            VoxelModelHandle handle = new VoxelModelHandle(AllocateHandleValue());

            try
            {
                Entry entry = new Entry(model, handle, gpuView);
                _entriesByModel.Add(model, entry);
                _entriesByHandle.Add(handle.Value, entry);
                return handle;
            }
            catch
            {
                DisposeGpuView(in gpuView);
                _freeHandleValues.Push(handle.Value);
                throw;
            }
        }

        public bool TryGetHandle(VoxelModel model, out VoxelModelHandle handle)
        {
            EnsureNotDisposed();

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (_entriesByModel.TryGetValue(model, out Entry entry))
            {
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
            VoxelModelGpuView replacementView = BuildGpuView(entry.Model);
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

            _entriesByModel.Remove(entry.Model);
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

            _entriesByModel.Clear();
            _entriesByHandle.Clear();
            _freeHandleValues.Clear();
            _isDisposed = true;
        }

        private static VoxelModelGpuView BuildGpuView(VoxelModel model)
        {
            return new VoxelModelGpuView(
                BuildBlas(model.OpaqueVolume),
                BuildBlas(model.TransparentVolume));
        }

        private static VoxelBlasGpuView BuildBlas(VoxelVolume volume)
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
                return new VoxelBlasGpuView(volumeBuffer, chunkCount, aabbDescBuffer, aabbBuffer, activeAabbCount);
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

                    descriptors[descriptorIndex++] = new VoxelBlasAabbDesc(packedChunkIndex);
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

        private sealed class Entry
        {
            public Entry(VoxelModel model, VoxelModelHandle handle, VoxelModelGpuView gpuView)
            {
                Model = model ?? throw new ArgumentNullException(nameof(model));
                Handle = handle;
                GpuView = gpuView;
                RefCount = 1;
            }

            public VoxelModel Model { get; }

            public VoxelModelHandle Handle { get; }

            public VoxelModelGpuView GpuView { get; set; }

            public uint RefCount { get; set; }
        }
    }

    public readonly struct VoxelModelGpuView
    {
        public VoxelModelGpuView(VoxelBlasGpuView opaque, VoxelBlasGpuView transparent)
        {
            Opaque = opaque;
            Transparent = transparent;
        }

        public VoxelBlasGpuView Opaque { get; }

        public VoxelBlasGpuView Transparent { get; }
    }

    public readonly struct VoxelBlasGpuView
    {
        public VoxelBlasGpuView(
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
        public VoxelBlasAabbDesc(int chunkIndex)
        {
            ChunkIndex = chunkIndex;
        }

        public int ChunkIndex;
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
