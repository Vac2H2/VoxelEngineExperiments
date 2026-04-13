# Core

## Purpose

`Core/` holds reusable render-module building blocks that are not full
camera-facing modules by themselves.

## Layout Rule

Each detachable core lives in its own folder so that its code, output IDs, and
documentation stay together.

## Current Building Blocks

- `GenGbufferCore/`
  encapsulates voxel RT GBuffer generation and its shared texture IDs
- `GenAoCore/`
  encapsulates AO generation and its shared texture ID

## Design Rule

Core types should be self-contained and detachable:

- they own one focused rendering responsibility
- they expose a minimal API to modules
- they do not depend on one specific concrete module for correctness
