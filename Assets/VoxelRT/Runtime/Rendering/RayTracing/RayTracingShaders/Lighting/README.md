# Lighting

## Purpose

`Lighting/` contains voxel DXR shader passes that operate on top of the
generated GBuffer.

## Layout

- `Shared/`
  reusable lighting-family include files
- `AO/`
  ambient-occlusion pass assets and pass-local helpers
- `Sun/`
  directional sun-light pass assets and pass-local helpers
- `Local/`
  local direct-light pass assets and pass-local helpers

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
  hemisphere-traced ambient occlusion writing a dedicated scalar AO RT
- `Sun/`
  shadow-tested directional sunlight writing a dedicated HDR RGB sun-light RT
- `Local/`
  shadow-tested point and spot lights writing a dedicated HDR RGB local-light RT

The passes are intentionally split by signal type:

- ambient visibility stays in AO
- direct directional light stays in Sun
- direct local light stays in Local

That separation keeps future local-light work additive and avoids overloading a
single lighting RT with unrelated semantics.
