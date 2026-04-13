# GenLocalLightCore

## Purpose

`GenLocalLightCore` is the detachable lighting render core that produces a
dedicated local-light buffer on top of the current GBuffer results.

## Responsibilities

- validate that the configured local-light DXR shader can render for the current camera
- collect scene `VoxelLocalLight` components into a GPU candidate buffer
- apply coarse camera-relevance culling before uploading lights to the GPU
- bind camera, scene, GBuffer, local-light buffer, and shading parameters
- dispatch the local-light ray generation shader
- write the local-light result into its dedicated HDR color texture
- release the local-light RT when the owner module is done

## Inputs

- `_VoxelRtNormal`
- `_VoxelRtDepth`

## Output

- `_VoxelRtLocalLight`

The current pass stores HDR RGB direct-light contribution from local lights
only. It does not multiply by albedo and it does not include AO or sunlight.

CPU-side culling is intentionally coarse. The current implementation rejects
lights that are disabled, non-local, zero-intensity, zero-range, or outside a
camera-relevant bounding volume. The shader then performs the cheaper
per-pixel tests first:

- range
- `NdotL`
- spot cone
- minimum contribution threshold

Only lights that pass those tests cast an occlusion ray.

## Supported Shapes

- `Point`
- `Spot`
- `Sphere`

`Point` and `Spot` stay deterministic single-sample lights. `Sphere` performs
multi-sample surface sampling in the local-light shader and can therefore
produce soft shadows and visible noise.
