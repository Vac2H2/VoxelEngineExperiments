# Shaders

## Purpose

`Shaders` contains shader assets owned by the production voxel engine render
stack.

This folder is where engine-specific materials and shader entry points live when
they belong to `VoxelEngine` rather than the legacy experiment pipeline.

## Current Assets

- `VoxelProceduralShader.shader`: procedural hit shader pass that consumes the voxel RTAS bindings expected by `VoxelRtasManager`
- `VoxelProceduralShader.mat`: material asset used by the current `VoxelEngine` render pipeline asset
- `VoxelProceduralShaderShared.hlsl`: shared traversal and payload code for voxel procedural RTAS shading
- `VoxelGbuffer.raytrace`: ray generation shader that writes the current voxel gbuffer
- `VoxelRtao.raytrace`: ray generation shader that traces one cosine-weighted AO ray per pixel from the current gbuffer surface using the current `vec2` STBN time slice

## Notes

`VoxelProceduralShader` remains the stable binding surface for:

- `_VoxelVolumeBuffer`
- `_VoxelAabbDescBuffer`
- `_VoxelAabbBuffer`
- `_VoxelChunkCount`
- `_VoxelAabbCount`
- `_VoxelPaletteColorBuffer`
- `_VoxelPaletteColorCount`
- `_VoxelOpaqueMaterial`

`VoxelGbuffer.raytrace` and `VoxelRtao.raytrace` use that binding contract to
trace the current RTAS against the production voxel scene.
