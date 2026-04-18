using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxelEngine.Data.Voxel
{
    public sealed class VoxelVolume : IDisposable
    {
        public const int ChunkDimension = 8;
        public const int VoxelsPerChunk = ChunkDimension * ChunkDimension * ChunkDimension;
        public const int MaxAabbsPerChunk = 16;

        private readonly Dictionary<int3, int> _chunkIndexByCoordinate;
        private readonly Stack<int> _freeChunkIndices;

        private NativeList<int3> _chunkCoordinates;
        private NativeList<byte> _chunkUsage;
        private NativeList<byte> _chunkVoxelData;
        private NativeList<VoxelChunkAabb> _chunkAabbs;

        public VoxelVolume(int initialChunkCapacity, Allocator allocator)
        {
            if (initialChunkCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialChunkCapacity), "Initial chunk capacity must be greater than zero.");
            }

            _chunkIndexByCoordinate = new Dictionary<int3, int>(initialChunkCapacity);
            _freeChunkIndices = new Stack<int>(initialChunkCapacity);
            _chunkCoordinates = new NativeList<int3>(initialChunkCapacity, allocator);
            _chunkUsage = new NativeList<byte>(initialChunkCapacity, allocator);
            _chunkVoxelData = new NativeList<byte>(initialChunkCapacity * VoxelsPerChunk, allocator);
            _chunkAabbs = new NativeList<VoxelChunkAabb>(initialChunkCapacity * MaxAabbsPerChunk, allocator);
            ExpandChunkSlots(initialChunkCapacity);
        }

        public int ChunkCapacity => _chunkCoordinates.Length;

        public int ChunkCount => _chunkIndexByCoordinate.Count;

        public bool IsCreated =>
            _chunkCoordinates.IsCreated &&
            _chunkUsage.IsCreated &&
            _chunkVoxelData.IsCreated &&
            _chunkAabbs.IsCreated;

        public NativeArray<int3> ChunkCoordinates => _chunkCoordinates.AsArray();

        public NativeArray<byte> ChunkVoxelData => _chunkVoxelData.AsArray();

        public NativeArray<VoxelChunkAabb> ChunkAabbs => _chunkAabbs.AsArray();

        public bool ContainsChunk(int3 chunkCoordinate)
        {
            ThrowIfDisposed();
            return _chunkIndexByCoordinate.ContainsKey(chunkCoordinate);
        }

        public bool TryGetChunkIndex(int3 chunkCoordinate, out int chunkIndex)
        {
            ThrowIfDisposed();
            return _chunkIndexByCoordinate.TryGetValue(chunkCoordinate, out chunkIndex);
        }

        public bool IsChunkAllocated(int chunkIndex)
        {
            ThrowIfDisposed();

            if ((uint)chunkIndex >= ChunkCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index is out of range.");
            }

            return _chunkUsage[chunkIndex] != 0;
        }

        public bool TryAllocateChunk(int3 chunkCoordinate, out int chunkIndex)
        {
            ThrowIfDisposed();

            if (_chunkIndexByCoordinate.TryGetValue(chunkCoordinate, out chunkIndex))
            {
                return false;
            }

            if (_freeChunkIndices.Count == 0)
            {
                GrowChunkSlots();
            }

            chunkIndex = _freeChunkIndices.Pop();
            _chunkIndexByCoordinate.Add(chunkCoordinate, chunkIndex);
            _chunkCoordinates[chunkIndex] = chunkCoordinate;
            _chunkUsage[chunkIndex] = 1;
            ClearChunkData(chunkIndex);
            ClearChunkAabbs(chunkIndex);
            return true;
        }

        public bool RemoveChunk(int3 chunkCoordinate)
        {
            ThrowIfDisposed();

            if (!_chunkIndexByCoordinate.TryGetValue(chunkCoordinate, out int chunkIndex))
            {
                return false;
            }

            _chunkIndexByCoordinate.Remove(chunkCoordinate);
            _chunkCoordinates[chunkIndex] = default;
            _chunkUsage[chunkIndex] = 0;
            ClearChunkData(chunkIndex);
            ClearChunkAabbs(chunkIndex);
            _freeChunkIndices.Push(chunkIndex);
            return true;
        }

        public byte GetVoxel(int chunkIndex, int x, int y, int z)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            return _chunkVoxelData[GetChunkVoxelDataBaseIndex(chunkIndex) + FlattenChunkVoxelIndex(x, y, z)];
        }

        public void SetVoxel(int chunkIndex, int x, int y, int z, byte value)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            _chunkVoxelData[GetChunkVoxelDataBaseIndex(chunkIndex) + FlattenChunkVoxelIndex(x, y, z)] = value;
        }

        public NativeSlice<byte> GetChunkVoxelDataSlice(int chunkIndex)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            return new NativeSlice<byte>(_chunkVoxelData.AsArray(), GetChunkVoxelDataBaseIndex(chunkIndex), VoxelsPerChunk);
        }

        public NativeSlice<VoxelChunkAabb> GetChunkAabbSlice(int chunkIndex)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            return new NativeSlice<VoxelChunkAabb>(_chunkAabbs.AsArray(), GetChunkAabbBaseIndex(chunkIndex), MaxAabbsPerChunk);
        }

        public bool TryAllocateAabbSlot(int chunkIndex, out int aabbIndex)
        {
            ValidateAllocatedChunkIndex(chunkIndex);

            int baseIndex = GetChunkAabbBaseIndex(chunkIndex);
            for (int localIndex = 0; localIndex < MaxAabbsPerChunk; localIndex++)
            {
                int absoluteIndex = baseIndex + localIndex;
                if (_chunkAabbs[absoluteIndex].IsActive != 0)
                {
                    continue;
                }

                _chunkAabbs[absoluteIndex] = VoxelChunkAabb.CreateActive(default, default);
                aabbIndex = localIndex;
                return true;
            }

            aabbIndex = -1;
            return false;
        }

        public bool IsAabbActive(int chunkIndex, int aabbIndex)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            return _chunkAabbs[GetChunkAabbIndex(chunkIndex, aabbIndex)].IsActive != 0;
        }

        public VoxelChunkAabb GetAabb(int chunkIndex, int aabbIndex)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            return _chunkAabbs[GetChunkAabbIndex(chunkIndex, aabbIndex)];
        }

        public void SetAabb(int chunkIndex, int aabbIndex, int3 min, int3 max)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            ValidateAabbIndex(aabbIndex);
            ValidateAabbExtents(min, max);
            _chunkAabbs[GetChunkAabbIndex(chunkIndex, aabbIndex)] = VoxelChunkAabb.CreateActive(min, max);
        }

        public void ReleaseAabbSlot(int chunkIndex, int aabbIndex)
        {
            ValidateAllocatedChunkIndex(chunkIndex);
            _chunkAabbs[GetChunkAabbIndex(chunkIndex, aabbIndex)] = default;
        }

        public void Dispose()
        {
            if (_chunkCoordinates.IsCreated)
            {
                _chunkCoordinates.Dispose();
            }

            if (_chunkUsage.IsCreated)
            {
                _chunkUsage.Dispose();
            }

            if (_chunkVoxelData.IsCreated)
            {
                _chunkVoxelData.Dispose();
            }

            if (_chunkAabbs.IsCreated)
            {
                _chunkAabbs.Dispose();
            }

            _chunkIndexByCoordinate.Clear();
            _freeChunkIndices.Clear();
        }

        public static int FlattenChunkVoxelIndex(int x, int y, int z)
        {
            ValidateLocalVoxelCoordinate(nameof(x), x);
            ValidateLocalVoxelCoordinate(nameof(y), y);
            ValidateLocalVoxelCoordinate(nameof(z), z);
            return x + (ChunkDimension * y) + (ChunkDimension * ChunkDimension * z);
        }

        private int GetChunkVoxelDataBaseIndex(int chunkIndex)
        {
            return chunkIndex * VoxelsPerChunk;
        }

        private int GetChunkAabbBaseIndex(int chunkIndex)
        {
            return chunkIndex * MaxAabbsPerChunk;
        }

        private void GrowChunkSlots()
        {
            ExpandChunkSlots(Math.Max(1, ChunkCapacity));
        }

        private void ExpandChunkSlots(int additionalChunkCount)
        {
            if (additionalChunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(additionalChunkCount), "Additional chunk count must be greater than zero.");
            }

            int startChunkIndex = ChunkCapacity;
            AppendDefaultItems(ref _chunkCoordinates, additionalChunkCount);
            AppendDefaultItems(ref _chunkUsage, additionalChunkCount);
            AppendDefaultItems(ref _chunkVoxelData, additionalChunkCount * VoxelsPerChunk);
            AppendDefaultItems(ref _chunkAabbs, additionalChunkCount * MaxAabbsPerChunk);

            for (int chunkIndex = startChunkIndex + additionalChunkCount - 1; chunkIndex >= startChunkIndex; chunkIndex--)
            {
                _freeChunkIndices.Push(chunkIndex);
            }
        }

        private static void AppendDefaultItems<T>(ref NativeList<T> list, int count) where T : unmanaged
        {
            for (int index = 0; index < count; index++)
            {
                list.Add(default);
            }
        }

        private int GetChunkAabbIndex(int chunkIndex, int aabbIndex)
        {
            ValidateAabbIndex(aabbIndex);
            return GetChunkAabbBaseIndex(chunkIndex) + aabbIndex;
        }

        private void ClearChunkData(int chunkIndex)
        {
            int baseIndex = GetChunkVoxelDataBaseIndex(chunkIndex);
            for (int voxelIndex = 0; voxelIndex < VoxelsPerChunk; voxelIndex++)
            {
                _chunkVoxelData[baseIndex + voxelIndex] = 0;
            }
        }

        private void ClearChunkAabbs(int chunkIndex)
        {
            int baseIndex = GetChunkAabbBaseIndex(chunkIndex);
            for (int aabbIndex = 0; aabbIndex < MaxAabbsPerChunk; aabbIndex++)
            {
                _chunkAabbs[baseIndex + aabbIndex] = default;
            }
        }

        private void ThrowIfDisposed()
        {
            if (!IsCreated)
            {
                throw new ObjectDisposedException(nameof(VoxelVolume));
            }
        }

        private void ValidateAllocatedChunkIndex(int chunkIndex)
        {
            ThrowIfDisposed();

            if ((uint)chunkIndex >= ChunkCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index is out of range.");
            }

            if (_chunkUsage[chunkIndex] == 0)
            {
                throw new InvalidOperationException("Chunk index is not currently allocated.");
            }
        }

        private static void ValidateLocalVoxelCoordinate(string paramName, int value)
        {
            if ((uint)value >= ChunkDimension)
            {
                throw new ArgumentOutOfRangeException(paramName, $"Voxel coordinate must be in the range [0, {ChunkDimension - 1}].");
            }
        }

        private static void ValidateAabbIndex(int aabbIndex)
        {
            if ((uint)aabbIndex >= MaxAabbsPerChunk)
            {
                throw new ArgumentOutOfRangeException(nameof(aabbIndex), $"AABB index must be in the range [0, {MaxAabbsPerChunk - 1}].");
            }
        }

        private static void ValidateAabbExtents(int3 min, int3 max)
        {
            ValidateAabbCoordinate(nameof(min), min);
            ValidateAabbCoordinate(nameof(max), max);

            if (max.x < min.x || max.y < min.y || max.z < min.z)
            {
                throw new ArgumentException("AABB max must be greater than or equal to min on every axis.");
            }
        }

        private static void ValidateAabbCoordinate(string paramName, int3 value)
        {
            if ((uint)value.x > ChunkDimension ||
                (uint)value.y > ChunkDimension ||
                (uint)value.z > ChunkDimension)
            {
                throw new ArgumentOutOfRangeException(paramName, $"AABB coordinates must be in the range [0, {ChunkDimension}].");
            }
        }
    }

    public struct VoxelChunkAabb
    {
        public byte IsActive;
        public int3 Min;
        public int3 Max;

        public static VoxelChunkAabb CreateActive(int3 min, int3 max)
        {
            return new VoxelChunkAabb
            {
                IsActive = 1,
                Min = min,
                Max = max
            };
        }
    }
}
