# RenderModules

## Purpose

`RenderModules` contains the reusable render-module layer that plugs into
`../RenderPipeline`.

## Layout Rules

- shared module framework types live in `Core/`
- every concrete render module gets its own folder
- module-local assets stay next to the module that owns them

## Current Modules

- `ModuleGroup/` is the minimal composition module
- `RtGbuffer/` builds the voxel RT GBuffer set and previews one selected target
- `VoxelRayTracingFullscreen/` renders the camera with the fullscreen DXR path
