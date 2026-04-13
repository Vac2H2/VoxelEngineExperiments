# GenAoCore

## Purpose

`GenAoCore` is the detachable lighting render core that produces the voxel RT
ambient-occlusion lighting value for one camera.

## Responsibilities

- validate that the configured AO DXR shader can render for the current camera
- allocate the shared temporary lighting target
- bind camera, scene, GBuffer, and AO sampling parameters
- dispatch the AO ray generation shader
- expose `_VoxelRtLighting` as a global texture for previews and later lighting work
- release the shared lighting target when the owner module is done

## Inputs

- `_VoxelRtNormal`
- `_VoxelRtDepth`

This core assumes an upstream pass has already generated the GBuffer globals for
the same camera.

## Output

- `_VoxelRtLighting`
  single-channel lighting RT initialized with AO visibility

`VoxelRtAoIds` lives next to this core because it is part of the core contract
that downstream lighting or composite modules consume. Today that contract
aliases the shared `_VoxelRtLighting` texture rather than owning a separate RT.

`GenAoCore` also exposes a `0..1` max ambient visibility control. The traced AO
visibility is multiplied by that cap before being written into the shared
lighting RT.
