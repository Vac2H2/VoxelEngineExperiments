# RenderBackend

## Purpose

`RenderBackend` owns the voxel engine's render-facing runtime resources.

This folder is the bridge between lifecycle-managed GPU uploads and the SRP
layer. Its job is to turn resident voxel model and palette resources into the
complete BLAS-facing views consumed by RTAS registration.

## Current Types

- `VoxelEngineRenderBackend`: owner of model, palette, and RTAS resource orchestration
- `VoxelEngineRenderInstanceHandle`: handle for one logical render instance
- `VoxelBlasGpuView`: complete BLAS-facing view composed from one model GPU view and one palette GPU view

## Backend Rules

`VoxelEngineRenderBackend` is the authority for voxel render resource lifetime.

`AddInstance(AssetReferenceVoxelModel, AssetReferenceVoxelPalette, Matrix4x4)` performs the full synchronous
load path:

- tries to retain the model through `VoxelModelManager` using the addressable `VoxelModelAsset` reference, then adds it on a miss
- tries to retain the palette through `VoxelPaletteManager` using the addressable `VoxelPaletteAsset` reference, then adds it on a miss
- builds one aggregated `VoxelBlasGpuView`
- registers the opaque and transparent volume views into `VoxelRtasManager` as needed

One logical render instance may therefore create up to two RTAS instances:

- one for opaque volume data
- one for transparent volume data

`VoxelBlasGpuView` is intentionally backend-defined. It represents the complete
set of GPU resources required for voxel RTAS registration:

- `Model`: one `VoxelModelGpuView`
- `Palette`: one `VoxelPaletteGpuView`

That means `VoxelModelManager` only owns volume-level GPU uploads. It does not
own the final BLAS semantics used by rendering.

## Ownership Boundary

`RenderBackend` may depend on `../../LifeCycle` managers, but those managers
should not depend back on `RenderBackend`.
