# NRD

## Purpose

`NRD` contains the production voxel engine's denoiser-facing integration layer.

This folder sits between raw ray tracing outputs and final AO consumption. It
owns denoiser bridge contracts, SRP-side data packing, and denoise-specific
render cores. It should stay focused on denoiser integration concerns rather
than general render-backend lifetime.

## Layout

- `Bridge`: native-plugin interop entry points and availability checks
- `Data`: blittable frame/settings contracts passed to the native side
- `Cores`: SRP-facing orchestration such as `RtaoDenoiseCore`

## Current State

- `RtaoDenoiseCore` is inserted after raw `RtaoCore` output and before preview/composite consumption
- `GbufferCore` now exposes the guides `NRD` needs on the C# side:
  - `IN_NORMAL_ROUGHNESS` via `normal.rgb + roughness.a`
  - `IN_VIEWZ`
  - `IN_MV`
- the native bridge is D3D12-and-Windows gated and falls back cleanly when the native plugin is unavailable
- `RtaoDenoiseCore` now packs AO-resolution NRD inputs, caches native texture pointers, queues per-camera frame data, and issues the native render-thread plugin event
- `RtaoDenoiseCore` outputs denoised AO from `REBLUR_DIFFUSE_OCCLUSION`:
  - raw front-end input = `NormHitDist`
  - denoised output = normalized hit distance used directly as AO
- the native backend is a real `NRDIntegration` / `NRI` path hosted by `Plugins/NRDPlugin`

## Vendor Pin

- official source: `NVIDIA-RTX/NRD`
- vendored location: `ThirdParty/NRD`
- pinned tag: `v4.17.2`
- pinned commit: `88b0457f35fa2961688a7602e3807cad66e8bb00`

## Follow-Ups

- keep the vendored `ThirdParty/NRD` pin in sync with the shader-side `Assets/VoxelEngine/Render/Shaders/NRD/Vendor` copies of `NRD.hlsli` and `NRDConfig.hlsli`
- extend the native path with validation/debug outputs only if they are going to be consumed in-engine
