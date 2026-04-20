# Render

## Purpose

`Render` contains the production voxel engine's rendering-facing orchestration.

This folder sits above `../LifeCycle` and is responsible for assembling the GPU
resource views that rendering and ray tracing consume, then exposing those
systems through pipeline-facing entry points.

## Current Layout

- `Cores`: small render building blocks that record concrete render passes
- `Debug`: scene-facing gizmo helpers for inspecting voxel render data
- `RenderBackend`: render resource aggregation and render-instance lifetime management
- `RenderPipeline`: SRP entry points that own and expose the backend
- `Shaders`: engine-owned placeholder and production shader assets used by the render stack

## Responsibilities

- defining the render-facing BLAS contract for voxel content
- aggregating model and palette GPU views into backend-owned render resources
- owning the SRP-side objects that access voxel rendering state

## Non-Goals

`Render` should not redefine the canonical voxel data structures.

Those live in the sibling `../Data` folder.
