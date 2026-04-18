# Data

## Purpose

`Data` contains the core data structures used by the production voxel engine.

This folder should hold stable, engine-facing representations of voxel data and
supporting value types that other systems can depend on safely.

## Current Layout

- `Voxel`: chunked voxel volume data structures, model containers, and fixed-size palette storage

## Responsibilities

- defining canonical voxel engine data structures
- keeping data contracts explicit and easy to serialize or inspect
- staying focused on representation, not runtime ownership or orchestration

## Non-Goals

`Data` should not own resource lifetime management.

That work belongs in the sibling `../LifeCycle` folder.
