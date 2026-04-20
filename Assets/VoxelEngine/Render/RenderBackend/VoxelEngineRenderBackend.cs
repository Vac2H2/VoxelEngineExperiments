using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Data.Voxel;
using VoxelEngine.LifeCycle.Manager;

namespace VoxelEngine.Render.RenderBackend
{
    public readonly struct VoxelBlasGpuView
    {
        public VoxelBlasGpuView(VoxelModelGpuView model, VoxelPaletteGpuView palette)
        {
            Model = model;
            Palette = palette;
        }

        public VoxelModelGpuView Model { get; }

        public VoxelPaletteGpuView Palette { get; }

        public VoxelVolumeGpuView GetVolume(bool isOpaque)
        {
            return isOpaque ? Model.Opaque : Model.Transparent;
        }
    }

    public sealed class VoxelEngineRenderBackend : IDisposable
    {
        public const string EmptyModelRtasErrorMessage = "VoxelModel contains no active AABBs to add into RTAS.";

        private readonly VoxelModelManager _modelManager;
        private readonly VoxelPaletteManager _paletteManager;
        private readonly VoxelRtasManager _rtasManager;
        private readonly Dictionary<int, Entry> _entriesByHandle = new Dictionary<int, Entry>();
        private readonly Stack<int> _freeHandleValues = new Stack<int>();
        private bool _isDisposed;

        public VoxelEngineRenderBackend(Material rayTracingMaterial)
            : this(rayTracingMaterial, VoxelRtasManagerConfiguration.Default)
        {
        }

        public VoxelEngineRenderBackend(
            Material rayTracingMaterial,
            VoxelRtasManagerConfiguration rtasConfiguration)
        {
            if (rayTracingMaterial == null)
            {
                throw new ArgumentNullException(nameof(rayTracingMaterial));
            }

            _modelManager = new VoxelModelManager();
            _paletteManager = new VoxelPaletteManager();
            _rtasManager = new VoxelRtasManager(rayTracingMaterial, rtasConfiguration);
        }

        public VoxelModelManager ModelManager
        {
            get
            {
                EnsureNotDisposed();
                return _modelManager;
            }
        }

        public VoxelPaletteManager PaletteManager
        {
            get
            {
                EnsureNotDisposed();
                return _paletteManager;
            }
        }

        public VoxelRtasManager RtasManager
        {
            get
            {
                EnsureNotDisposed();
                return _rtasManager;
            }
        }

        public bool HasInstances
        {
            get
            {
                EnsureNotDisposed();
                return _entriesByHandle.Count > 0;
            }
        }

        public VoxelBlasGpuView GetBlasGpuView(VoxelEngineRenderInstanceHandle handle)
        {
            EnsureNotDisposed();
            Entry entry = GetEntry(handle);
            return CreateBlasGpuView(entry.ModelHandle, entry.PaletteHandle);
        }

        public VoxelEngineRenderInstanceHandle AddInstance(
            AssetReferenceVoxelModel modelReference,
            AssetReferenceVoxelPalette paletteReference,
            Matrix4x4 localToWorld)
        {
            EnsureNotDisposed();

            VoxelModelHandle modelHandle = default;
            VoxelPaletteHandle paletteHandle = default;
            VoxelRtasHandle opaqueRtasHandle = default;
            VoxelRtasHandle transparentRtasHandle = default;

            try
            {
                if (!_modelManager.TryRetain(modelReference, out modelHandle))
                {
                    modelHandle = _modelManager.Add(modelReference);
                }

                if (!_paletteManager.TryRetain(paletteReference, out paletteHandle))
                {
                    paletteHandle = _paletteManager.Add(paletteReference);
                }

                VoxelBlasGpuView blasGpuView = CreateBlasGpuView(modelHandle, paletteHandle);

                VoxelVolumeGpuView opaqueVolumeGpuView = blasGpuView.Model.Opaque;
                if (opaqueVolumeGpuView.AabbCount > 0)
                {
                    opaqueRtasHandle = _rtasManager.AddInstance(in blasGpuView, localToWorld, isOpaque: true);
                }

                VoxelVolumeGpuView transparentVolumeGpuView = blasGpuView.Model.Transparent;
                if (transparentVolumeGpuView.AabbCount > 0)
                {
                    transparentRtasHandle = _rtasManager.AddInstance(in blasGpuView, localToWorld, isOpaque: false);
                }

                if (!opaqueRtasHandle.IsValid && !transparentRtasHandle.IsValid)
                {
                    throw new InvalidOperationException(EmptyModelRtasErrorMessage);
                }

                VoxelEngineRenderInstanceHandle instanceHandle = new VoxelEngineRenderInstanceHandle(AllocateHandleValue());
                _entriesByHandle.Add(
                    instanceHandle.Value,
                    new Entry(instanceHandle, modelHandle, paletteHandle, opaqueRtasHandle, transparentRtasHandle));
                return instanceHandle;
            }
            catch
            {
                if (transparentRtasHandle.IsValid)
                {
                    _rtasManager.RemoveInstance(transparentRtasHandle);
                }

                if (opaqueRtasHandle.IsValid)
                {
                    _rtasManager.RemoveInstance(opaqueRtasHandle);
                }

                if (paletteHandle.IsValid)
                {
                    _paletteManager.Release(paletteHandle);
                }

                if (modelHandle.IsValid)
                {
                    _modelManager.Release(modelHandle);
                }

                throw;
            }
        }

        public VoxelEngineRenderInstanceHandle AddInstance(
            VoxelModelKey modelKey,
            VoxelPaletteKey paletteKey,
            VoxelModel model,
            VoxelPalette palette,
            Matrix4x4 localToWorld)
        {
            EnsureNotDisposed();

            VoxelModelHandle modelHandle = default;
            VoxelPaletteHandle paletteHandle = default;
            VoxelRtasHandle opaqueRtasHandle = default;
            VoxelRtasHandle transparentRtasHandle = default;

            try
            {
                if (!_modelManager.TryRetain(modelKey, out modelHandle))
                {
                    modelHandle = _modelManager.Add(modelKey, model);
                }

                if (!_paletteManager.TryRetain(paletteKey, out paletteHandle))
                {
                    paletteHandle = _paletteManager.Add(paletteKey, palette);
                }

                VoxelBlasGpuView blasGpuView = CreateBlasGpuView(modelHandle, paletteHandle);

                VoxelVolumeGpuView opaqueVolumeGpuView = blasGpuView.Model.Opaque;
                if (opaqueVolumeGpuView.AabbCount > 0)
                {
                    opaqueRtasHandle = _rtasManager.AddInstance(in blasGpuView, localToWorld, isOpaque: true);
                }

                VoxelVolumeGpuView transparentVolumeGpuView = blasGpuView.Model.Transparent;
                if (transparentVolumeGpuView.AabbCount > 0)
                {
                    transparentRtasHandle = _rtasManager.AddInstance(in blasGpuView, localToWorld, isOpaque: false);
                }

                if (!opaqueRtasHandle.IsValid && !transparentRtasHandle.IsValid)
                {
                    throw new InvalidOperationException(EmptyModelRtasErrorMessage);
                }

                VoxelEngineRenderInstanceHandle instanceHandle = new VoxelEngineRenderInstanceHandle(AllocateHandleValue());
                _entriesByHandle.Add(
                    instanceHandle.Value,
                    new Entry(instanceHandle, modelHandle, paletteHandle, opaqueRtasHandle, transparentRtasHandle));
                return instanceHandle;
            }
            catch
            {
                if (transparentRtasHandle.IsValid)
                {
                    _rtasManager.RemoveInstance(transparentRtasHandle);
                }

                if (opaqueRtasHandle.IsValid)
                {
                    _rtasManager.RemoveInstance(opaqueRtasHandle);
                }

                if (paletteHandle.IsValid)
                {
                    _paletteManager.Release(paletteHandle);
                }

                if (modelHandle.IsValid)
                {
                    _modelManager.Release(modelHandle);
                }

                throw;
            }
        }

        public VoxelEngineRenderInstanceHandle AddDebugAabbOverlayInstance(
            VoxelModelKey modelKey,
            VoxelPaletteKey paletteKey,
            VoxelModel model,
            VoxelPalette palette,
            Matrix4x4 localToWorld,
            float lineWidth)
        {
            EnsureNotDisposed();

            VoxelModelHandle modelHandle = default;
            VoxelPaletteHandle paletteHandle = default;
            VoxelRtasHandle debugRtasHandle = default;

            try
            {
                if (!_modelManager.TryRetain(modelKey, out modelHandle))
                {
                    modelHandle = _modelManager.Add(modelKey, model);
                }

                if (!_paletteManager.TryRetain(paletteKey, out paletteHandle))
                {
                    paletteHandle = _paletteManager.Add(paletteKey, palette);
                }

                VoxelBlasGpuView blasGpuView = CreateBlasGpuView(modelHandle, paletteHandle);
                if (blasGpuView.Model.Opaque.AabbCount <= 0)
                {
                    throw new InvalidOperationException(EmptyModelRtasErrorMessage);
                }

                debugRtasHandle = _rtasManager.AddDebugAabbOverlayInstance(in blasGpuView, localToWorld, lineWidth);

                VoxelEngineRenderInstanceHandle instanceHandle = new VoxelEngineRenderInstanceHandle(AllocateHandleValue());
                _entriesByHandle.Add(
                    instanceHandle.Value,
                    new Entry(instanceHandle, modelHandle, paletteHandle, debugRtasHandle, default));
                return instanceHandle;
            }
            catch
            {
                if (debugRtasHandle.IsValid)
                {
                    _rtasManager.RemoveInstance(debugRtasHandle);
                }

                if (paletteHandle.IsValid)
                {
                    _paletteManager.Release(paletteHandle);
                }

                if (modelHandle.IsValid)
                {
                    _modelManager.Release(modelHandle);
                }

                throw;
            }
        }

        public void RemoveInstance(VoxelEngineRenderInstanceHandle handle)
        {
            EnsureNotDisposed();
            Entry entry = GetEntry(handle);

            _entriesByHandle.Remove(handle.Value);

            if (entry.TransparentRtasHandle.IsValid)
            {
                _rtasManager.RemoveInstance(entry.TransparentRtasHandle);
            }

            if (entry.OpaqueRtasHandle.IsValid)
            {
                _rtasManager.RemoveInstance(entry.OpaqueRtasHandle);
            }

            _paletteManager.Release(entry.PaletteHandle);
            _modelManager.Release(entry.ModelHandle);
            _freeHandleValues.Push(handle.Value);
        }

        public void Clear()
        {
            EnsureNotDisposed();

            if (_entriesByHandle.Count == 0)
            {
                return;
            }

            int[] handles = new int[_entriesByHandle.Count];
            _entriesByHandle.Keys.CopyTo(handles, 0);
            for (int i = 0; i < handles.Length; i++)
            {
                RemoveInstance(new VoxelEngineRenderInstanceHandle(handles[i]));
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _rtasManager.Dispose();
            _paletteManager.Dispose();
            _modelManager.Dispose();
            _entriesByHandle.Clear();
            _freeHandleValues.Clear();
            _isDisposed = true;
        }

        private VoxelBlasGpuView CreateBlasGpuView(VoxelModelHandle modelHandle, VoxelPaletteHandle paletteHandle)
        {
            VoxelModelGpuView modelGpuView = _modelManager.GetGpuView(modelHandle);
            VoxelPaletteGpuView paletteGpuView = _paletteManager.GetGpuView(paletteHandle);
            return new VoxelBlasGpuView(modelGpuView, paletteGpuView);
        }

        private Entry GetEntry(VoxelEngineRenderInstanceHandle handle)
        {
            ValidateHandle(handle);

            if (!_entriesByHandle.TryGetValue(handle.Value, out Entry entry))
            {
                throw new KeyNotFoundException(
                    "The specified VoxelEngineRenderInstanceHandle is not resident in VoxelEngineRenderBackend.");
            }

            return entry;
        }

        private int AllocateHandleValue()
        {
            if (_freeHandleValues.Count > 0)
            {
                return _freeHandleValues.Pop();
            }

            return checked(_entriesByHandle.Count + 1);
        }

        private static void ValidateHandle(VoxelEngineRenderInstanceHandle handle)
        {
            if (!handle.IsValid)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(handle),
                    "VoxelEngineRenderInstanceHandle must be non-zero.");
            }
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelEngineRenderBackend));
            }
        }

        private sealed class Entry
        {
            public Entry(
                VoxelEngineRenderInstanceHandle handle,
                VoxelModelHandle modelHandle,
                VoxelPaletteHandle paletteHandle,
                VoxelRtasHandle opaqueRtasHandle,
                VoxelRtasHandle transparentRtasHandle)
            {
                Handle = handle;
                ModelHandle = modelHandle;
                PaletteHandle = paletteHandle;
                OpaqueRtasHandle = opaqueRtasHandle;
                TransparentRtasHandle = transparentRtasHandle;
            }

            public VoxelEngineRenderInstanceHandle Handle { get; }

            public VoxelModelHandle ModelHandle { get; }

            public VoxelPaletteHandle PaletteHandle { get; }

            public VoxelRtasHandle OpaqueRtasHandle { get; }

            public VoxelRtasHandle TransparentRtasHandle { get; }
        }
    }
}
