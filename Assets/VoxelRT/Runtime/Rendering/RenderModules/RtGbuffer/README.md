# RtGbuffer

## Purpose

`RtGbufferModule` renders the voxel ray-traced GBuffer set for the active camera.

It is responsible for:

- dispatching the `Gbuffer` ray tracing shader family
- allocating the full temporary GBuffer set for the camera
- exposing those temporary textures as globals for the rest of the camera render
- previewing one selected GBuffer target to the camera target when no submodule takes over

It is not responsible for:

- voxel instance registration
- palette or surface-type residency ownership
- lighting or shading after the GBuffer fill

## Outputs

The module writes these temporary global textures:

- `_VoxelRtGBuffer0`
- `_VoxelRtGBuffer1`
- `_VoxelRtGBuffer2`
- `_VoxelRtSurfaceInfo`

Their layout matches the `Gbuffer` shader family contract:

- `GBuffer0`: `RGBA8`, `rgb = albedo`, `a = reserved`
- `GBuffer1`: `RGBA8`, `rgb = encoded normal`, `a = reserved`
- `GBuffer2`: `R16_SFloat`, `r = linear view depth`
- `SurfaceInfo`: `RGBA8`, `rgba = reflectivity, smoothness, metallic, emissive`

## Integration Note

This module expects voxel instances to use the `Gbuffer` procedural shader family
for their `VoxelOccupancyDXR` material pass. The provided material asset is:

- `../../RayTracing/RayTracingShaders/Gbuffer/VoxelOccupancyProceduralGbuffer.mat`

The module asset provided in this folder is:

- `RtGbufferModule.asset`

If no submodule handles the camera after the GBuffer fill, `RtGbufferModule`
blits the selected preview target to the camera target.
