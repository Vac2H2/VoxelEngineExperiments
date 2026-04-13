# GenLightAdditiveCore

## Purpose

`GenLightAdditiveCore` merges the raw lighting terms into one additive lighting
RT that has not yet been multiplied by albedo.

## Inputs

- `_VoxelRtAo`
- `_VoxelRtSunLight`
- `_VoxelRtLocalLight`

## Output

- `_VoxelRtLightingRaw`

## Behavior

The current additive merge is:

```text
LightingRaw = ambientColor * AO + SunLight + LocalLight
```

`ambientColor` currently defaults to white, so AO behaves as a white ambient
visibility term until a different ambient tint is configured.
