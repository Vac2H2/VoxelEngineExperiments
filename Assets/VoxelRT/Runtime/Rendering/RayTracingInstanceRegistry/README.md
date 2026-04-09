# RayTracingInstanceRegistry

## Purpose

`RayTracingInstanceRegistry` is the runtime module that owns scene-instance lifecycle for ray tracing.

It sits between:

- shared geometry providers
- `RayTracingSceneService`

It is responsible for:

- allocating stable `sceneInstanceId`
- storing per-instance state
- resolving `sharedGeometryId` through a geometry provider
- choosing mesh or procedural RTAS registration based on resolved geometry kind
- deciding when a change is a hot update versus a re-registration

It is not responsible for:

- owning shared geometry data
- owning voxel residency or palette residency
- owning the RTAS backend implementation details

## Public API

The public contract is [`IRayTracingInstanceRegistry.cs`](./IRayTracingInstanceRegistry.cs).

It exposes:

- `RayTracingAccelerationStructure AccelerationStructure`
- `bool HasPendingBuild`
- `int RegisterInstance(IRayTracingGeometryProvider geometryProvider, int sharedGeometryId, in RayTracingSceneInstanceRegistration registration)`
- `void UnregisterInstance(int sceneInstanceId)`
- `void Clear()`
- `void UpdateInstanceTransform(int sceneInstanceId, Matrix4x4 localToWorld)`
- `void UpdateInstanceMask(int sceneInstanceId, uint mask)`
- `void UpdateInstanceShaderId(int sceneInstanceId, uint shaderInstanceId)`
- `void UpdateInstanceLayer(int sceneInstanceId, int layer)`
- `void UpdateInstancePropertyBlock(int sceneInstanceId, MaterialPropertyBlock materialProperties)`
- `void RebindSharedGeometry(int sceneInstanceId, IRayTracingGeometryProvider geometryProvider, int sharedGeometryId)`
- `Build(...)` pass-through overloads

## Registration Model

Initial instance state is provided through [`RayTracingSceneInstanceRegistration.cs`](./RayTracingSceneInstanceRegistration.cs).

This contains instance-level data only:

- current transform
- optional previous transform
- shader-visible instance id
- per-instance mask
- Unity layer
- optional instance material property block

Shared geometry is supplied separately:

- `IRayTracingGeometryProvider geometryProvider`
- `int sharedGeometryId`

This split is intentional:

- provider owns shared geometry
- registry owns scene instance state

## Update Semantics

Hot updates:

- transform
- mask
- shader instance id
- material property block

Re-registration updates:

- Unity layer change
- shared geometry rebinding

The registry stores enough state to rebuild the scene-service registration when re-registration is required.

## Identity Model

`sceneInstanceId` is the only public runtime identity for a registered scene instance.

Internally:

- the registry maps `sceneInstanceId -> instance record`
- `RayTracingSceneService` maps `sceneInstanceId -> Unity RTAS handle`

External systems should not depend on Unity RTAS handles.

## Integration Recommendation

Recommended layering:

1. shared geometry preparation
2. `IRayTracingGeometryProvider`
3. `RayTracingInstanceRegistry`
4. `RayTracingSceneService`
5. render loop build/bind

This keeps shared geometry identity, scene instance identity, and RTAS backend ownership separated.
