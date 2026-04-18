# GenLightAdditiveCore

## Purpose

`GenLightAdditiveCore` merges the raw lighting terms into one additive lighting
RT that has not yet been multiplied by albedo.

## Inputs

- `_VoxelExperimentsAo`
- `_VoxelExperimentsSunLight`
- `_VoxelExperimentsLocalLight`

## Output

- `_VoxelExperimentsLightingRaw`

## Behavior

The current additive merge is:

```text
LightingRaw = ambientColor * AO + SunLight + LocalLight
```

`ambientColor` currently defaults to white, so AO behaves as a white ambient
visibility term until a different ambient tint is configured.
