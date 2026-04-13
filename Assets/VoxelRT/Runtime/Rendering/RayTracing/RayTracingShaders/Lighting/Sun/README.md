# Lighting Sun

## Purpose

`VoxelOccupancyProceduralLightingSun` is the directional sunlight pass inside
the `Lighting` shader family.

It starts from the visible GBuffer surface and computes one shadow-tested
directional-light factor.

## Behavior

- reads `_VoxelRtNormal` and `_VoxelRtDepth`
- reads the current shared lighting value written by AO
- reconstructs the visible world-space surface point
- traces one opaque-only shadow ray toward the sun direction
- jitters the shadow-ray origin to approximate softer shadow edges
- adds a single-channel directional-light term back into the shared lighting RT

## Outputs

- `_VoxelRtLighting`
  single-channel lighting value containing `ao + NdotL * shadowVisibility`

## Integration Note

This pass owns the sun-light ray generation and miss shader entry points, but
its RTAS hit-stage pass is hosted by the shared voxel surface material:

- `../../Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

That shared material exposes the ray tracing pass:

- `VoxelLightingSun`
