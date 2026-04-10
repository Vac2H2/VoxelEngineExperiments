using System;
using Unity.Collections;
using UnityEngine;
using VoxelRT.Runtime.Data;
using VoxelRT.Runtime.Rendering.ModelProceduralAabb;
using VoxelRT.Runtime.Rendering.VoxelGpuResourceSystem;

namespace VoxelRT.Runtime.Rendering.VoxelRenderer
{
    [Flags]
    public enum VoxelFilterChangeFlags
    {
        None = 0,
        Model = 1 << 0,
        Palette = 1 << 1,
        All = Model | Palette,
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VoxelFilter : MonoBehaviour
    {
        [SerializeField] private VoxelModel _model;
        [SerializeField] private VoxelPalette _palette;

        private object _modelKeyToken = new object();
        private object _paletteKeyToken = new object();
        private VoxelModel _subscribedModel;
        private VoxelPalette _subscribedPalette;

        public event Action<VoxelFilterChangeFlags> Changed;

        public VoxelModel Model => _model;

        public VoxelPalette Palette => _palette;

        public object ModelKey => _model != null ? _model.ResidencyKey : _modelKeyToken;

        public object PaletteKey => _palette != null ? _palette.ResidencyKey : _paletteKeyToken;

        public uint ChunkCount => _model != null ? _model.ChunkCount : 0u;

        public void MarkModelDirty()
        {
            if (_model != null)
            {
                _model.InvalidateResidency();
                return;
            }

            _modelKeyToken = new object();
            Changed?.Invoke(VoxelFilterChangeFlags.Model);
        }

        public void MarkPaletteDirty()
        {
            if (_palette != null)
            {
                _palette.InvalidateResidency();
                return;
            }

            _paletteKeyToken = new object();
            Changed?.Invoke(VoxelFilterChangeFlags.Palette);
        }

        public void MarkAllDirty()
        {
            if (_model != null)
            {
                _model.InvalidateResidency();
            }
            else
            {
                _modelKeyToken = new object();
            }

            if (_palette != null)
            {
                _palette.InvalidateResidency();
                return;
            }

            _paletteKeyToken = new object();
            Changed?.Invoke(VoxelFilterChangeFlags.All);
        }

        public VoxelModelUpload CreateModelUpload(
            Allocator allocator,
            out NativeArray<byte> occupancyBytes,
            out NativeArray<byte> voxelBytes,
            out NativeArray<ModelChunkAabb> chunkAabbs)
        {
            ValidateModelData();
            return _model.CreateUpload(allocator, out occupancyBytes, out voxelBytes, out chunkAabbs);
        }

        public NativeArray<byte> CreatePaletteBytes(Allocator allocator)
        {
            ValidatePaletteData();
            return _palette.CreateBytes(allocator);
        }

        private void Awake()
        {
            SynchronizeAssetSubscriptions();
        }

        private void OnEnable()
        {
            SynchronizeAssetSubscriptions();
        }

        private void OnDisable()
        {
            UnsubscribeFromAssets();
        }

        private void OnValidate()
        {
            SynchronizeAssetSubscriptions();
        }

        private void ValidateModelData()
        {
            if (_model == null)
            {
                throw new InvalidOperationException("VoxelFilter model asset is not assigned.");
            }

            _model.ValidateData();
        }

        private void ValidatePaletteData()
        {
            if (_palette == null)
            {
                throw new InvalidOperationException("VoxelFilter palette asset is not assigned.");
            }
        }

        private void SynchronizeAssetSubscriptions()
        {
            if (_subscribedModel != _model)
            {
                if (_subscribedModel != null)
                {
                    _subscribedModel.Changed -= HandleModelAssetChanged;
                }

                _subscribedModel = _model;

                if (_subscribedModel != null)
                {
                    _subscribedModel.Changed += HandleModelAssetChanged;
                }
            }

            if (_subscribedPalette != _palette)
            {
                if (_subscribedPalette != null)
                {
                    _subscribedPalette.Changed -= HandlePaletteAssetChanged;
                }

                _subscribedPalette = _palette;

                if (_subscribedPalette != null)
                {
                    _subscribedPalette.Changed += HandlePaletteAssetChanged;
                }
            }
        }

        private void UnsubscribeFromAssets()
        {
            if (_subscribedModel != null)
            {
                _subscribedModel.Changed -= HandleModelAssetChanged;
                _subscribedModel = null;
            }

            if (_subscribedPalette != null)
            {
                _subscribedPalette.Changed -= HandlePaletteAssetChanged;
                _subscribedPalette = null;
            }
        }

        private void HandleModelAssetChanged()
        {
            Changed?.Invoke(VoxelFilterChangeFlags.Model);
        }

        private void HandlePaletteAssetChanged()
        {
            Changed?.Invoke(VoxelFilterChangeFlags.Palette);
        }
    }
}
