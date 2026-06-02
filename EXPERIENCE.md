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

## 2026-04-23 - `NrdGuidePack` was reading the wrong raw `HitDist`

### Symptoms

- `HitDist` preview looked correct.
- `MaxHitMask` also looked correct, so raw RTAO output was probably fine.
- `NormHitDist` looked wrong and lacked expected noise/detail.
- A dedicated `GuideRawHitDist` debug view did not match `HitDist`, even though it was supposed to show the same source data before normalization.

### What actually went wrong

`NrdGuidePack` was reading raw hit distance through the implicit `Blit` source binding (`_MainTex`) instead of through an explicitly bound texture.

The problematic path was:

- `commandBuffer.Blit(rtaoCore.HitDistanceTexture, _packedDiffuseHitDistanceTexture, _guidePackMaterial);`
- `NrdGuidePack.shader` then sampled `_MainTex`

In practice, this made the guide shader's view of raw `HitDist` diverge from the direct `HitDist` preview path. As a result:

- `REBLUR_FrontEnd_GetNormHitDist` was being fed the wrong input signal
- `NormHitDist` looked spatially wrong
- denoised output also looked suspicious even though the denoiser itself was not the primary bug

### Key evidence

The decisive comparison was not between `NormHitDist` and `HitDist`, because those two are supposed to look different. The decisive comparison was:

- `HitDist` preview
- `GuideRawHitDist` preview

`GuideRawHitDist` was implemented to show the exact raw value sampled inside `NrdGuidePack`, mapped with the same `hitDistance / maxDistance` visualization as the normal `HitDist` preview. These two views should have matched. They did not.

This ruled out:

- `REBLUR_FrontEnd_GetNormHitDist` itself
- NRD denoiser settings
- `MaxDistance` clamping as the main issue

and isolated the bug to the guide-pack input path.

### Fix

Stop relying on `_MainTex` for raw hit distance in `NrdGuidePack`.

Instead:

- explicitly bind raw hit distance as `_VoxelEngineNrdHitDistanceSource`
- sample that explicit texture in `GuideRawHitDist` and `NormHitDist` modes
- keep using `sampler2D_float` for float RT inputs

This made the guide shader consume the same raw `HitDist` texture that the direct preview was showing.

### Why this bug appeared

The project mixed two assumptions:

- `CommandBuffer.Blit` can be convenient for simple full-screen copies
- guide preparation for NRD is a precision-sensitive data path

Those are not the same thing. Once the packed guide path started depending on exact float values from raw hit distance, the implicit `_MainTex` route became an unnecessary risk and made debugging ambiguous.

### What to check first next time

When a guide-pack or denoiser input looks wrong but the raw source looks correct:

1. Add a debug view that shows the exact value sampled inside the guide shader, not just the final normalized result.
2. Compare that debug view against the original source using the same visualization mapping.
3. If they differ, distrust implicit `Blit` source binding first.
4. For precision-sensitive guide inputs, prefer explicit texture bindings with dedicated property names.
5. Keep float textures on `sampler2D_float` all the way through guide generation and preview.

### Takeaway

For NRD guide preparation in this project, raw data should be passed explicitly, not inferred through `_MainTex`. If direct preview and guide-sampled preview disagree, fix the binding path before touching NRD formulas or denoiser settings.

## 2026-04-28 - AO frame averaging reused the same implicit `Blit` source mistake

### Symptoms

- Direct `HitDist` preview still showed the expected voxel hit pattern.
- The AO output produced by the new frame-average pass looked flat: hit voxels had the same color.
- This happened even with frame averaging disabled, after `_VoxelEngineRtao` had been changed to mean single-frame normalized AO.

### What actually went wrong

`VoxelRtaoFrameAverage.shader` sampled the current raw `HitDist` through the implicit `Blit` source binding (`_MainTex`).

This repeated the same class of bug as `NrdGuidePack`: the debug view for the original source and the value sampled inside the post pass were not guaranteed to be the same signal.

### Fix

Bind the raw source explicitly:

- `RtaoFrameAverageCore` now sets `_VoxelEngineRtaoHitDistanceSource` to `rtaoCore.HitDistanceTexture`.
- `VoxelRtaoFrameAverage.shader` samples `_VoxelEngineRtaoHitDistanceSource` instead of `_MainTex`.

### What to check first next time

If a post-process or denoise pass flattens values while the direct source preview looks correct, compare the direct source with the exact texture binding sampled inside the pass. Avoid implicit `_MainTex` for float data paths that need exact debugging.

## 2026-04-28 - Player build preview switching did not work because the hidden preview shader was stripped

### Symptoms

- In Editor Play mode, the debug preview buttons worked.
- In a Player build, switching to `Normal`, `LitColor`, or any other preview appeared to do nothing.
- The screen kept showing the camera clear/skybox color.
- This happened even though the runtime UI button state changed.

### What actually went wrong

`VoxelGbufferPreview.shader` was only discovered at runtime with:

- `Shader.Find("Hidden/VoxelEngine/Rendering/GbufferPreview")`

That works in the Editor because the AssetDatabase can still find project shaders. It is not reliable in a Player build. Since this hidden shader had no explicit asset or material reference in the render pipeline asset, Unity could strip it from the build.

When the shader was missing in Player:

- `EnsurePreviewMaterial()` could not create the preview material.
- the custom full-screen preview `Blit` did not run.
- the frame remained at the camera clear/skybox result, so all preview modes looked like they were ignored.

### Key evidence

The important clue was the Editor/Player split:

- Editor Play worked.
- Player build did not.
- The issue affected every preview mode, not only one texture or one shader branch.

That ruled out most per-preview math bugs and pointed at a build-time resource inclusion problem.

### Fix

Make the hidden preview shader an explicit serialized dependency:

- add `_gbufferPreviewShader` to `VoxelEngineRenderPipelineAsset`
- assign `Assets/VoxelEngine/Render/Shaders/VoxelGbufferPreview.shader` in the pipeline asset
- make `VoxelEngineRenderPipeline.EnsurePreviewMaterial()` prefer the asset reference before falling back to `Shader.Find`

This forces Unity to include the shader in Player builds.

### What to check first next time

When something works in Editor Play but not in Player:

1. Check whether the missing behavior depends on `Shader.Find`, `Resources.Load`, reflection, or any string-only asset lookup.
2. Hidden shaders should be referenced by a serialized asset, material, preloaded shader, or Always Included Shaders.
3. If every debug mode fails the same way, suspect the shared preview material/pass before individual preview math.
4. Add a Player log on the fallback path so build failures do not silently look like valid rendering.

### Takeaway

Do not rely on `Shader.Find` alone for project-local `Hidden/*` shaders in this render pipeline. If a shader is needed in Player, make it an explicit serialized dependency of the pipeline asset or another build-included asset.
