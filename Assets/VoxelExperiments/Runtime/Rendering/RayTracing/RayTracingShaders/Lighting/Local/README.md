# Lighting Local

## Purpose

`VoxelOccupancyProceduralLightingLocal` is the local direct-light pass inside
the `Lighting` shader family.

## Behavior

- reads `_VoxelExperimentsNormal` and `_VoxelExperimentsDepth`
- reconstructs the visible world-space surface point
- loops over a prefiltered GPU candidate-light buffer
- rejects lights with cheap per-pixel tests before tracing any occlusion ray
- casts one opaque-only shadow ray per surviving light when that light has shadows enabled
- accumulates HDR RGB direct-light contribution into its own RT

## Supported Lights

- `Point`
- `Spot`
- `Sphere`

The current implementation consumes scene `VoxelLocalLight` components and uses
one candidate list for the whole camera. It does not yet build per-tile or
per-cluster light lists.

`Sphere` is the first stochastic area-light shape in the pipeline. It samples
points on the light surface, so it can produce soft shadows and sampling noise.

## Outputs

- `_VoxelExperimentsLocalLight`
  HDR RGB direct-local-light term containing the accumulated visible local-light
  contribution for the current pixel

## Integration Note

This pass owns the local-light ray generation and miss shader entry points, but
its RTAS hit-stage pass is hosted by the shared voxel surface material:

- `../../Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

That shared material exposes the ray tracing pass:

- `VoxelLightingLocal`
