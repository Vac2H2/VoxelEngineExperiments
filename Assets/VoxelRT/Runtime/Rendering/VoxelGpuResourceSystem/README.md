# VoxelGpuResourceSystem

## Purpose

`VoxelGpuResourceSystem` is the high-level runtime module that aggregates all voxel GPU data resources required for shading.

It is responsible for:

- owning model chunk residency
- owning model procedural AABB residency coordination
- owning palette chunk residency
- owning the global surface-type table
- exposing all bindable voxel GPU buffers through one read-side contract
- exposing one façade for retaining and releasing voxel GPU resources by resource domain

It is not responsible for:

- RTAS scene registration
- scene instance identity
- procedural AABB generation
- RTAS geometry lifetime

## Public API

The write-side contract is [`IVoxelGpuResourceSystem.cs`](./IVoxelGpuResourceSystem.cs).

The read-side contract is [`IVoxelGpuResourceView.cs`](./IVoxelGpuResourceView.cs).

The system exposes:

- all bindable voxel GPU buffers
- stride metadata for the start buffers and surface-type table
- `VoxelModelResourceDescriptor GetModelResourceDescriptor(int modelResidencyId)`
- `int RetainModel(object modelKey, in VoxelModelUpload upload)`
- `void ReleaseModel(int modelResidencyId)`
- `int RetainPalette(object paletteKey, NativeArray<byte> paletteBytes)`
- `void ReleasePalette(int paletteResidencyId)`
- `void UpdateSurfaceTypes(NativeArray<uint> packedEntries)`

## Internal Ownership

This module owns three lower-level services:

- `ModelChunkResidency`
- `ModelProceduralAabb`
- `PaletteChunkResidency`
- `SurfaceTypeTableService`

It intentionally hides those services behind one aggregated view so higher-level modules do not need to know how voxel GPU resources are split internally.

## Why There Is No Voxel Asset Id

This module deliberately does not invent a synthetic `voxelGpuAssetId`.

That abstraction was removed because it incorrectly bound together:

- model chunk residency
- palette residency
- surface-type table ownership

Those are different resource domains with different sharing behavior.

The correct ownership model is:

- model data is retained by `modelKey`
- model procedural AABB data is retained together with model data
- palette data is retained by `paletteKey`
- surface types are updated as one global table

Only the model domain is coordinated inside this module, because model chunk data and model procedural AABB data share the same lifetime.

Palette remains independent.

## Buffer Contract

The read-side view exposes:

- `OccupancyChunkBuffer`
- `VoxelDataChunkBuffer`
- `ModelChunkStartBuffer`
- `PaletteChunkBuffer`
- `PaletteChunkStartBuffer`
- `SurfaceTypeTableBuffer`

These buffers are intended for global binding in the render path.

They are not intended to be bound per instance through `MaterialPropertyBlock`.

## Runtime Semantics

### Model Retain/Release

`RetainModel(...)` coordinates two lower-level services:

- `ModelChunkResidency`
- `ModelProceduralAabb`

It:

- deduplicates by `modelKey`
- validates that `ChunkAabbs.Length == ChunkCount`
- returns a `modelResidencyId`
- owns both model chunk residency and procedural AABB lifetime through one model-domain retain/release

`GetModelResourceDescriptor(modelResidencyId)` returns the model-domain read-side descriptor:

- `modelResidencyId`
- `chunkCount`
- `proceduralAabbBuffer`
- `proceduralAabbCount`

`ReleaseModel(...)` releases both lower-level model-domain resources on the final release.

### Palette Retain/Release

`RetainPalette(...)` forwards to `PaletteChunkResidency`:

- deduplicates by `paletteKey`
- returns a `paletteResidencyId`
- owns palette GPU residency lifetime through retain/release

`ReleasePalette(...)` forwards to `PaletteChunkResidency.Release(...)`.

### Surface Type Updates

`UpdateSurfaceTypes(...)` replaces the global surface-type table contents as a whole.

This is not a residency operation.

## Integration Recommendation

Recommended layering:

1. `VoxelGpuResourceSystem`
   - owns voxel GPU data resources by domain
2. `VoxelProceduralGeometryProvider`
   - consumes the read-side `IVoxelGpuResourceView`
   - translates model-domain resources into RTAS procedural descriptors
3. `RayTracingInstanceRegistry`
   - owns scene instance ids and instance state
4. `RayTracingSceneService`
   - performs RTAS registration and updates
5. render binder
   - globally binds the current voxel GPU buffers before dispatch

This keeps shader data resources separate from RTAS geometry resources.
