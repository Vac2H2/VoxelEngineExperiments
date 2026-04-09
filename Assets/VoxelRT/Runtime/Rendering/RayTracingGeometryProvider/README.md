# RayTracingGeometryProvider

## Purpose

`RayTracingGeometryProvider` defines the shared geometry contract that sits between geometry preparation and RTAS scene-instance registration.

It is responsible for:

- describing shared ray tracing geometry independently from scene instance state
- separating mesh geometry from procedural geometry at the contract level
- giving higher-level systems a stable read-side interface for resolving `sharedGeometryId`

It is not responsible for:

- allocating `sceneInstanceId`
- owning a `RayTracingAccelerationStructure`
- registering anything into RTAS
- storing transform, layer, mask, or shader instance id

## Public API

The core interface is [`IRayTracingGeometryProvider.cs`](./IRayTracingGeometryProvider.cs).

It exposes:

- `RayTracingGeometryKind GeometryKind`
- `bool TryGetGeometryDescriptor(int sharedGeometryId, out RayTracingGeometryDescriptor descriptor)`

## Contract Shape

The provider returns a [`RayTracingGeometryDescriptor`](./RayTracingGeometryDescriptor.cs), which is a tagged union over:

- [`RayTracingMeshGeometryDescriptor`](./RayTracingMeshGeometryDescriptor.cs)
- [`RayTracingProceduralGeometryDescriptor`](./RayTracingProceduralGeometryDescriptor.cs)

These descriptors intentionally contain only shared geometry data.

They do not contain scene-instance data such as:

- transform
- Unity layer
- instance mask
- shader-visible instance id

Those belong to the future `RayTracingInstanceRegistry`.

## Why This Layer Exists

Without this layer, `RayTracingInstanceRegistry` would need to know concrete voxel residency, palette residency, AABB cache, mesh source, and Unity RTAS registration details all at once.

With this layer:

1. geometry preparation modules create and maintain shared geometry
2. providers expose shared geometry through a small stable contract
3. the instance registry combines:
   - shared geometry descriptor
   - scene instance state
4. the registry calls `RayTracingSceneService`

This keeps geometry preparation and scene instance lifetime separated.

## Expected Integration

Recommended layering:

1. shared geometry preparation
   - mesh geometry cache
   - voxel procedural geometry cache
   - residency services
2. `IRayTracingGeometryProvider`
   - resolves `sharedGeometryId -> geometry descriptor`
3. `RayTracingInstanceRegistry`
   - owns `sceneInstanceId`
   - adds transform, layer, mask, and shader id
4. `RayTracingSceneService`
   - performs actual RTAS registration and updates

## Notes

- A concrete voxel provider will typically return `Procedural` descriptors.
- A concrete mesh provider will typically return `Mesh` descriptors.
- A provider may own retention or caching internally, but this contract only defines the read-side shape that the registry needs.
