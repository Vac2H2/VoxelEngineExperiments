# LifeCycle

## Purpose

`LifeCycle` manages voxel resource lifetime for the production engine.

This folder should contain the systems that create, upload, retain, recycle,
and dispose voxel resources as runtime state changes.

## Current Layout

- `Manager`: synchronous managers that own voxel GPU resource lifetimes

## Responsibilities

- managing resource acquisition and release
- coordinating transitions between CPU-side data and runtime GPU resources
- centralizing lifetime rules so dependent systems do not duplicate them

## Non-Goals

`LifeCycle` should not become the home for low-level data definitions.

Canonical data structures belong in the sibling `../Data` folder.
