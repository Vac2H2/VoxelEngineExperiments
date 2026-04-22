# EXPERIENCE

This file records implementation and debugging experience accumulated in this repository.

Each entry should capture:
- what went wrong
- why it happened
- how it was diagnosed
- how it was fixed
- what to check first next time

## 2026-04-22 - RTAO `NHD` preview was pure black

### Symptoms

- `NHD` in the in-game debug UI was pure black.
- This happened after refactoring RTAO to output only normalized hit distance.
- Older RTAO paths had worked before, so the ray tracing path itself was probably still valid.

### What actually went wrong

`RtaoCore` was binding G-buffer `normal` and `viewZ` to the ray tracing shader using property-name render targets instead of the real persistent `RenderTexture` instances.

That was wrong because:
- `GbufferCore` creates `albedo` and `depth` as temporary RTs.
- `normal`, `viewZ`, and `motion` are persistent `RenderTexture` objects.
- `SetRayTracingTextureParam` with `VoxelGbufferIds.NormalTarget` / `VoxelGbufferIds.ViewZTarget` made Unity try to find temporary RTs with those names.

As a result, Unity failed to bind valid textures for `normal` and `viewZ`, `VoxelRtao.raytrace` could not load a valid surface, and `_VoxelEngineNhd` was written as `0` across the frame.

### Key evidence

`Editor.log` reported:

- `temporary render texture _VoxelEngineGbufferNormal not found while executing VoxelEngineRenderPipeline (SetRayTracingTextureParam)`
- `temporary render texture _VoxelEngineGbufferViewZ not found while executing VoxelEngineRenderPipeline (SetRayTracingTextureParam)`

This was the decisive signal. The problem was not NRD normalization, not STBN, and not preview tonemapping. It was a texture-binding mismatch.

### Fix

Change `RtaoCore.Record(...)` to receive `GbufferCore` directly and bind the actual textures:

- bind `gbufferCore.NormalTexture` instead of `VoxelGbufferIds.NormalTarget`
- bind `gbufferCore.ViewZTexture` instead of `VoxelGbufferIds.ViewZTarget`

Also update SRP to pass `_gbufferCore` into `RtaoCore.Record(...)`.

### Why this bug appeared

The refactor mixed two resource models:

- temporary RTs addressed by shader property ID
- persistent `RenderTexture` instances addressed by object reference

The old code path had both kinds of resources in the same G-buffer setup, and the new RTAO path incorrectly treated all of them like temporary RTs.

### What to check first next time

When a new debug view or ray tracing output becomes uniformly black:

1. Check `Editor.log` for `SetRayTracingTextureParam`, missing RT, shader import, or unsupported format errors.
2. Verify whether each bound texture is a temporary RT or a persistent `RenderTexture`.
3. For persistent textures, bind the actual texture object, not just a property-name `RenderTargetIdentifier`.
4. Only after binding is confirmed, investigate shader math such as normalization, bias, or sampling.

### Takeaway

In this project, G-buffer resources are not all allocated the same way. For DXR paths, binding mode must match allocation mode. If a shader suddenly outputs pure black after a refactor, inspect the resource binding path before touching the rendering math.
