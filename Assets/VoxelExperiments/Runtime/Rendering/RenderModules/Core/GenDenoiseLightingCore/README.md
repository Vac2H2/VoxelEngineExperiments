# GenDenoiseLightingCore

## Purpose

`GenDenoiseLightingCore` denoises the additive lighting RT in two stages:

1. temporal reprojection and history blending
2. edge-aware spatial filtering

## Inputs

- `_VoxelExperimentsLightingRaw`
- `_VoxelExperimentsDepth`
- `_VoxelExperimentsNormal`
- `_VoxelExperimentsVelocity`

## Outputs

- `_VoxelExperimentsLightingAfterTemporal`
- `_VoxelExperimentsLightingAfterSpatial`

## History

The core keeps one history set per camera:

- previous denoised lighting
- previous depth
- previous normal
- previous camera position
- previous camera forward

That history is used to validate reprojected samples before blending them into
the current frame.
