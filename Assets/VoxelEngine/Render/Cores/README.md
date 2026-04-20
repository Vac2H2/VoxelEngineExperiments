# Cores

## Purpose

`Cores` contains small render-pass building blocks used by the production voxel
render pipeline.

These types should stay focused on recording one concrete rendering step. They
should not own global GPU residency or SRP lifetime by themselves.

## Current Types

- `GbufferCore`: records the voxel gbuffer ray tracing pass for albedo, normal, depth, and motion
- `RtaoCore`: records the voxel RTAO ray tracing pass from gbuffer depth and world-space normal

## Gbuffer Notes

- `Albedo` must remain HDR-friendly and default to a 16-bit-per-channel format (`R16G16B16A16_SFloat`)
- do not downshift `Albedo` to 8-bit storage for bandwidth savings; future HDR lighting and composition need the extra precision
- `Normal` is world-space and stored as `RGB8`
- `Depth` is stored as far-clip-normalized linear depth in `R16_UNorm`
- `Motion` stores 2.5D screen-space motion as `(uv_prev - uv_cur, viewZ_prev - viewZ_cur)` in a 16-bit float format

## RTAO Notes

- `RtaoCore` outputs one-channel 16-bit hit distance in world units
- traced rays write the first-hit distance; miss rays write the configured max distance
- the AO pass can dispatch at full resolution or half resolution
- `RtaoCore` exposes configurable rays-per-pixel; when `RPP > 1`, the output stores the average hit distance across all AO rays for that pixel
- per-frame ray directions come from the current `stbn_vec2_*` time slice in `Assets/STBN`, then feed cosine-weighted hemisphere sampling around the gbuffer normal
- `RtaoCore` only binds the `vec2` STBN sequence; the cosine hemisphere sampler is the only stage that turns that random pair into an AO direction
- multi-ray sampling stays on the same STBN slice, but each ray uses a different texel offset so one pixel does not reuse the same noise value for every ray
- STBN inputs should stay linear, point-sampled, uncompressed, and without mipmaps; `RtaoCore.EditorAutoAssignDependencies()` enforces that import setup in the editor

## Responsibilities

- validating that the current pipeline has the render resources needed for one pass
- allocating and releasing pass-local temporary targets
- binding camera and backend state to shaders
- recording one concrete render pass into a command buffer
