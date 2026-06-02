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
- `VoxelGbufferEncoding.hlsl`: shared project-local encoding helpers for gbuffer normal+roughness data
- `VoxelGbuffer.raytrace`: ray generation shader that writes the current voxel gbuffer
- `VoxelRtao.raytrace`: ray generation shader that writes the current AO `HitDist` output from the current gbuffer surface using the current `vec2` STBN time slice
- `VoxelSunLight.raytrace`: ray generation shader that writes RGB direct sunlight from `RenderSettings.sun` or fallback sun settings, modulated by the current artist sunlight tint
- `VoxelReflection.raytrace`: ray generation shader that writes first-bounce weighted reflection color from gbuffer surfaces
- `VoxelReflectionSpatialDenoise.shader`: 3x3/5x5 normal/depth/roughness-guided spatial filter for reflection noise
- `VoxelRtaoFrameAverage.shader`: raw light composition and 3x3 edge-aware spatial filtering for quick denoise validation
- `NRD/*`: inactive experimental denoise assets kept for reference while custom AO denoising is developed

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

`VoxelGbuffer.raytrace` also applies the current procedural puddle controls as a
material-layer override. Upward-facing hits sample low-frequency world-space
noise; covered pixels get darker diffuse albedo and lower roughness in
`Normal.a`, which makes the reflection pass pick them up as wet surfaces.

`VoxelRtao.raytrace` currently writes one AO front-end value:

- `HitDist = averageHitDistance`

Preview code may visualize that as `saturate(HitDist / maxDistance)`.
The active pipeline currently composes raw diffuse light as:

`sunlight.rgb + ambientLight.rgb * ambientVisibility * saturate(HitDist / maxDistance)`

`VoxelReflection.raytrace` writes `_VoxelEngineRawReflectionColor`. It reflects
the camera ray around the gbuffer normal, jitters the reflection ray by roughness
for a stable screen-space grain pattern, traces opaque scene geometry, samples
the equirectangular sky on misses, and weights the result by dielectric Fresnel
and smoothness.
`VoxelReflectionSpatialDenoise.shader` filters that raw result into
`_VoxelEngineReflectionColor` with gbuffer normal, depth, and roughness as guides.

NRD guide packing is disconnected. `VoxelRtaoFrameAverage.shader` exposes raw
light and denoised light; the denoised output reflects the current spatial
filter switch.

`VoxelGbufferPreview.shader` combines `(Albedo * DenoisedLight) +
ReflectionColor` for the lit color preview. GBuffer albedo is already decoded
from sRGB to linear in `VoxelGbuffer.raytrace`, so preview composition stays in
linear space and leaves display conversion to Unity's render target path.
When a pixel has no surface, the lit preview draws the current camera background
or the assigned equirectangular HDR sky, then adds a procedural sun disk and halo
based on the sunlight direction.
It also exposes `RawAO` and `Smooth` previews: `RawAO` reads `HitDist` directly
and normalizes it by the AO max distance, while `Smooth` visualizes
`1 - Normal.a` so procedural wet areas can be inspected.
