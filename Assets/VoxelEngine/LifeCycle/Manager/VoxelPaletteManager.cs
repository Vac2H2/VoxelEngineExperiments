using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.LifeCycle.Manager
{
    public sealed class VoxelPaletteManager : IDisposable
    {
        public const int ColorBufferStrideBytes = 4;

        private readonly Dictionary<VoxelPalette, Entry> _entriesByPalette = new Dictionary<VoxelPalette, Entry>();
        private readonly Dictionary<int, Entry> _entriesByHandle = new Dictionary<int, Entry>();
        private readonly Stack<int> _freeHandleValues = new Stack<int>();
        private bool _isDisposed;

        public VoxelPaletteHandle Acquire(VoxelPalette palette)
        {
            EnsureNotDisposed();
            palette = ValidatePalette(nameof(palette), palette);

            if (_entriesByPalette.TryGetValue(palette, out Entry existingEntry))
            {
                if (existingEntry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("VoxelPaletteManager handle refcount overflow.");
                }

                existingEntry.RefCount++;
                return existingEntry.Handle;
            }

            VoxelPaletteGpuView gpuView = BuildGpuView(palette);
            VoxelPaletteHandle handle = new VoxelPaletteHandle(AllocateHandleValue());

            try
            {
                Entry entry = new Entry(palette, handle, gpuView);
                _entriesByPalette.Add(palette, entry);
                _entriesByHandle.Add(handle.Value, entry);
                return handle;
            }
            catch
            {
                gpuView.DisposeBuffers();
                _freeHandleValues.Push(handle.Value);
                throw;
            }
        }

        public bool TryGetHandle(VoxelPalette palette, out VoxelPaletteHandle handle)
        {
            EnsureNotDisposed();
            palette = ValidatePalette(nameof(palette), palette);

            if (_entriesByPalette.TryGetValue(palette, out Entry entry))
            {
                handle = entry.Handle;
                return true;
            }

            handle = default;
            return false;
        }

        public void Synchronize(VoxelPaletteHandle handle)
        {
            EnsureNotDisposed();

            Entry entry = GetEntry(handle);
            VoxelPaletteGpuView replacementView = BuildGpuView(entry.Palette);
            VoxelPaletteGpuView previousView = entry.GpuView;
            entry.GpuView = replacementView;
            previousView.DisposeBuffers();
        }

        public VoxelPaletteGpuView GetGpuView(VoxelPaletteHandle handle)
        {
            EnsureNotDisposed();
            return GetEntry(handle).GpuView;
        }

        public void Release(VoxelPaletteHandle handle)
        {
            EnsureNotDisposed();

            Entry entry = GetEntry(handle);
            if (entry.RefCount > 1)
            {
                entry.RefCount--;
                return;
            }

            _entriesByPalette.Remove(entry.Palette);
            _entriesByHandle.Remove(handle.Value);
            entry.GpuView.DisposeBuffers();
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
                entry.GpuView.DisposeBuffers();
            }

            _entriesByPalette.Clear();
            _entriesByHandle.Clear();
            _freeHandleValues.Clear();
            _isDisposed = true;
        }

        private static VoxelPaletteGpuView BuildGpuView(VoxelPalette palette)
        {
            GraphicsBuffer colorBuffer = null;

            try
            {
                colorBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    VoxelPalette.ColorCount,
                    ColorBufferStrideBytes);

                colorBuffer.SetData(palette.Colors.ToArray());
                return new VoxelPaletteGpuView(colorBuffer, VoxelPalette.ColorCount);
            }
            catch
            {
                colorBuffer?.Dispose();
                throw;
            }
        }

        private int AllocateHandleValue()
        {
            if (_freeHandleValues.Count > 0)
            {
                return _freeHandleValues.Pop();
            }

            return checked(_entriesByHandle.Count + 1);
        }

        private Entry GetEntry(VoxelPaletteHandle handle)
        {
            ValidateHandle(handle);

            if (!_entriesByHandle.TryGetValue(handle.Value, out Entry entry))
            {
                throw new KeyNotFoundException("The specified VoxelPaletteHandle is not resident in VoxelPaletteManager.");
            }

            return entry;
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelPaletteManager));
            }
        }

        private static void ValidateHandle(VoxelPaletteHandle handle)
        {
            if (!handle.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), "VoxelPaletteHandle must be non-zero.");
            }
        }

        private static VoxelPalette ValidatePalette(string paramName, VoxelPalette palette)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (!palette.IsCreated)
            {
                throw new ArgumentException("VoxelPalette must be created.", paramName);
            }

            return palette;
        }

        private sealed class Entry
        {
            public Entry(VoxelPalette palette, VoxelPaletteHandle handle, VoxelPaletteGpuView gpuView)
            {
                Palette = palette ?? throw new ArgumentNullException(nameof(palette));
                Handle = handle;
                GpuView = gpuView;
                RefCount = 1;
            }

            public VoxelPalette Palette { get; }

            public VoxelPaletteHandle Handle { get; }

            public VoxelPaletteGpuView GpuView { get; set; }

            public uint RefCount { get; set; }
        }
    }

    public readonly struct VoxelPaletteGpuView
    {
        public VoxelPaletteGpuView(GraphicsBuffer colorBuffer, int colorCount)
        {
            if (colorCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(colorCount), "Color count must be non-negative.");
            }

            ColorBuffer = colorBuffer ?? throw new ArgumentNullException(nameof(colorBuffer));
            ColorCount = colorCount;
        }

        public GraphicsBuffer ColorBuffer { get; }

        public int ColorCount { get; }

        internal void DisposeBuffers()
        {
            ColorBuffer.Dispose();
        }
    }

    public readonly struct VoxelPaletteHandle : IEquatable<VoxelPaletteHandle>
    {
        public const int InvalidValue = 0;

        internal VoxelPaletteHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != InvalidValue;

        public bool Equals(VoxelPaletteHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelPaletteHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(VoxelPaletteHandle left, VoxelPaletteHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelPaletteHandle left, VoxelPaletteHandle right)
        {
            return !left.Equals(right);
        }
    }
}
