# GenSunLightCore

## Purpose

`GenSunLightCore` is the detachable lighting render core that produces a simple
directional-light buffer on top of the current GBuffer results.

## Responsibilities

- validate that the configured sun-light DXR shader can render for the current camera
- resolve one directional sun from `RenderSettings.sun` or fallback settings
- resolve one RGB sun color from `RenderSettings.sun` or fallback settings
- allocate the temporary sun-light target
- bind camera, scene, GBuffer, and sun parameters
- dispatch the sun-light ray generation shader
- write the sun-light result into its dedicated color texture
- release the sun-light target when the owner module is done

## Inputs

- `_VoxelRtNormal`
- `_VoxelRtDepth`

## Output

- `_VoxelRtSunLight`

The current pass stores HDR RGB directional light:

- `0,0,0` means unlit or fully shadowed
- `sunColor` scaled by `NdotL * shadowVisibility` means directly lit

It does not multiply by albedo, so the result stays suitable for later denoise
and composition passes. The default RT format is
`B10G11R11_UFloatPack32`, which keeps the sun-light target compact while still
supporting HDR RGB output. Local lights follow the same split and write their
own dedicated RT rather than accumulating into the sun-light target.
