# GenComposeLightingCore

## Purpose

`GenComposeLightingCore` multiplies the denoised additive lighting term with
albedo to produce the final color RT for preview and presentation.

## Inputs

- `_VoxelRtLightingAfterSpatial`
- `_VoxelRtAlbedo`

## Output

- `_VoxelRtFinalColor`

## Behavior

The current compose is:

```text
FinalColor = Albedo * LightingAfterSpatial
```
