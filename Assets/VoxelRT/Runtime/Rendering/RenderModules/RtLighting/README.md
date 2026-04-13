# RtLighting

## Purpose

`RtLightingModule` is the first camera-facing lighting composition module.

In this step it chains:

1. `GenGbufferCore`
2. `GenAoCore`
3. `GenSunLightCore`
4. `GenLocalLightCore`
5. `GenLightAdditiveCore`
6. `GenDenoiseLightingCore`
7. `GenComposeLightingCore`

That keeps the module orchestration-level, while the actual render work stays
inside detachable cores.

## Responsibilities

- run the voxel RT GBuffer generation core
- run the AO generation core on top of that GBuffer
- run the sun-light generation core on top of that GBuffer
- run the local-light generation core on top of that GBuffer
- merge ambient AO contribution, sunlight, and local lights into one additive lighting RT
- run temporal denoise on that additive lighting RT
- run spatial denoise on the temporal result
- compose the denoised lighting with albedo
- preview one selected lighting-stage output to the camera target

## Preview Targets

- `AO`
- `SunLight`
- `Albedo`
- `Normal`
- `Depth`
- `SurfaceInfo`
- `LocalLight`
- `LightingRaw`
- `LightingAfterTemporal`
- `LightingAfterSpatial`
- `FinalColor`

`AO` preview reads the dedicated `_VoxelRtAo` scalar RT, then treats that value
as ambient visibility and displays `white * visibility`, so open space stays
bright and nearby occlusion darkens the preview.

`SunLight` preview now reads the dedicated `_VoxelRtSunLight` color RT directly.
That target already stores HDR RGB sunlight, so no scalar-to-color preview
conversion is needed.

`LocalLight` preview reads the dedicated `_VoxelRtLocalLight` color RT directly.
That target stores HDR RGB local direct-light contribution.

`LightingRaw` preview shows the additive lighting term before denoise:

- `ambientColor * AO`
- `SunLight`
- `LocalLight`

`LightingAfterTemporal` preview shows the reprojected history blend before the
spatial cleanup pass.

`LightingAfterSpatial` preview shows the temporally accumulated lighting after
the edge-aware spatial filter.

`FinalColor` preview shows the denoised lighting multiplied by albedo.

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
- `_VoxelRtVelocity`
  screen-space motion vector derived from current and previous camera clip transforms
- `_VoxelRtAo`
  dedicated single-channel AO visibility texture
- `_VoxelRtSunLight`
  dedicated HDR RGB sunlight texture
- `_VoxelRtLocalLight`
  dedicated HDR RGB local-light texture
- `_VoxelRtLightingRaw`
  additive lighting term before denoise
- `_VoxelRtLightingAfterTemporal`
  additive lighting term after temporal reprojection and history blend
- `_VoxelRtLightingAfterSpatial`
  additive lighting term after spatial denoise
- `_VoxelRtFinalColor`
  denoised lighting multiplied by albedo
- `_VoxelRtScalarPreview`
  debug-only color preview generated from a scalar lighting-stage snapshot

AO, sunlight, and local lights stay separate while they are generated. They are
then merged in `GenLightAdditiveCore` so temporal and spatial denoise can
operate on one unified additive lighting term before the final albedo compose.

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
