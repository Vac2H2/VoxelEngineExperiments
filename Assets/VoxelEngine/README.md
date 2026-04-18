# VoxelEngine

## Purpose

`VoxelEngine` is the root for the production voxel engine code.

Experimental or transitional work should stay outside this tree. New engine
systems, runtime code, editor tooling, assets, and supporting docs that belong
to the formal pipeline should be organized under this folder.

## Current Layout

- `Data`: core data structures shared by the engine
- `LifeCycle`: voxel resource lifetime and orchestration

## Folder Rules

Every folder inside `VoxelEngine` must include a `README.md`.

That rule applies recursively:

- `VoxelEngine` itself must have a `README.md`
- every direct child folder must have a `README.md`
- every nested folder must also have a `README.md`

Each README should explain:

- the folder's purpose
- the main files or systems that live there
- the ownership or architectural boundary if it is not obvious
