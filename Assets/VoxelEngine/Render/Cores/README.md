# Cores

## Purpose

`Cores` contains small render-pass building blocks used by the production voxel
render pipeline.

These types should stay focused on recording one concrete rendering step. They
should not own global GPU residency or SRP lifetime by themselves.

## Current Types

- `GbufferCore`: records the voxel gbuffer ray tracing pass for albedo, normal+roughness, depth, viewZ, and motion
- `RtaoCore`: records the voxel AO front-end pass and keeps the current `HitDist` output alive for later consumption
- `RtaoDenoiseCore`: prepares NRD guide inputs and outputs denoised AO through the native NRD backend when that path is enabled

## Gbuffer Notes

- `Albedo` must remain HDR-friendly and default to a 16-bit-per-channel format (`R16G16B16A16_SFloat`)
- do not downshift `Albedo` to 8-bit storage for bandwidth savings; future HDR lighting and composition need the extra precision
- `Normal` is world-space and stored as `RGB8`; `Normal.a` is reserved for linear roughness and currently uses a stable temporary default
- `Depth` is stored as the primary-ray hit distance in world units in `R16_SFloat`
- `ViewZ` is stored separately as linear view-space depth in `R16_SFloat` for denoiser guides; it is the primary surface distance projected onto the camera forward axis, not NDC z and not hardware depth
- `Motion` stores 2.5D screen-space motion as `(uv_prev - uv_cur, viewZ_prev - viewZ_cur)` in a 16-bit float format

## RTAO Notes

- `RtaoCore` currently owns one output: `HitDist`
- `HitDist` is defined as the average AO hit distance in world units, clamped by the AO `Max Distance`
- the AO term uses cosine-weighted hemisphere rays around the gbuffer world normal
- per-frame ray directions come from the current `stbn_vec2_*` time slice in `Assets/STBN`
- when `RPP > 1`, rays stay on the same STBN slice, but each ray uses a different texel offset so one pixel does not reuse the same noise value every time
- STBN inputs should stay linear, point-sampled, uncompressed, and without mipmaps; `RtaoCore.EditorAutoAssignDependencies()` enforces that import setup in the editor
- preview code may display `HitDist / MaxDistance`, but that visualization is not the stored value itself
- `RtaoDenoiseCore` currently has a lightweight preview path that converts `HitDist + ViewZ` into `NormHitDist` with `REBLUR_FrontEnd_GetNormHitDist`
- `NormHitDist` uses the official default `ReblurHitDistanceParameters` from `ThirdParty/NRD/Include/NRDSettings.h`: `A = 3.0`, `B = 0.1`, `C = 20.0`
- `RtaoDenoiseCore` feeds that `NormHitDist` into `REBLUR_DIFFUSE_OCCLUSION`, and the native output is consumed directly as denoised AO

## Responsibilities

- validating that the current pipeline has the render resources needed for one pass
- allocating and releasing pass-local temporary targets
- binding camera and backend state to shaders
- recording one concrete render pass into a command buffer
