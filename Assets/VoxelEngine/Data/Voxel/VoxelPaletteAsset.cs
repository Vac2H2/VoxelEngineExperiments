using System;
using UnityEngine;

namespace VoxelEngine.Data.Voxel
{
    [PreferBinarySerialization]
    public sealed class VoxelPaletteAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private byte[] _serializedData = Array.Empty<byte>();

        public byte[] SerializedData => _serializedData ?? Array.Empty<byte>();

        public bool HasSerializedData => _serializedData != null && _serializedData.Length > 0;

        public int SerializedByteCount => _serializedData?.Length ?? 0;

        public void SetSerializedData(byte[] serializedData)
        {
            if (serializedData == null)
            {
                throw new ArgumentNullException(nameof(serializedData));
            }

            _serializedData = CloneBytes(serializedData);
        }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source.Length == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
