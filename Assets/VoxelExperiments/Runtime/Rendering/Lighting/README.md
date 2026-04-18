# Lighting Components

## Purpose

`Rendering/Lighting/` contains scene-facing lighting components that are owned
by the voxel renderer rather than Unity's built-in light system.

## Current Components

- `VoxelLocalLight`
  custom local-light component consumed by `GenLocalLightCore`

## Usage

Add `VoxelLocalLight` to a scene object and choose one of the currently
supported shapes:

- `Point`
- `Spot`
- `Sphere`

`Point` and `Spot` evaluate as deterministic single-sample lights. `Sphere`
uses stochastic surface sampling inside the local-light DXR pass and therefore
can produce soft shadows and visible sampling noise when sample counts are low.
