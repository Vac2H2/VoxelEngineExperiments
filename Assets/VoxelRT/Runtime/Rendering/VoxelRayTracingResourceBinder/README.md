# VoxelRayTracingResourceBinder

## Purpose

`VoxelRayTracingResourceBinder` is the render-binding module that binds voxel GPU data resources to shader properties.

It is responsible for:

- reading the current voxel GPU buffers from `IVoxelGpuResourceView`
- binding those buffers as global shader resources
- binding those buffers directly to a `RayTracingShader`
- publishing the related integer metadata required by shader code

It is not responsible for:

- owning voxel GPU resources
- retaining or releasing voxel assets
- creating RTAS instances
- binding the RTAS itself

## Public API

The public contract is [`IVoxelRayTracingResourceBinder.cs`](./IVoxelRayTracingResourceBinder.cs).

It exposes:

- `void BindGlobals()`
- `void BindGlobals(CommandBuffer commandBuffer)`
- `void BindRayTracingShader(RayTracingShader rayTracingShader)`
- `void BindRayTracingShader(CommandBuffer commandBuffer, RayTracingShader rayTracingShader)`

## Binding Layout

The property-id layout is defined by [`VoxelRayTracingResourceBindingLayout.cs`](./VoxelRayTracingResourceBindingLayout.cs).

The default property names are:

- `_VoxelOccupancyChunkBuffer`
- `_VoxelDataChunkBuffer`
- `_VoxelModelChunkStartBuffer`
- `_VoxelPaletteChunkBuffer`
- `_VoxelPaletteChunkStartBuffer`
- `_VoxelSurfaceTypeTableBuffer`
- `_VoxelModelChunkStartStrideBytes`
- `_VoxelPaletteChunkStartStrideBytes`
- `_VoxelSurfaceTypeTableStrideBytes`
- `_VoxelSurfaceTypeEntryCount`

## Bound Resources

This binder currently binds:

- occupancy chunk buffer
- voxel data chunk buffer
- model chunk start buffer
- palette chunk buffer
- palette chunk start buffer
- surface type table buffer

It also publishes:

- model chunk start stride bytes
- palette chunk start stride bytes
- surface type table stride bytes
- surface type entry count

## Integration Recommendation

Use this binder from the render path immediately before a ray tracing dispatch or any shader pass that consumes voxel ray tracing resources.

Recommended flow:

1. update/retain voxel assets through `VoxelGpuResourceSystem`
2. before dispatch, call the binder
3. then bind RTAS and dispatch the ray tracing shader

This avoids stale `GraphicsBuffer` references when residency services recreate internal buffers during compaction.

## Notes

- This binder intentionally depends only on `IVoxelGpuResourceView`.
- It does not depend on `VoxelProceduralGeometryProvider`, `RayTracingInstanceRegistry`, or `RayTracingSceneService`.
- The current implementation requires all voxel resource buffers to be available before binding.
