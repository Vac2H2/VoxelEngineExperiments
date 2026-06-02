# Cores

## Purpose

`Cores` contains small render-pass building blocks used by the production voxel
render pipeline.

These types should stay focused on recording one concrete rendering step. They
should not own global GPU residency or SRP lifetime by themselves.

## Current Types

- `GbufferCore`: records the voxel gbuffer ray tracing pass for albedo, normal+roughness, depth, viewZ, and motion
- `RtaoCore`: records the voxel AO front-end pass and keeps the current `HitDist` output alive for later consumption
- `SunLightCore`: records RGB directional sunlight using `RenderSettings.sun` or fallback sun settings, plus artist tint and visible sun-disk controls
- `ReflectionCore`: records first-bounce ray traced reflection color using the gbuffer surface, global roughness, and RTAS albedo hits, then optionally applies a small edge-aware spatial filter
- `RtaoFrameAverageCore`: converts `HitDist` and sunlight into raw light, then optionally applies a small edge-aware spatial filter

## Gbuffer Notes

- `Albedo` must remain HDR-friendly and default to a 16-bit-per-channel format (`R16G16B16A16_SFloat`)
- do not downshift `Albedo` to 8-bit storage for bandwidth savings; future HDR lighting and composition need the extra precision
- `Normal` is world-space and stored as `RGB8`; `Normal.a` stores linear roughness from the current global roughness control, with procedural puddles able to locally lower roughness on upward-facing surfaces
- procedural puddles are a gbuffer material layer: they use low-frequency world-space noise, darken albedo, and make covered surfaces smoother so the existing reflection pass becomes stronger there
- `Depth` is stored as the primary-ray hit distance in world units in `R16_SFloat`
- `ViewZ` is stored separately as linear view-space depth; it is the primary surface distance projected onto the camera forward axis, not NDC z and not hardware depth
- `Motion` stores 2.5D screen-space motion as `(uv_prev - uv_cur, viewZ_prev - viewZ_cur)` in a 16-bit float format

## RTAO Notes

- `RtaoCore` currently owns one output: `HitDist`
- `HitDist` is defined as the average AO hit distance in world units, clamped by the AO `Max Distance`
- the AO term uses cosine-weighted hemisphere rays around the gbuffer world normal
- per-frame ray directions come from the current `stbn_vec2_*` time slice in `Assets/STBN`
- when `RPP > 1`, rays stay on the same STBN slice, but each ray uses a different texel offset so one pixel does not reuse the same noise value every time
- RTAO noise can be animated across STBN slices or fixed to one screen-space STBN slice for stable, non-flickering raw AO inspection
- STBN inputs should stay linear, point-sampled, uncompressed, and without mipmaps; `RtaoCore.EditorAutoAssignDependencies()` enforces that import setup in the editor
- preview code may display `HitDist / MaxDistance`, but that visualization is not the stored value itself
- NRD denoise is currently disconnected from the active SRP path; custom AO denoising should consume `HitDist` plus gbuffer guides directly
- `_VoxelEngineSunLight` is RGB direct sunlight: sun color times artist sunlight tint times `NdotL` times shadow visibility
- `_VoxelEngineRawReflectionColor` is first-bounce reflection color weighted by dielectric Fresnel and smoothness (`1 - roughness`)
- `_VoxelEngineReflectionColor` is the spatially denoised reflection output consumed by final composition
- `_VoxelEngineRawLight` is `sunlight.rgb + ambientLight.rgb * ambientVisibility * saturate(HitDist / MaxDistance)`
- `_VoxelEngineDenoisedLight` is the current-frame denoised light output produced by the spatial filter switch
- `_VoxelEngineSkyTexture` is an optional equirectangular HDR environment texture; the lit preview samples it for background sky, and reflection misses sample it as environment reflection

## Responsibilities

- validating that the current pipeline has the render resources needed for one pass
- allocating and releasing pass-local temporary targets
- binding camera and backend state to shaders
- recording one concrete render pass into a command buffer
