using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace VoxelEngine.Data.Voxel
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VoxelColor
    {
        public VoxelColor(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public byte R;
        public byte G;
        public byte B;
        public byte A;
    }

    public sealed class VoxelPalette : IDisposable
    {
        public const int ColorCount = 256;

        private NativeArray<VoxelColor> _colors;

        public VoxelPalette(Allocator allocator)
        {
            _colors = new NativeArray<VoxelColor>(ColorCount, allocator, NativeArrayOptions.ClearMemory);
        }

        public bool IsCreated => _colors.IsCreated;

        public NativeArray<VoxelColor> Colors
        {
            get
            {
                ThrowIfDisposed();
                return _colors;
            }
        }

        public VoxelColor this[int index]
        {
            get
            {
                ValidateIndex(index);
                ThrowIfDisposed();
                return _colors[index];
            }
            set
            {
                ValidateIndex(index);
                ThrowIfDisposed();
                _colors[index] = value;
            }
        }

        public void Dispose()
        {
            if (_colors.IsCreated)
            {
                _colors.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (!_colors.IsCreated)
            {
                throw new ObjectDisposedException(nameof(VoxelPalette));
            }
        }

        private static void ValidateIndex(int index)
        {
            if ((uint)index >= ColorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"Palette index must be in the range [0, {ColorCount - 1}].");
            }
        }
    }
}
