# Lighting AO

## Purpose

`VoxelOccupancyProceduralLightingAO` is the ambient-occlusion pass inside the
`Lighting` shader family.

It traces cosine-weighted hemisphere rays from the visible GBuffer surface and
outputs a scalar ambient visibility value.

## Behavior

- reads `_VoxelRtNormal` and `_VoxelRtDepth`
- reconstructs the visible world-space surface point
- traces opaque-only secondary rays
- treats nearby hits as stronger occlusion
- treats misses or distant hits as higher visibility

## Outputs

- `_VoxelRtAo`
  single-channel AO visibility in the `[0, 1]` range

## Integration Note

This pass owns the AO ray generation and miss shader entry points, but its RTAS
hit-stage pass is hosted by the shared voxel surface material:

- `../../Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

That shared material exposes the ray tracing pass:

- `VoxelLightingAO`
