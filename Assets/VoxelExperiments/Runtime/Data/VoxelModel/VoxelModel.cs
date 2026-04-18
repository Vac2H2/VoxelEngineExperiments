using System;
using Unity.Collections;
using UnityEngine;
using VoxelExperiments.Runtime.Rendering.ModelProceduralAabb;
using VoxelExperiments.Runtime.Rendering.VoxelGpuResourceSystem;

namespace VoxelExperiments.Runtime.Data
{
    [CreateAssetMenu(menuName = "VoxelExperiments/Data/Voxel Model", fileName = "VoxelModel")]
    [PreferBinarySerialization]
    public sealed class VoxelModel : ScriptableObject
    {
        [Min(1)]
        [SerializeField] private int _chunkCount = 1;
        [SerializeField, HideInInspector] private VoxelMemoryLayout _memoryLayout = VoxelMemoryLayout.Linear;
        [SerializeField, HideInInspector] private string _contentHash = string.Empty;
        [SerializeField] private byte[] _occupancyBytes = Array.Empty<byte>();
        [SerializeField] private byte[] _voxelBytes = Array.Empty<byte>();
        [SerializeField] private ModelChunkAabb[] _chunkAabbs = Array.Empty<ModelChunkAabb>();

        [NonSerialized] private object _residencyKey;

        public event Action Changed;

        public object ResidencyKey => _residencyKey ?? (_residencyKey = new object());

        public uint ChunkCount => checked((uint)_chunkCount);

        public VoxelMemoryLayout MemoryLayout => _memoryLayout;

        public string ContentHash => _contentHash ?? string.Empty;

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

        public void OverwriteData(
            VoxelMemoryLayout memoryLayout,
            int chunkCount,
            byte[] occupancyBytes,
            byte[] voxelBytes,
            ModelChunkAabb[] chunkAabbs)
        {
            OverwriteData(memoryLayout, chunkCount, occupancyBytes, voxelBytes, chunkAabbs, string.Empty);
        }

        public void OverwriteData(
            VoxelMemoryLayout memoryLayout,
            int chunkCount,
            byte[] occupancyBytes,
            byte[] voxelBytes,
            ModelChunkAabb[] chunkAabbs,
            string contentHash)
        {
            if (chunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), "Chunk count must be greater than zero.");
            }

            ValidateMemoryLayout(memoryLayout);

            if (HasSerializedChunkData() && _memoryLayout != memoryLayout)
            {
                throw new InvalidOperationException(
                    $"VoxelModel memory layout is fixed to {_memoryLayout} and cannot be changed to {memoryLayout}.");
            }

            _chunkCount = chunkCount;
            _memoryLayout = memoryLayout;
            _contentHash = contentHash ?? string.Empty;
            _occupancyBytes = occupancyBytes ?? throw new ArgumentNullException(nameof(occupancyBytes));
            _voxelBytes = voxelBytes ?? throw new ArgumentNullException(nameof(voxelBytes));
            _chunkAabbs = chunkAabbs ?? throw new ArgumentNullException(nameof(chunkAabbs));
            InvalidateResidency();
        }

        public bool HasContentHash(string contentHash)
        {
            return !string.IsNullOrEmpty(contentHash)
                && string.Equals(ContentHash, contentHash, StringComparison.Ordinal);
        }

        public void InvalidateResidency()
        {
            _residencyKey = new object();
            Changed?.Invoke();
        }

        public void ValidateData()
        {
            ValidateMemoryLayout(_memoryLayout);

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

            if (!Enum.IsDefined(typeof(VoxelMemoryLayout), _memoryLayout))
            {
                _memoryLayout = VoxelMemoryLayout.Linear;
            }

            _contentHash ??= string.Empty;
            _occupancyBytes ??= Array.Empty<byte>();
            _voxelBytes ??= Array.Empty<byte>();
            _chunkAabbs ??= Array.Empty<ModelChunkAabb>();
            InvalidateResidency();
        }

        private bool HasSerializedChunkData()
        {
            return (_occupancyBytes != null && _occupancyBytes.Length > 0)
                || (_voxelBytes != null && _voxelBytes.Length > 0)
                || (_chunkAabbs != null && _chunkAabbs.Length > 0);
        }

        private static void ValidateMemoryLayout(VoxelMemoryLayout memoryLayout)
        {
            if (!Enum.IsDefined(typeof(VoxelMemoryLayout), memoryLayout))
            {
                throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }
        }
    }
}
