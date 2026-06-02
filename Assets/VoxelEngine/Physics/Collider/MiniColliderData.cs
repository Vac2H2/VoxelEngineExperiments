using System;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Physics.Collider
{
    [Serializable]
    public sealed class MiniColliderData
    {
        public const float DefaultVoxelSize = VoxelEngineSettings.DefaultVoxelSize;
        public const int ChunkVoxelDimension = VoxelVolume.ChunkDimension;

        [SerializeField] private MiniColliderChunkData[] _chunks = Array.Empty<MiniColliderChunkData>();

        public float VoxelSize => VoxelEngineSettings.GlobalVoxelSize;

        public float ChunkSize => ChunkVoxelDimension * VoxelSize;

        public MiniColliderChunkData[] Chunks => _chunks ?? Array.Empty<MiniColliderChunkData>();

        public int ChunkCount => _chunks?.Length ?? 0;

        public bool IsEmpty => ChunkCount == 0;

        public void SetChunks(MiniColliderChunkData[] chunks)
        {
            if (chunks == null || chunks.Length == 0)
            {
                _chunks = Array.Empty<MiniColliderChunkData>();
                return;
            }

            _chunks = new MiniColliderChunkData[chunks.Length];
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                _chunks[chunkIndex] = chunks[chunkIndex].NormalizedClone();
            }
        }

        public Bounds GetLocalChunkBounds(int chunkIndex)
        {
            MiniColliderChunkData chunk = GetChunk(chunkIndex);
            return chunk.GetLocalBounds(ChunkSize, VoxelSize);
        }

        public bool TryGetLocalBounds(out Bounds bounds)
        {
            if (ChunkCount == 0)
            {
                bounds = default;
                return false;
            }

            bounds = GetLocalChunkBounds(0);
            for (int chunkIndex = 1; chunkIndex < ChunkCount; chunkIndex++)
            {
                bounds.Encapsulate(GetLocalChunkBounds(chunkIndex));
            }

            return true;
        }

        internal void Normalize()
        {
            _chunks ??= Array.Empty<MiniColliderChunkData>();
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                _chunks[chunkIndex] = _chunks[chunkIndex].NormalizedClone();
            }
        }

        private MiniColliderChunkData GetChunk(int chunkIndex)
        {
            if ((uint)chunkIndex >= ChunkCount)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index is out of range.");
            }

            return _chunks[chunkIndex];
        }

    }

    [Serializable]
    public struct MiniColliderChunkData
    {
        [SerializeField] private int3 _position;
        [SerializeField] private int[] _occupiedVoxelIndices;

        public MiniColliderChunkData(int3 position)
        {
            _position = position;
            _occupiedVoxelIndices = Array.Empty<int>();
        }

        public MiniColliderChunkData(int3 position, int[] occupiedVoxelIndices)
        {
            _position = position;
            _occupiedVoxelIndices = NormalizeOccupiedVoxelIndices(occupiedVoxelIndices);
        }

        public int3 Position => _position;

        public int[] OccupiedVoxelIndices => _occupiedVoxelIndices ?? Array.Empty<int>();

        public bool HasExplicitVoxelOccupancy => _occupiedVoxelIndices != null && _occupiedVoxelIndices.Length > 0;

        public int OccupiedVoxelCount => HasExplicitVoxelOccupancy
            ? _occupiedVoxelIndices.Length
            : VoxelVolume.VoxelsPerChunk;

        public Bounds GetLocalBounds(float chunkSize)
        {
            if (chunkSize <= 0.0f || !float.IsFinite(chunkSize))
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
            }

            return GetFullChunkLocalBounds(chunkSize);
        }

        public Bounds GetLocalBounds(float chunkSize, float voxelSize)
        {
            if (!HasExplicitVoxelOccupancy)
            {
                return GetLocalBounds(chunkSize);
            }

            if (voxelSize <= 0.0f || !float.IsFinite(voxelSize))
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize), "Voxel size must be greater than zero.");
            }

            GetOccupiedLocalVoxelMinMax(out int3 minVoxel, out int3 maxVoxel);
            Vector3 min = new Vector3(
                ((_position.x * VoxelVolume.ChunkDimension) + minVoxel.x) * voxelSize,
                ((_position.y * VoxelVolume.ChunkDimension) + minVoxel.y) * voxelSize,
                ((_position.z * VoxelVolume.ChunkDimension) + minVoxel.z) * voxelSize);
            Vector3 size = new Vector3(
                maxVoxel.x - minVoxel.x + 1,
                maxVoxel.y - minVoxel.y + 1,
                maxVoxel.z - minVoxel.z + 1) * voxelSize;
            return new Bounds(min + (size * 0.5f), size);
        }

        public void GetOccupiedLocalVoxelMinMax(out int3 min, out int3 max)
        {
            if (!HasExplicitVoxelOccupancy)
            {
                min = int3.zero;
                max = new int3(
                    VoxelVolume.ChunkDimension - 1,
                    VoxelVolume.ChunkDimension - 1,
                    VoxelVolume.ChunkDimension - 1);
                return;
            }

            int3 currentMin = new int3(int.MaxValue, int.MaxValue, int.MaxValue);
            int3 currentMax = new int3(int.MinValue, int.MinValue, int.MinValue);
            int[] occupiedVoxelIndices = OccupiedVoxelIndices;
            for (int index = 0; index < occupiedVoxelIndices.Length; index++)
            {
                UnflattenLocalVoxelIndex(occupiedVoxelIndices[index], out int x, out int y, out int z);
                currentMin = new int3(
                    math.min(currentMin.x, x),
                    math.min(currentMin.y, y),
                    math.min(currentMin.z, z));
                currentMax = new int3(
                    math.max(currentMax.x, x),
                    math.max(currentMax.y, y),
                    math.max(currentMax.z, z));
            }

            min = currentMin;
            max = currentMax;
        }

        internal MiniColliderChunkData NormalizedClone()
        {
            return new MiniColliderChunkData(_position, _occupiedVoxelIndices);
        }

        public static int FlattenLocalVoxelIndex(int x, int y, int z)
        {
            return VoxelVolume.FlattenChunkVoxelIndex(x, y, z);
        }

        public static void UnflattenLocalVoxelIndex(int voxelIndex, out int x, out int y, out int z)
        {
            if ((uint)voxelIndex >= VoxelVolume.VoxelsPerChunk)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelIndex), "Voxel index is out of range.");
            }

            int dimension = VoxelVolume.ChunkDimension;
            x = voxelIndex % dimension;
            y = (voxelIndex / dimension) % dimension;
            z = voxelIndex / (dimension * dimension);
        }

        private Bounds GetFullChunkLocalBounds(float chunkSize)
        {
            Vector3 min = new Vector3(
                _position.x * chunkSize,
                _position.y * chunkSize,
                _position.z * chunkSize);
            Vector3 size = Vector3.one * chunkSize;
            return new Bounds(min + (size * 0.5f), size);
        }

        private static int[] NormalizeOccupiedVoxelIndices(int[] occupiedVoxelIndices)
        {
            if (occupiedVoxelIndices == null || occupiedVoxelIndices.Length == 0)
            {
                return Array.Empty<int>();
            }

            bool[] used = new bool[VoxelVolume.VoxelsPerChunk];
            int validCount = 0;
            for (int index = 0; index < occupiedVoxelIndices.Length; index++)
            {
                int voxelIndex = occupiedVoxelIndices[index];
                if ((uint)voxelIndex >= VoxelVolume.VoxelsPerChunk || used[voxelIndex])
                {
                    continue;
                }

                used[voxelIndex] = true;
                validCount++;
            }

            if (validCount == 0 || validCount == VoxelVolume.VoxelsPerChunk)
            {
                return Array.Empty<int>();
            }

            int[] normalizedIndices = new int[validCount];
            int writeIndex = 0;
            for (int voxelIndex = 0; voxelIndex < used.Length; voxelIndex++)
            {
                if (used[voxelIndex])
                {
                    normalizedIndices[writeIndex++] = voxelIndex;
                }
            }

            return normalizedIndices;
        }
    }
}
