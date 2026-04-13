# GenAoCore

## Purpose

`GenAoCore` is the detachable lighting render core that produces the voxel RT
ambient-occlusion lighting value for one camera.

## Responsibilities

- validate that the configured AO DXR shader can render for the current camera
- allocate the dedicated temporary AO target
- bind camera, scene, GBuffer, and AO sampling parameters
- dispatch the AO ray generation shader
- expose `_VoxelRtAo` as a global texture for previews and later lighting work
- release the AO target when the owner module is done

## Inputs

- `_VoxelRtNormal`
- `_VoxelRtDepth`

This core assumes an upstream pass has already generated the GBuffer globals for
the same camera.

## Output

- `_VoxelRtAo`
  single-channel AO RT initialized with ambient visibility

`VoxelRtAoIds` lives next to this core because it is part of the core contract
that downstream lighting or composite modules consume. That contract now owns a
dedicated `_VoxelRtAo` texture instead of aliasing a shared lighting target.

`GenAoCore` also exposes a `0..1` max ambient visibility control. The traced AO
visibility is multiplied by that cap before being written into the AO RT.
