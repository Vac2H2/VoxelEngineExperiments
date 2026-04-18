# Lighting Sun

## Purpose

`VoxelOccupancyProceduralLightingSun` is the directional sunlight pass inside
the `Lighting` shader family.

It starts from the visible GBuffer surface and computes one shadow-tested
directional-light term.

## Behavior

- reads `_VoxelExperimentsNormal` and `_VoxelExperimentsDepth`
- reconstructs the visible world-space surface point
- traces one opaque-only shadow ray toward the sun direction
- jitters the shadow-ray origin to approximate softer shadow edges
- multiplies shadow visibility by `NdotL` and the configured RGB sun color
- writes the resulting HDR RGB directional-light term into its own RT

## Outputs

- `_VoxelExperimentsSunLight`
  HDR RGB sunlight containing `sunColor * NdotL * shadowVisibility`

## Integration Note

This pass owns the sun-light ray generation and miss shader entry points, but
its RTAS hit-stage pass is hosted by the shared voxel surface material:

- `../../Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

That shared material exposes the ray tracing pass:

- `VoxelLightingSun`
