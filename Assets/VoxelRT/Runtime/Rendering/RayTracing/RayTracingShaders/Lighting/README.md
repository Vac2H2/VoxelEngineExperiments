# Lighting

## Purpose

`Lighting/` contains voxel DXR shader passes that operate on top of the
generated GBuffer.

## Layout

- `Shared/`
  reusable lighting-family include files
- `AO/`
  ambient-occlusion pass assets and pass-local helpers

## Design Rule

Each lighting pass lives in its own folder and stays self-contained on the
dispatch side:

- one pass-local `.raytrace` for ray generation and miss shaders
- optional pass-local helpers and material assets

Shared code that is useful across multiple lighting passes belongs under
`Shared/`.

Ray tracing hit-stage passes are hosted by the shared voxel instance surface
shader used by `VoxelRenderer`. At the moment that shared shader is still the
`Gbuffer/VoxelOccupancyProceduralGbuffer` surface shader and material.

## Current Passes

- `AO/`
  hemisphere-traced ambient occlusion over the voxel scene
