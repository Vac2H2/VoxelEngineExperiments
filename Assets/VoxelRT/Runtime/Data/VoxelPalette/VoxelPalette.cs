using System;
using Unity.Collections;
using UnityEngine;

namespace VoxelRT.Runtime.Data
{
    [CreateAssetMenu(menuName = "VoxelRT/Data/Voxel Palette", fileName = "VoxelPalette")]
    public sealed class VoxelPalette : ScriptableObject
    {
        public const int EntryCount = 256;
        public const int EntryStrideBytes = 4;
        public const int ByteCount = EntryCount * EntryStrideBytes;

        [SerializeField] private VoxelPaletteEntry[] _entries = CreateDefaultEntries();

        [NonSerialized] private object _residencyKey;

        public event Action Changed;

        public object ResidencyKey => _residencyKey ?? (_residencyKey = new object());

        public int Count => _entries?.Length ?? 0;

        public VoxelPaletteEntry GetEntry(int index)
        {
            EnsureEntryCount();

            if ((uint)index >= EntryCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Palette entry index must be in the range [0, 255].");
            }

            return _entries[index];
        }

        public NativeArray<byte> CreateBytes(Allocator allocator)
        {
            EnsureEntryCount();

            NativeArray<byte> paletteBytes = new NativeArray<byte>(ByteCount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < EntryCount; i++)
            {
                VoxelPaletteEntry entry = _entries[i];
                int byteIndex = i * EntryStrideBytes;
                paletteBytes[byteIndex] = entry.Color.r;
                paletteBytes[byteIndex + 1] = entry.Color.g;
                paletteBytes[byteIndex + 2] = entry.Color.b;
                paletteBytes[byteIndex + 3] = entry.SurfaceType;
            }

            return paletteBytes;
        }

        public void InvalidateResidency()
        {
            _residencyKey = new object();
            Changed?.Invoke();
        }

        private void OnValidate()
        {
            EnsureEntryCount();
            InvalidateResidency();
        }

        private void EnsureEntryCount()
        {
            if (_entries == null)
            {
                _entries = CreateDefaultEntries();
                return;
            }

            if (_entries.Length == EntryCount)
            {
                return;
            }

            VoxelPaletteEntry[] resizedEntries = CreateDefaultEntries();
            Array.Copy(_entries, resizedEntries, Math.Min(_entries.Length, resizedEntries.Length));
            _entries = resizedEntries;
        }

        private static VoxelPaletteEntry[] CreateDefaultEntries()
        {
            return new VoxelPaletteEntry[EntryCount];
        }
    }
}
