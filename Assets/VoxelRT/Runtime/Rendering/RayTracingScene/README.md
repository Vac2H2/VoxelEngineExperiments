# RayTracingScene

## Purpose

`RayTracingScene` is the runtime module that owns one Unity `RayTracingAccelerationStructure` and manages RTAS instances directly by Unity handle.

It is responsible for:

- creating and owning one RTAS object
- adding both mesh and procedural instances through one unified descriptor
- returning the Unity RTAS instance `handle` as the only public runtime instance identity
- applying incremental RTAS updates by `handle`
- rebuilding the RTAS on demand

It is not responsible for:

- shared geometry residency or caching
- model or palette lifetime
- scene-level asset ids outside of the returned RTAS `handle`
- shader resource binding

## Public API

The public contract is [`IRayTracingScene.cs`](./IRayTracingScene.cs).

It exposes:

- `RayTracingAccelerationStructure AccelerationStructure`
- `bool HasPendingBuild`
- `int AddInstance(in RayTracingSceneInstanceDescriptor descriptor)`
- `int RecreateInstance(int handle, in RayTracingSceneInstanceDescriptor descriptor)`
- `void RemoveInstance(int handle)`
- `void Clear()`
- `void UpdateTransform(int handle, Matrix4x4 localToWorld)`
- `void UpdateMask(int handle, uint mask)`
- `void UpdateShaderId(int handle, uint shaderInstanceId)`
- `void UpdateMaterialPropertyBlock(int handle, MaterialPropertyBlock materialProperties)`
- `void MarkGeometryDirty(int handle)`
- `Build(...)` overloads

## Instance Model

`RayTracingScene` deliberately does not introduce a second public scene-instance id.

The only public runtime identity is the Unity RTAS `handle` returned by `AddInstance(...)`.

This keeps the instance lifecycle simple:

- add returns a `handle`
- hot updates use that same `handle`
- remove uses that same `handle`
- re-registration returns a new `handle`

## Descriptor Model

[`RayTracingSceneInstanceDescriptor.cs`](./RayTracingSceneInstanceDescriptor.cs) combines:

- one `RayTracingGeometryDescriptor`
- transform and optional previous transform
- shader-visible `shaderInstanceId`
- per-instance ray mask
- Unity `layer`
- optional per-instance `MaterialPropertyBlock`

This keeps geometry shape and instance state together at the RTAS boundary without requiring a separate registry layer.

The geometry descriptor types now live in this same module:

- [`RayTracingGeometryDescriptor.cs`](./RayTracingGeometryDescriptor.cs)
- [`RayTracingGeometryKind.cs`](./RayTracingGeometryKind.cs)
- [`RayTracingMeshGeometryDescriptor.cs`](./RayTracingMeshGeometryDescriptor.cs)
- [`RayTracingProceduralGeometryDescriptor.cs`](./RayTracingProceduralGeometryDescriptor.cs)

## Recreate Semantics

`RecreateInstance(...)` is the explicit path for changes that require unregister + register semantics, such as:

- switching mesh to procedural
- switching procedural to mesh
- changing layer when the registration shape must be rebuilt
- replacing geometry source

Because Unity returns a new RTAS `handle` on add, recreate also returns a new `handle`.

## Configuration

[`RayTracingSceneConfiguration.cs`](./RayTracingSceneConfiguration.cs) defines RTAS-wide settings.

It controls:

- `RayTracingModeMask`
- `LayerMask`
- static geometry build flags
- dynamic geometry build flags

The RTAS is always created in Unity `ManagementMode.Manual`.

## Integration Recommendation

Recommended layering:

1. resource residency / geometry preparation
2. caller-owned geometry descriptor assembly
3. caller-owned material payload assembly
4. `RayTracingScene`
5. render-path RTAS build and shader dispatch

This keeps resource ids, shader payloads, and RTAS instance lifecycle separate.
