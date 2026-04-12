# VoxelRenderer

## Purpose

`VoxelRenderer` is the component-level runtime module that owns one voxel instance lifecycle.

It is responsible for:

- reading CPU-side voxel data from `VoxelFilter`
- retaining model and palette resources through `VoxelGpuResourceSystem`
- building the procedural geometry descriptor directly from retained model resources
- assembling the per-instance `MaterialPropertyBlock`
- submitting one procedural instance to `RayTracingScene`
- tracking the returned RTAS `handle`
- updating transform and property-block changes over the instance lifetime
- doing the same registration path in both edit mode and play mode
- assigning the internal RTAS instance mask used for opaque/transparent retraces

It is not responsible for:

- global voxel buffer binding
- global RTAS ownership
- model/palette residency internals
- non-voxel mesh instances
- an intermediate geometry provider layer

## Component Pair

This module intentionally mirrors Unity's `MeshFilter` / `MeshRenderer` split:

- `VoxelFilter`
  - references one `VoxelModel` and one `VoxelPalette`
  - re-raises asset changes into renderer-facing dirty signals
  - exposes retain keys and upload helpers
- `VoxelRenderer`
  - owns GPU residency coordination, RTAS registration, and instance material payload

## Property Block Semantics

`VoxelRenderer` exposes Unity-style:

- `GetPropertyBlock(...)`
- `SetPropertyBlock(...)`

External systems can write user-facing material properties through this API.

`VoxelRenderer` then merges those user properties with system-owned fields such as:

- `_VoxelModelResidencyId`
- `_VoxelPaletteResidencyId`

before pushing the final block to `RayTracingScene`.

## Internal Instance Mask

`VoxelRenderer` no longer exposes a user-facing RTAS mask.

The RTAS instance mask is now internal:

- opaque instance = `1`
- transparent instance = `2`

Primary rays trace against both bits, and transparent pass-through retraces use
the opaque bit only.

## Shared Runtime Bootstrap

`VoxelRuntimeBootstrap` is the scene-level composition root. It owns one pure `VoxelRuntime`, which in turn owns the shared services:

- `VoxelGpuResourceSystem`
- `RayTracingScene`
- `VoxelRayTracingResourceBinder`

There should be exactly one active bootstrap in the scene.

In edit mode and play mode, `VoxelRenderer` still uses the same retain/register/property-block path. The difference is only how often Unity drives updates.
