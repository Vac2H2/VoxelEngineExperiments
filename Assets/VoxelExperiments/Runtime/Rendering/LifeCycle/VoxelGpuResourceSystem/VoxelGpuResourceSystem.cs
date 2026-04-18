using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using VoxelExperiments.Runtime.Rendering.ModelChunkResidency;
using VoxelExperiments.Runtime.Rendering.ModelProceduralAabb;
using VoxelExperiments.Runtime.Rendering.PaletteChunkResidency;
using VoxelExperiments.Runtime.Rendering.SurfaceTypeTableService;

namespace VoxelExperiments.Runtime.Rendering.VoxelGpuResourceSystem
{
    public sealed class VoxelGpuResourceSystem : IVoxelGpuResourceSystem
    {
        private readonly IModelChunkResidencyService _modelChunkResidency;
        private readonly IModelProceduralAabbService _modelProceduralAabbService;
        private readonly IPaletteChunkResidencyService _paletteChunkResidency;
        private readonly ISurfaceTypeTableService _surfaceTypeTableService;
        private readonly bool _ownsDependencies;
        private readonly Dictionary<object, ModelEntry> _modelEntriesByKey = new Dictionary<object, ModelEntry>();
        private readonly Dictionary<int, ModelEntry> _modelEntriesByResidencyId = new Dictionary<int, ModelEntry>();
        private bool _isDisposed;

        public VoxelGpuResourceSystem()
            : this(
                new ModelChunkResidencyService(),
                new ModelProceduralAabbService(),
                new PaletteChunkResidencyService(),
                new SurfaceTypeTableService.SurfaceTypeTableService(),
                true)
        {
        }

        internal VoxelGpuResourceSystem(
            IModelChunkResidencyService modelChunkResidency,
            IModelProceduralAabbService modelProceduralAabbService,
            IPaletteChunkResidencyService paletteChunkResidency,
            ISurfaceTypeTableService surfaceTypeTableService,
            bool ownsDependencies)
        {
            _modelChunkResidency = modelChunkResidency ?? throw new ArgumentNullException(nameof(modelChunkResidency));
            _modelProceduralAabbService = modelProceduralAabbService ?? throw new ArgumentNullException(nameof(modelProceduralAabbService));
            _paletteChunkResidency = paletteChunkResidency ?? throw new ArgumentNullException(nameof(paletteChunkResidency));
            _surfaceTypeTableService = surfaceTypeTableService ?? throw new ArgumentNullException(nameof(surfaceTypeTableService));
            _ownsDependencies = ownsDependencies;
        }

        public GraphicsBuffer OccupancyChunkBuffer => _modelChunkResidency.OccupancyChunkBuffer;

        public GraphicsBuffer VoxelDataChunkBuffer => _modelChunkResidency.VoxelDataChunkBuffer;

        public GraphicsBuffer ModelChunkStartBuffer => _modelChunkResidency.ModelChunkStartBuffer;

        public GraphicsBuffer PaletteChunkBuffer => _paletteChunkResidency.PaletteChunkBuffer;

        public GraphicsBuffer PaletteChunkStartBuffer => _paletteChunkResidency.PaletteChunkStartBuffer;

        public GraphicsBuffer SurfaceTypeTableBuffer => _surfaceTypeTableService.SurfaceTypeTableBuffer;

        public uint ModelChunkStartStrideBytes => _modelChunkResidency.ModelChunkStartStrideBytes;

        public uint PaletteChunkStartStrideBytes => _paletteChunkResidency.PaletteChunkStartStrideBytes;

        public uint SurfaceTypeTableStrideBytes => _surfaceTypeTableService.SurfaceTypeTableStrideBytes;

        public uint SurfaceTypeEntryCount => _surfaceTypeTableService.SurfaceTypeEntryCount;

        public int RetainModel(
            object modelKey,
            in VoxelModelUpload upload)
        {
            EnsureNotDisposed();

            if (modelKey == null)
            {
                throw new ArgumentNullException(nameof(modelKey));
            }

            if (upload.ChunkCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(upload), "Chunk count must be greater than zero.");
            }

            if (upload.ChunkAabbs.Length != checked((int)upload.ChunkCount))
            {
                throw new ArgumentException("Chunk AABB count must match model chunk count.", nameof(upload));
            }

            if (_modelEntriesByKey.TryGetValue(modelKey, out ModelEntry existingEntry))
            {
                if (existingEntry.RefCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("Voxel model resource refcount overflow.");
                }

                existingEntry.RefCount++;
                return existingEntry.ModelResidencyId;
            }

            int modelResidencyId = -1;
            int proceduralAabbResidencyId = -1;
            bool modelRetained = false;
            bool proceduralAabbRetained = false;

            try
            {
                modelResidencyId = _modelChunkResidency.Retain(
                    modelKey,
                    upload.ChunkCount,
                    upload.OccupancyBytes,
                    upload.VoxelBytes);
                modelRetained = true;

                proceduralAabbResidencyId = _modelProceduralAabbService.Retain(
                    modelKey,
                    upload.ChunkAabbs);
                proceduralAabbRetained = true;

                ModelEntry newEntry = new ModelEntry(
                    modelKey,
                    modelResidencyId,
                    proceduralAabbResidencyId,
                    upload.ChunkCount);
                _modelEntriesByKey.Add(modelKey, newEntry);
                _modelEntriesByResidencyId.Add(modelResidencyId, newEntry);
                return modelResidencyId;
            }
            catch
            {
                if (proceduralAabbRetained)
                {
                    _modelProceduralAabbService.Release(proceduralAabbResidencyId);
                }

                if (modelRetained)
                {
                    _modelChunkResidency.Release(modelResidencyId);
                }

                throw;
            }
        }

        public void ReleaseModel(int modelResidencyId)
        {
            EnsureNotDisposed();

            ModelEntry entry = GetModelEntry(modelResidencyId);
            if (entry.RefCount > 1)
            {
                entry.RefCount--;
                return;
            }

            _modelEntriesByKey.Remove(entry.ModelKey);
            _modelEntriesByResidencyId.Remove(modelResidencyId);

            _modelProceduralAabbService.Release(entry.ProceduralAabbResidencyId);
            _modelChunkResidency.Release(modelResidencyId);
        }

        public int RetainPalette(
            object paletteKey,
            NativeArray<byte> paletteBytes)
        {
            EnsureNotDisposed();
            return _paletteChunkResidency.Retain(
                paletteKey,
                paletteBytes);
        }

        public void ReleasePalette(int paletteResidencyId)
        {
            EnsureNotDisposed();
            _paletteChunkResidency.Release(paletteResidencyId);
        }

        public void UpdateSurfaceTypes(NativeArray<uint> packedEntries)
        {
            EnsureNotDisposed();
            _surfaceTypeTableService.Update(packedEntries);
        }

        public VoxelModelResourceDescriptor GetModelResourceDescriptor(int modelResidencyId)
        {
            EnsureNotDisposed();

            ModelEntry entry = GetModelEntry(modelResidencyId);
            ModelProceduralAabbDescriptor proceduralAabb = _modelProceduralAabbService.GetDescriptor(entry.ProceduralAabbResidencyId);
            return new VoxelModelResourceDescriptor(
                entry.ModelResidencyId,
                entry.ChunkCount,
                proceduralAabb.AabbBuffer,
                proceduralAabb.AabbCount);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_ownsDependencies)
            {
                _modelChunkResidency.Dispose();
                _modelProceduralAabbService.Dispose();
                _paletteChunkResidency.Dispose();
                _surfaceTypeTableService.Dispose();
            }

            _modelEntriesByKey.Clear();
            _modelEntriesByResidencyId.Clear();
            _isDisposed = true;
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VoxelGpuResourceSystem));
            }
        }

        private ModelEntry GetModelEntry(int modelResidencyId)
        {
            if (modelResidencyId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(modelResidencyId), "Model residency id must be non-negative.");
            }

            if (!_modelEntriesByResidencyId.TryGetValue(modelResidencyId, out ModelEntry entry))
            {
                throw new KeyNotFoundException("The specified model residency id is not resident in VoxelGpuResourceSystem.");
            }

            return entry;
        }

        private sealed class ModelEntry
        {
            public ModelEntry(
                object modelKey,
                int modelResidencyId,
                int proceduralAabbResidencyId,
                uint chunkCount)
            {
                ModelKey = modelKey ?? throw new ArgumentNullException(nameof(modelKey));
                ModelResidencyId = modelResidencyId;
                ProceduralAabbResidencyId = proceduralAabbResidencyId;
                ChunkCount = chunkCount;
                RefCount = 1;
            }

            public object ModelKey { get; }

            public int ModelResidencyId { get; }

            public int ProceduralAabbResidencyId { get; }

            public uint ChunkCount { get; }

            public uint RefCount { get; set; }
        }
    }
}
