# GenGbufferCore

## Purpose

`GenGbufferCore` is the detachable render core that fills the voxel RT GBuffer
set for one camera.

## Responsibilities

- validate that the current camera can render with the configured DXR shader
- allocate the temporary GBuffer targets
- bind camera, scene, and runtime parameters
- dispatch the GBuffer ray generation shader
- expose the generated textures as globals for downstream lighting cores
- release the temporary targets when the owner module is done

## Outputs

- `_VoxelRtAlbedo`
- `_VoxelRtNormal`
- `_VoxelRtDepth`
- `_VoxelRtSurfaceInfo`

`VoxelRtGbufferIds` lives next to this core because it is part of the core
contract that downstream lighting passes consume.
