# Manager

## Purpose

`Manager` contains voxel lifecycle managers that own concrete runtime resources.

These managers sit above the raw `Data` layer and are responsible for turning
CPU-side voxel structures into the GPU-facing views that rendering systems
consume.

## Current Types

- `VoxelModelManager`: synchronous GPU residency manager for `VoxelModel`
- `VoxelPaletteManager`: synchronous GPU residency manager for `VoxelPalette`

## VoxelModelManager Rules

`VoxelModelManager` exposes a handle-based API:

- callers provide a `VoxelModel` to acquire a `VoxelModelHandle`
- callers use the handle to read a `VoxelModelGpuView`
- callers release the handle when they no longer need the GPU resources

The current design is fully synchronous. Acquiring or synchronizing a model
finishes the buffer rebuild on the calling thread before returning.

Each `VoxelModelGpuView` contains two independent `VoxelBLAS` views:

- one for opaque chunks
- one for transparent chunks

Each `VoxelBLAS` owns its own three `GraphicsBuffer` instances:

- `VolumeBuffer`
- `AabbDescBuffer`
- `AabbBuffer`

`VolumeBuffer` stores only packed chunk voxel bytes. It does not prepend a
per-chunk header.

`AabbDescBuffer` and `AabbBuffer` are one-to-one. The descriptor at index `i`
describes which uploaded chunk the AABB at index `i` belongs to inside that
BLAS `VolumeBuffer`.

## VoxelPaletteManager Rules

`VoxelPaletteManager` exposes the same handle-based residency flow:

- callers provide a `VoxelPalette` to acquire a `VoxelPaletteHandle`
- callers use the handle to read a `VoxelPaletteGpuView`
- callers release the handle when they no longer need the GPU resources

Each `VoxelPaletteGpuView` currently owns one `GraphicsBuffer`:

- `ColorBuffer`: 256 packed `VoxelColor` entries uploaded as RGBA bytes
