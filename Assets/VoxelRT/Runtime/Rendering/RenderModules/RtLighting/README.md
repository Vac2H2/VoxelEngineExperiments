# RtLighting

## Purpose

`RtLightingModule` is the first camera-facing lighting composition module.

In this step it simply chains:

1. `GenGbufferCore`
2. `GenAoCore`

That keeps the module orchestration-level, while the actual render work stays
inside detachable cores.

## Responsibilities

- run the voxel RT GBuffer generation core
- run the AO generation core on top of that GBuffer
- keep both temporary target sets alive while submodules render
- preview one selected lighting-stage output to the camera target

## Preview Targets

- `AO`
- `Albedo`
- `Normal`
- `Depth`
- `SurfaceInfo`

`AO` preview treats `_VoxelRtAo` as ambient visibility and displays
`white * visibility`, so open space stays bright and nearby occlusion darkens
the preview.

For debugging clarity, the module first converts `_VoxelRtAo` into an
intermediate `_VoxelRtAoPreview` color target and then blits that preview target
to the camera output.

## Outputs

- `_VoxelRtAlbedo`
- `_VoxelRtNormal`
- `_VoxelRtDepth`
- `_VoxelRtSurfaceInfo`
- `_VoxelRtAo`

## Integration Note

This module assumes the scene can satisfy both ray tracing material pass names:

- GBuffer: `VoxelOccupancyDXR`
- AO: `VoxelLightingAO`

Today both passes are expected to come from the same voxel instance material:

- `../../RayTracing/RayTracingShaders/Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

The module asset provided in this folder is:

- `RtLightingModule.asset`
