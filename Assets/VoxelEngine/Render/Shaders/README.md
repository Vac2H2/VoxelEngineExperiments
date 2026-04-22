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
- `VoxelGbuffer.raytrace`: ray generation shader that writes the current voxel gbuffer and packs `IN_NORMAL_ROUGHNESS` with the official `NRD_FrontEnd_PackNormalAndRoughness` helper
- `VoxelRtao.raytrace`: ray generation shader that writes the current AO `HitDist` output from the current gbuffer surface using the current `vec2` STBN time slice
- `NRD/VoxelNrdGuidePack.shader`: downscales or copies full-resolution gbuffer guides into AO-resolution NRD guide textures
- `NRD/VoxelRtaoDenoiseComposite.shader`: passes through the current denoised-or-raw AO result
- `NRD/Vendor/NRD.hlsli` and `NRD/Vendor/NRDConfig.hlsli`: Unity-visible copies of the official vendor front-end helpers used by the engine shaders

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

`VoxelRtao.raytrace` currently writes one AO front-end value:

- `HitDist = averageHitDistance`

Preview code may visualize that as `saturate(HitDist / maxDistance)`.
`NRD/VoxelNrdGuidePack.shader` is responsible for generating previewable
`NormHitDist` from `HitDist + ViewZ` via `REBLUR_FrontEnd_GetNormHitDist`.
`RtaoDenoiseCore` then feeds that texture into `REBLUR_DIFFUSE_OCCLUSION`,
whose `OUT_DIFF_HITDIST` is consumed directly as denoised AO.
