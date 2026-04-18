# VoxelRuntime

This module owns the shared voxel rendering runtime and the scene-level host around it.

- `VoxelRuntime`
  - pure C# owner for `VoxelGpuResourceSystem`, one shared `RayTracingScene`, and `VoxelRayTracingResourceBinder`
- `VoxelRuntimeBootstrap`
  - `ExecuteAlways` scene component that creates, ticks, and disposes one runtime instance
- `VoxelRuntimeBootstrapResolver`
  - resolves the correct bootstrap for a renderer or camera within the current edit/play world
- `VoxelRuntimeRenderLoop`
  - hooks SRP camera rendering so edit-mode and play-mode cameras drive the same runtime tick path
- `VoxelRuntimeUpdateUtility`
  - editor-only repaint / player-loop nudges for authoring-time updates

The runtime semantics stay the same in edit and play. The only difference is how often Unity drives updates.

The current transparent path uses a single RTAS. Opaque-only retraces are
filtered by internal instance masks instead of a second opaque-only scene.
