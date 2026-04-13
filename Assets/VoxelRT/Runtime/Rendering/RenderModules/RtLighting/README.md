# RtLighting

## Purpose

`RtLightingModule` is the first camera-facing lighting composition module.

In this step it simply chains:

1. `GenGbufferCore`
2. `GenAoCore`
3. `GenSunLightCore`

That keeps the module orchestration-level, while the actual render work stays
inside detachable cores.

## Responsibilities

- run the voxel RT GBuffer generation core
- run the AO generation core on top of that GBuffer
- run the sun-light generation core on top of that GBuffer
- keep the shared lighting target alive while submodules render
- preview one selected lighting-stage output to the camera target

## Preview Targets

- `AO`
- `SunLight`
- `Albedo`
- `Normal`
- `Depth`
- `SurfaceInfo`

`AO` preview captures the shared `_VoxelRtLighting` before the sun pass modifies
it, then treats that scalar value as ambient visibility and displays
`white * visibility`, so open space stays bright and nearby occlusion darkens
the preview.

`SunLight` preview captures the shared `_VoxelRtLighting` after the sun pass has
added its contribution, then displays that scalar lighting value as
`white * lighting`.

For debugging clarity, the module first snapshots the selected lighting stage
into an intermediate `_VoxelRtScalarPreview` color target and then blits that
preview target to the camera output.

That preview conversion is implemented with a material blit, so the preview
shader must explicitly declare a `_MainTex` property. Without that property,
Unity's `CommandBuffer.Blit(source, dest, material)` path does not reliably bind
the source texture to the preview shader, which can collapse the output to a
flat color.

## Outputs

- `_VoxelRtAlbedo`
- `_VoxelRtNormal`
- `_VoxelRtDepth`
- `_VoxelRtSurfaceInfo`
- `_VoxelRtLighting`
  shared single-channel lighting texture written by AO and then updated by SunLight
- `_VoxelRtScalarPreview`
  debug-only color preview generated from a lighting-stage snapshot

## Integration Note

This module assumes the scene can satisfy both ray tracing material pass names:

- GBuffer: `VoxelOccupancyDXR`
- AO: `VoxelLightingAO`
- Sun: `VoxelLightingSun`

Today both passes are expected to come from the same voxel instance material:

- `../../RayTracing/RayTracingShaders/Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

The module asset provided in this folder is:

- `RtLightingModule.asset`
