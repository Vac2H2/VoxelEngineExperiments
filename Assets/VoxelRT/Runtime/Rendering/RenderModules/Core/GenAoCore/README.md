# GenAoCore

## Purpose

`GenAoCore` is the detachable lighting render core that produces the voxel RT
ambient-occlusion target for one camera.

## Responsibilities

- validate that the configured AO DXR shader can render for the current camera
- allocate the temporary AO target
- bind camera, scene, GBuffer, and AO sampling parameters
- dispatch the AO ray generation shader
- expose `_VoxelRtAo` as a global texture for previews and later lighting work
- release the temporary AO target when the owner module is done

## Inputs

- `_VoxelRtNormal`
- `_VoxelRtDepth`

This core assumes an upstream pass has already generated the GBuffer globals for
the same camera.

## Output

- `_VoxelRtAo`

`VoxelRtAoIds` lives next to this core because it is part of the core contract
that downstream lighting or composite modules consume.
