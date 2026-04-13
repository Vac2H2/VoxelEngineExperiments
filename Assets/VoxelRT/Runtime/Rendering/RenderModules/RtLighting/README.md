# RtLighting

## Purpose

`RtLightingModule` is the first camera-facing lighting composition module.

In this step it simply chains:

1. `GenGbufferCore`
2. `GenAoCore`
3. `GenSunLightCore`
4. `GenLocalLightCore`

That keeps the module orchestration-level, while the actual render work stays
inside detachable cores.

## Responsibilities

- run the voxel RT GBuffer generation core
- run the AO generation core on top of that GBuffer
- run the sun-light generation core on top of that GBuffer
- run the local-light generation core on top of that GBuffer
- preview one selected lighting-stage output to the camera target

## Preview Targets

- `AO`
- `SunLight`
- `Albedo`
- `Normal`
- `Depth`
- `SurfaceInfo`
- `LocalLight`

`AO` preview reads the dedicated `_VoxelRtAo` scalar RT, then treats that value
as ambient visibility and displays `white * visibility`, so open space stays
bright and nearby occlusion darkens the preview.

`SunLight` preview now reads the dedicated `_VoxelRtSunLight` color RT directly.
That target already stores HDR RGB sunlight, so no scalar-to-color preview
conversion is needed.

`LocalLight` preview reads the dedicated `_VoxelRtLocalLight` color RT directly.
That target stores HDR RGB local direct-light contribution.

For debugging clarity, scalar-only stages are first snapped into an intermediate
`_VoxelRtScalarPreview` color target and then blitted to the camera output.
Today that path is only used by `AO`.

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
- `_VoxelRtAo`
  dedicated single-channel AO visibility texture
- `_VoxelRtSunLight`
  dedicated HDR RGB sunlight texture
- `_VoxelRtLocalLight`
  dedicated HDR RGB local-light texture
- `_VoxelRtScalarPreview`
  debug-only color preview generated from a scalar lighting-stage snapshot

Separating AO and SunLight keeps the ambient-visibility signal distinct from
the direct-light signal. Local lights follow the same rule and keep their own
target instead of overloading one shared RT.

## Integration Note

This module assumes the scene can satisfy both ray tracing material pass names:

- GBuffer: `VoxelOccupancyDXR`
- AO: `VoxelLightingAO`
- Sun: `VoxelLightingSun`
- Local: `VoxelLightingLocal`

Today both passes are expected to come from the same voxel instance material:

- `../../RayTracing/RayTracingShaders/Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

The module asset provided in this folder is:

- `RtLightingModule.asset`
