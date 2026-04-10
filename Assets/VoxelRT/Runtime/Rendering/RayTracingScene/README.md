# RayTracingScene

## Purpose

`RayTracingScene` is the runtime module that owns one Unity `RayTracingAccelerationStructure` and manages procedural RTAS instances directly by Unity handle.

It is responsible for:

- creating and owning one RTAS object
- adding procedural instances from Unity's native `RayTracingAABBsInstanceConfig`
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
- `int AddInstance(in RayTracingAABBsInstanceConfig config, Matrix4x4 localToWorld)`
- `void RemoveInstance(int handle)`
- `void Clear()`
- `void UpdateTransform(int handle, Matrix4x4 localToWorld)`
- `void UpdateMask(int handle, uint mask)`
- `void UpdateMaterialPropertyBlock(int handle, MaterialPropertyBlock materialProperties)`
- `void MarkGeometryDirty(int handle)`
- `Build(...)` overloads

## Instance Model

`RayTracingScene` deliberately does not introduce a second public scene-instance id or a wrapper descriptor layer.

The only public runtime identity is the Unity RTAS `handle` returned by `AddInstance(...)`.

This keeps the instance lifecycle simple:

- add returns a `handle`
- hot updates use that same `handle`
- remove uses that same `handle`
- re-registration still means remove + add at the caller level when needed

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
2. caller-owned native procedural config assembly
3. caller-owned material payload assembly
4. `RayTracingScene`
5. render-path RTAS build and shader dispatch

This keeps resource ids, shader payloads, and RTAS instance lifecycle separate.
