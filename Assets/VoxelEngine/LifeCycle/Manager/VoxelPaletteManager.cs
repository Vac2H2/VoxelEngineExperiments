using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.LifeCycle.Manager
{
    public sealed class VoxelPaletteManager : IDisposable
    {
        public const int ColorBufferStrideBytes = 4;

        private readonly Dictionary<VoxelPaletteKey, Entry> _entriesByKey = new Dictionary<VoxelPaletteKey, Entry>();
        private readonly Dictionary<int, Entry> _entriesByHandle = new Dictionary<int, Entry>();
        private readonly Stack<int> _freeHandleValues = new Stack<int>();
        private bool _isDisposed;

        public VoxelPaletteHandle Add(VoxelPaletteKey key, VoxelPalette palette)
        {
            EnsureNotDisposed();
            ValidateKey(key);

            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (_entriesByKey.ContainsKey(key))
            {
                throw new InvalidOperationException("The specified VoxelPaletteKey is already resident in VoxelPaletteManager.");
            }

            VoxelPaletteGpuView gpuView = BuildGpuView(palette);
            VoxelPaletteHandle handle = new VoxelPaletteHandle(AllocateHandleValue());

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
                gpuView.DisposeBuffers();
                _freeHandleValues.Push(handle.Value);
                throw;
            }
        }

        public VoxelPaletteHandle Add(AssetReferenceVoxelPalette paletteReference)
        {
            EnsureNotDisposed();
            ValidateReference(paletteReference);
            VoxelPaletteKey key = CreateKey(paletteReference);

            if (_entriesByKey.ContainsKey(key))
            {
                throw new InvalidOperationException("The specified VoxelPaletteKey is already resident in VoxelPaletteManager.");
            }

            string sourceRuntimeKey = paletteReference.RuntimeKey.ToString();
            VoxelPaletteGpuView gpuView = BuildGpuViewFromSource(sourceRuntimeKey);
            VoxelPaletteHandle handle = new VoxelPaletteHandle(AllocateHandleValue());

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
                gpuView.DisposeBuffers();
                _freeHandleValues.Push(handle.Value);
                throw;
            }
        }

        public bool TryRetain(VoxelPaletteKey key, out VoxelPaletteHandle handle)
        {
            EnsureNotDisposed();
            ValidateKey(key);

            if (_entriesByKey.TryGetValue(key, out Entry entry))
            {
                if (entry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("VoxelPaletteManager handle refcount overflow.");
                }

                entry.RefCount++;
                handle = entry.Handle;
                return true;
            }

            handle = default;
            return false;
        }

        public bool TryRetain(AssetReferenceVoxelPalette paletteReference, out VoxelPaletteHandle handle)
        {
            EnsureNotDisposed();
            ValidateReference(paletteReference);
            VoxelPaletteKey key = CreateKey(paletteReference);

            if (_entriesByKey.TryGetValue(key, out Entry entry))
            {
                if (entry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("VoxelPaletteManager handle refcount overflow.");
                }

                entry.RefCount++;
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
            if (string.IsNullOrWhiteSpace(entry.SourceRuntimeKey))
            {
                throw new InvalidOperationException("Runtime-created VoxelPalette entries cannot be synchronized from Addressables.");
            }

            VoxelPaletteGpuView replacementView = BuildGpuViewFromSource(entry.SourceRuntimeKey);
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

            _entriesByKey.Remove(entry.Key);
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

            _entriesByKey.Clear();
            _entriesByHandle.Clear();
            _freeHandleValues.Clear();
            _isDisposed = true;
        }

        private static VoxelPaletteKey CreateKey(AssetReference paletteReference)
        {
            return new VoxelPaletteKey(paletteReference.AssetGUID);
        }

        private static VoxelPaletteGpuView BuildGpuViewFromSource(string sourceRuntimeKey)
        {
            VoxelPalette palette = null;

            try
            {
                palette = LoadPaletteFromSource(sourceRuntimeKey);
                return BuildGpuView(palette);
            }
            finally
            {
                palette?.Dispose();
            }
        }

        private static VoxelPalette LoadPaletteFromSource(string sourceRuntimeKey)
        {
            if (string.IsNullOrWhiteSpace(sourceRuntimeKey))
            {
                throw new ArgumentException("Source runtime key must be a non-empty string.", nameof(sourceRuntimeKey));
            }

            AsyncOperationHandle<VoxelPaletteAsset> loadHandle = default;

            try
            {
                loadHandle = Addressables.LoadAssetAsync<VoxelPaletteAsset>(sourceRuntimeKey);
                VoxelPaletteAsset paletteAsset = loadHandle.WaitForCompletion();
                if (loadHandle.Status != AsyncOperationStatus.Succeeded || paletteAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load VoxelPaletteAsset from Addressables runtime key '{sourceRuntimeKey}'.");
                }

                return VoxelPaletteSerializer.Deserialize(paletteAsset, Allocator.Temp);
            }
            finally
            {
                if (loadHandle.IsValid())
                {
                    Addressables.Release(loadHandle);
                }
            }
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

        private static void ValidateKey(VoxelPaletteKey key)
        {
            if (!key.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(key), "VoxelPaletteKey must contain a non-empty asset GUID.");
            }
        }

        private static void ValidateReference(AssetReferenceVoxelPalette paletteReference)
        {
            if (paletteReference == null)
            {
                throw new ArgumentNullException(nameof(paletteReference));
            }

            if (!paletteReference.RuntimeKeyIsValid())
            {
                throw new ArgumentException("VoxelPalette asset reference must contain a valid Addressables runtime key.", nameof(paletteReference));
            }

            ValidateKey(CreateKey(paletteReference));
        }

        private sealed class Entry
        {
            public Entry(VoxelPaletteKey key, string sourceRuntimeKey, VoxelPaletteHandle handle, VoxelPaletteGpuView gpuView)
            {
                Key = key;
                SourceRuntimeKey = sourceRuntimeKey;
                Handle = handle;
                GpuView = gpuView;
                RefCount = 1;
            }

            public VoxelPaletteKey Key { get; }

            public string SourceRuntimeKey { get; }

            public VoxelPaletteHandle Handle { get; }

            public VoxelPaletteGpuView GpuView { get; set; }

            public uint RefCount { get; set; }
        }
    }

    public readonly struct VoxelPaletteKey : IEquatable<VoxelPaletteKey>
    {
        public VoxelPaletteKey(string assetGuid)
        {
            if (string.IsNullOrWhiteSpace(assetGuid))
            {
                throw new ArgumentException("Asset GUID must be a non-empty string.", nameof(assetGuid));
            }

            AssetGuid = assetGuid;
        }

        public string AssetGuid { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(AssetGuid);

        public bool Equals(VoxelPaletteKey other)
        {
            return StringComparer.Ordinal.Equals(AssetGuid, other.AssetGuid);
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelPaletteKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return AssetGuid == null ? 0 : StringComparer.Ordinal.GetHashCode(AssetGuid);
        }

        public override string ToString()
        {
            return AssetGuid ?? string.Empty;
        }

        public static bool operator ==(VoxelPaletteKey left, VoxelPaletteKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoxelPaletteKey left, VoxelPaletteKey right)
        {
            return !left.Equals(right);
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
