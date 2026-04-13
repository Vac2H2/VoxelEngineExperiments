# RenderModules

## Purpose

`RenderModules` contains the reusable render-module layer that plugs into
`../RenderPipeline`.

## Layout Rules

- shared module framework types live at the `RenderModules/` root
- detachable render cores live under `Core/<CoreName>/`
- every concrete render module gets its own folder
- module-local assets stay next to the module that owns them

## Current Modules

- `Core/` contains detachable rendering cores shared by concrete modules
- `ModuleGroup/` is the minimal composition module
- `RtGbuffer/` builds the voxel RT GBuffer set and previews one selected target
- `RtLighting/` composes GBuffer generation and AO generation into one lighting-stage module
- `VoxelRayTracingFullscreen/` renders the camera with the fullscreen DXR path
