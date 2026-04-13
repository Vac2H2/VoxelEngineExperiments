# GenSunLightCore

## Purpose

`GenSunLightCore` is the detachable lighting render core that produces a simple
directional-light factor on top of the current GBuffer results.

## Responsibilities

- validate that the configured sun-light DXR shader can render for the current camera
- resolve one directional sun from `RenderSettings.sun` or fallback settings
- bind camera, scene, GBuffer, and sun parameters
- dispatch the sun-light ray generation shader
- write the sun-light result into the shared lighting texture

## Inputs

- `_VoxelRtNormal`
- `_VoxelRtDepth`

## Output

- `_VoxelRtLighting`

The current pass stores a single-channel white-light factor:

- `0` means unlit or fully shadowed
- `1` means fully lit by the directional light

It does not multiply by albedo or sun color yet, so the result stays suitable
for later denoise and composition passes. The pass reads the current AO value
from the shared lighting RT and adds its own `0..1` sunlight contribution back
into the same single-channel RT. The accumulated lighting value is allowed to go
above `1`.
