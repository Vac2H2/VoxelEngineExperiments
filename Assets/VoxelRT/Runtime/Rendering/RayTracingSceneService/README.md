# RayTracingSceneService

## Purpose

`RayTracingSceneService` is the runtime module that owns a Unity `RayTracingAccelerationStructure` and synchronizes registered scene instances into it.

It is responsible for:

- creating and owning one RTAS object
- translating mesh and procedural registration requests into Unity RTAS instance adds
- hiding Unity's transient RTAS instance handle from external systems
- mapping stable external `sceneInstanceId` values to internal RTAS handles
- applying incremental RTAS instance updates
- tracking whether a rebuild is pending

It is not responsible for:

- allocating stable `sceneInstanceId` values
- deciding whether an object should use mesh or procedural registration
- preparing voxel residency, palette residency, or AABB data
- scene visibility policy outside of RTAS settings and per-instance properties

## Public API

The public contract is [`IRayTracingSceneService.cs`](./IRayTracingSceneService.cs).

It exposes:

- `RayTracingAccelerationStructure AccelerationStructure`
- `bool HasPendingBuild`
- `void RegisterMeshInstance(int sceneInstanceId, in RayTracingMeshInstanceRegistration registration)`
- `void RegisterProceduralInstance(int sceneInstanceId, in RayTracingProceduralInstanceRegistration registration)`
- `void UnregisterInstance(int sceneInstanceId)`
- `void Clear()`
- `void UpdateInstanceTransform(int sceneInstanceId, Matrix4x4 localToWorld)`
- `void UpdateInstanceMask(int sceneInstanceId, uint mask)`
- `void UpdateInstanceShaderId(int sceneInstanceId, uint shaderInstanceId)`
- `void UpdateInstancePropertyBlock(int sceneInstanceId, MaterialPropertyBlock materialProperties)`
- `void MarkInstanceGeometryDirty(int sceneInstanceId)`
- `void Build()`
- `void Build(Vector3 relativeOrigin)`
- `void Build(CommandBuffer commandBuffer)`
- `void Build(CommandBuffer commandBuffer, Vector3 relativeOrigin)`

## Identity Model

This module deliberately uses two different identities:

- `sceneInstanceId`
  - stable external runtime identity
  - owned by the caller
  - used for all later updates and removal
- Unity RTAS handle
  - transient backend handle returned by `AddInstance`
  - stored only inside this module

External systems should never cache or depend on the Unity RTAS handle.

## Registration Paths

### Mesh

[`RayTracingMeshInstanceRegistration.cs`](./RayTracingMeshInstanceRegistration.cs) is the mesh registration DTO.

It currently carries:

- mesh and material
- optional material property block
- transform and optional previous transform
- shader-visible `shaderInstanceId`
- instance `mask`
- Unity `layer`
- rendering layer mask
- mesh RTAS mode and build flags
- sub-mesh selection and flags
- triangle culling and motion vector options
- light probe settings

### Procedural

[`RayTracingProceduralInstanceRegistration.cs`](./RayTracingProceduralInstanceRegistration.cs) is the procedural registration DTO.

It currently carries:

- AABB buffer
- AABB count and offset
- material and optional material property block
- transform
- shader-visible `shaderInstanceId`
- instance `mask`
- Unity `layer`
- opaque flag
- dynamic geometry flag
- per-instance build flag override

For voxel rendering, this path is expected to be fed by a higher-level provider that has already prepared residency data and AABB buffers.

## Build Semantics

This module does not automatically rebuild the RTAS after every mutation.

Instead:

- registration sets `HasPendingBuild = true`
- unregister sets `HasPendingBuild = true`
- all supported incremental updates set `HasPendingBuild = true`
- every `Build(...)` overload clears `HasPendingBuild`

This lets a higher-level render loop batch multiple changes before issuing one RTAS build.

Recommended usage:

- use `Build(CommandBuffer)` or `Build(CommandBuffer, Vector3)` from the render path
- avoid issuing a build immediately after every small mutation

## Incremental Updates

The following updates are explicitly supported without re-registration:

- transform
- instance mask
- shader instance id
- material property block
- mesh geometry dirty notification through `MarkInstanceGeometryDirty`

These map to Unity RTAS update APIs on the stored internal handle.

## Re-registration Boundaries

The following changes should be treated as unregister + register:

- mesh instance switched to procedural instance
- procedural instance switched to mesh instance
- Unity `layer` change
- mesh/material/AABB source replacement that changes registration shape
- any change that requires a different registration DTO than the original one

This module currently does not keep enough source state to rebuild a registration in place.

## Configuration

[`RayTracingSceneServiceConfiguration.cs`](./RayTracingSceneServiceConfiguration.cs) defines RTAS-wide settings.

It currently controls:

- `RayTracingModeMask`
- `LayerMask`
- static geometry build flags
- dynamic geometry build flags

The service always creates the RTAS in Unity `ManagementMode.Manual`.

The default configuration is:

- `RayTracingModeMask.Everything`
- `LayerMask = ~0`
- static build flags = `PreferFastTrace`
- dynamic build flags = `PreferFastBuild`

## LayerMask vs Instance Mask

Do not mix these two concepts:

- RTAS `LayerMask`
  - scene-level filter in RTAS settings
  - determines which Unity layers the RTAS accepts
- instance `mask`
  - per-instance ray tracing mask
  - intended for shader and ray filtering semantics

`LayerMask` belongs to RTAS configuration.
`mask` belongs to the registration DTO and later `UpdateInstanceMask(...)`.

## RayTracingModeMask

`RayTracingModeMask` is an RTAS-level filter on Unity ray tracing update modes.

It is not a custom gameplay or rendering classification mask.

Use it to decide which Unity `RayTracingMode` categories a given RTAS should accept.
Do not use it to encode your own voxel, mesh, terrain, or material categories.

## Internal Structure

The module currently contains these internal responsibilities:

- `RayTracingSceneServiceConfiguration`
  - RTAS-wide creation settings
- `RayTracingMeshInstanceRegistration`
  - mesh registration contract
- `RayTracingProceduralInstanceRegistration`
  - procedural registration contract
- `RayTracingSceneService`
  - RTAS ownership, `sceneInstanceId -> handle` mapping, updates, and builds

External systems should depend only on `IRayTracingSceneService`.

## Integration Recommendation

Use this module as the RTAS backend only.

Recommended layering:

1. residency / geometry preparation
   - `ModelChunkResidency`
   - `PaletteChunkResidency`
   - procedural AABB preparation
2. scene instance registry
   - owns stable `sceneInstanceId`
   - decides mesh vs procedural registration
3. `RayTracingSceneService`
   - performs actual RTAS registration and updates
4. render loop
   - binds the RTAS
   - issues `Build(CommandBuffer)` when `HasPendingBuild` is true

This keeps data preparation, scene identity, and RTAS backend ownership separated.
