# Manager

## Purpose

`Manager` contains voxel lifecycle managers that own concrete runtime resources.

These managers sit above the raw `Data` layer and are responsible for turning
CPU-side voxel structures into the GPU-facing views that rendering systems
consume.

## Current Types

- `VoxelModelManager`: synchronous GPU residency manager for `VoxelModel`
- `VoxelPaletteManager`: synchronous GPU residency manager for `VoxelPalette`
- `VoxelRtasManager`: RTAS registration manager for backend-provided voxel BLAS views

## VoxelModelManager Rules

`VoxelModelManager` exposes a handle-based API:

- callers first try to retain an existing residency entry through `TryRetain(AssetReferenceVoxelModel, out VoxelModelHandle)`
- callers add a new residency entry through `Add(AssetReferenceVoxelModel)` when the referenced model is not already resident
- callers use the handle to read a `VoxelModelGpuView`
- callers release the handle when they no longer need the GPU resources

The current design is fully synchronous. Acquiring or synchronizing a model
finishes the Addressables load, CPU deserialization, and GPU buffer rebuild on
the calling thread before returning.

`VoxelModelKey` remains the internal residency identity.

For addressable models, that key is derived from the referenced asset GUID. That
allows repeated loads of the same source asset to share one GPU residency entry.

`VoxelModelManager` does not keep CPU-side `VoxelModel` data resident after a
successful upload. It reloads the referenced `VoxelModelAsset`, deserializes a
temporary `VoxelModel`, builds GPU buffers, and immediately releases the CPU
staging data.

Each `VoxelModelGpuView` contains two independent `VoxelVolumeGpuView` entries:

- one for opaque chunks
- one for transparent chunks

Each `VoxelVolumeGpuView` owns its own three `GraphicsBuffer` instances:

- `VolumeBuffer`
- `AabbDescBuffer`
- `AabbBuffer`

`VolumeBuffer` stores only packed chunk voxel bytes. It does not prepend a
per-chunk header.

`AabbDescBuffer` and `AabbBuffer` are one-to-one. The descriptor at index `i`
describes which uploaded chunk the AABB at index `i` belongs to inside that
volume view `VolumeBuffer`.

## VoxelPaletteManager Rules

`VoxelPaletteManager` exposes the same handle-based residency flow:

- callers first try to retain an existing residency entry through `TryRetain(AssetReferenceVoxelPalette, out VoxelPaletteHandle)`
- callers add a new residency entry through `Add(AssetReferenceVoxelPalette)` when the referenced palette is not already resident
- callers use the handle to read a `VoxelPaletteGpuView`
- callers release the handle when they no longer need the GPU resources

`VoxelPaletteKey` remains the internal residency identity.

For addressable palettes, that key is derived from the referenced asset GUID.
That allows repeated loads of the same source asset to share one GPU residency
entry.

`VoxelPaletteManager` does not keep CPU-side `VoxelPalette` data resident after
a successful upload. It reloads the referenced `VoxelPaletteAsset`,
deserializes a temporary `VoxelPalette`, builds the GPU buffer, and immediately
releases the CPU staging data.

Each `VoxelPaletteGpuView` currently owns one `GraphicsBuffer`:

- `ColorBuffer`: 256 packed `VoxelColor` entries uploaded as RGBA bytes

## VoxelRtasManager Rules

`VoxelRtasManager` owns the procedural RTAS wrapper used by the production
engine.

It does not define voxel BLAS data by itself. The complete voxel BLAS-facing
view is assembled by the render backend and passed in as `VoxelBlasGpuView`.

That aggregated BLAS view includes:

- one `VoxelModelGpuView`
- one `VoxelPaletteGpuView`

When `AddInstance` is called, `VoxelRtasManager` chooses either the opaque or
transparent `VoxelVolumeGpuView`, then builds the Unity
`RayTracingAABBsInstanceConfig` and `MaterialPropertyBlock` needed for RTAS
registration.
