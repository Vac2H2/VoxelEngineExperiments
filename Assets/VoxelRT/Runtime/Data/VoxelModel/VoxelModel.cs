using System;
using Unity.Collections;
using UnityEngine;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;
using VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem;

namespace VoxelRT.Runtime.Data
{
    [CreateAssetMenu(menuName = "VoxelRT/Data/Voxel Model", fileName = "VoxelModel")]
    [PreferBinarySerialization]
    public sealed class VoxelModel : ScriptableObject
    {
        [Min(1)]
        [SerializeField] private int _chunkCount = 1;
        [SerializeField] private byte[] _occupancyBytes = Array.Empty<byte>();
        [SerializeField] private byte[] _voxelBytes = Array.Empty<byte>();
        [SerializeField] private ModelChunkAabb[] _chunkAabbs = Array.Empty<ModelChunkAabb>();

        [NonSerialized] private object _residencyKey;

        public event Action Changed;

        public object ResidencyKey => _residencyKey ?? (_residencyKey = new object());

        public uint ChunkCount => checked((uint)_chunkCount);

        public int OccupancyByteCount => _occupancyBytes?.Length ?? 0;

        public int VoxelByteCount => _voxelBytes?.Length ?? 0;

        public int ChunkAabbCount => _chunkAabbs?.Length ?? 0;

        public VoxelModelUpload CreateUpload(
            Allocator allocator,
            out NativeArray<byte> occupancyBytes,
            out NativeArray<byte> voxelBytes,
            out NativeArray<ModelChunkAabb> chunkAabbs)
        {
            ValidateData();

            occupancyBytes = new NativeArray<byte>(_occupancyBytes, allocator);
            voxelBytes = new NativeArray<byte>(_voxelBytes, allocator);
            chunkAabbs = new NativeArray<ModelChunkAabb>(_chunkAabbs, allocator);
            return new VoxelModelUpload(ChunkCount, occupancyBytes, voxelBytes, chunkAabbs);
        }

        public void InvalidateResidency()
        {
            _residencyKey = new object();
            Changed?.Invoke();
        }

        public void ValidateData()
        {
            if (_chunkCount <= 0)
            {
                throw new InvalidOperationException("VoxelModel chunk count must be greater than zero.");
            }

            if (_chunkAabbs == null || _chunkAabbs.Length != _chunkCount)
            {
                throw new InvalidOperationException("VoxelModel chunk AABB count must match chunk count.");
            }

            if (_occupancyBytes == null)
            {
                throw new InvalidOperationException("VoxelModel occupancy bytes are not assigned.");
            }

            if (_voxelBytes == null)
            {
                throw new InvalidOperationException("VoxelModel voxel bytes are not assigned.");
            }

            int expectedOccupancyByteCount = checked(_chunkCount * VoxelChunkLayout.OccupancyByteCount);
            if (_occupancyBytes.Length != expectedOccupancyByteCount)
            {
                throw new InvalidOperationException(
                    $"VoxelModel occupancy byte count must be {expectedOccupancyByteCount} for {_chunkCount} chunks.");
            }

            int expectedVoxelByteCount = checked(_chunkCount * VoxelChunkLayout.VoxelDataByteCount);
            if (_voxelBytes.Length != expectedVoxelByteCount)
            {
                throw new InvalidOperationException(
                    $"VoxelModel voxel byte count must be {expectedVoxelByteCount} for {_chunkCount} chunks.");
            }
        }

        private void OnValidate()
        {
            if (_chunkCount < 1)
            {
                _chunkCount = 1;
            }

            _occupancyBytes ??= Array.Empty<byte>();
            _voxelBytes ??= Array.Empty<byte>();
            _chunkAabbs ??= Array.Empty<ModelChunkAabb>();
            InvalidateResidency();
        }
    }
}
