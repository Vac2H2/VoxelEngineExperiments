# RenderPipeline

## Purpose

`RenderPipeline` is the SRP host for voxel render features.

It is responsible for:

- owning the `VoxelRenderPipelineAsset` / `VoxelRenderPipeline` entry point
- running module lifecycle across pipeline, frame, and camera scopes
- letting ordered root modules compete to take over camera rendering
- supporting nested submodules under a parent module
- preserving Unity SRP begin/end callbacks so external listeners continue to work

It is not responsible for:

- hardcoding one concrete render path
- owning voxel upload, residency, or RTAS scene lifetime
- forcing every module to render

## Module Contract

`VoxelRenderPipelineAsset` owns the ordered root module list.

`VoxelRenderPipeline` visits those roots in order for each camera until one module handles rendering.

`VoxelRenderPipelineModule` exposes:

- pipeline create / dispose hooks
- frame begin / end hooks
- camera begin / end hooks
- `OnRender(...)` for render takeover
- `RenderSubmodules(...)` for parent-controlled delegation

`VoxelRenderPipelineModuleGroup` is the minimal container module when a parent only wants to compose submodules.

## Topology Rules

The module graph is intentionally a tree:

- a module asset may appear only once in the pipeline graph
- circular references are rejected when the pipeline is created
- if a parent module is disabled, its child modules do not execute

## Current Fallback

If no module handles a camera, the host performs only a camera clear plus `Submit()`.

That keeps the SRP valid as a shell while concrete render modules are still being added.
