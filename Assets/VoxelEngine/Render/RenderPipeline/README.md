# RenderPipeline

## Purpose

`RenderPipeline` contains the SRP-facing entry points for the production voxel
engine.

The current implementation is intentionally minimal. This folder now contains
both the SRP asset and the SRP instance so the engine has a concrete Unity
pipeline asset that can create the backend-owning SRP before frame rendering
logic is implemented.

## Current Types

- `VoxelEngineRenderPipelineAsset`: serializes the ray tracing material reference and creates the SRP instance
- `VoxelEngineRenderPipeline`: SRP object that owns one `VoxelEngineRenderBackend`
- `GbufferCore`: serializable render core that fills the current voxel albedo target through DXR

## Pipeline Rules

`VoxelEngineRenderPipelineAsset` currently guarantees two things:

- it is the Unity-facing serialization root for the production voxel render pipeline
- it creates `VoxelEngineRenderPipeline` only when a ray tracing material is assigned
- it serializes the `GbufferCore` configuration used by the runtime pipeline

`VoxelEngineRenderPipeline` currently guarantees two things:

- it owns the lifetime of `VoxelEngineRenderBackend`
- callers can reach voxel render resources through the `RenderBackend` property
- it drives `GbufferCore` once per camera, then blits the generated albedo RT into the camera target

When rendering flow is added later, this folder should stay focused on SRP
integration and frame execution. Resource upload, residency, and RTAS assembly
should remain in the sibling `../RenderBackend` folder.
