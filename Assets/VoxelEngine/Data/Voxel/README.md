# Voxel

## Purpose

`Voxel` holds the production voxel data structures that define how chunked voxel
content is stored in memory.

The initial implementation centers on `VoxelVolume`, which owns chunk data
logically while storing chunk payloads in contiguous `NativeList` blocks and a
coordinate-to-index mapping.

## Current Types

- `VoxelVolume`: chunked voxel storage with 8x8x8 byte voxels per chunk
- `VoxelModel`: one voxel object wrapped around a fixed opaque/transparent volume pair
- `VoxelPalette`: fixed-size 256-color palette for voxel content

## Storage Rules

Each chunk stores:

- `512` voxel bytes in linear order: `x + 8 * y + 64 * z`
- up to `16` AABB slots

Each AABB slot includes an in-use flag so inactive entries can remain inside the
fixed `16`-slot per-chunk layout without being treated as valid bounds.

`VoxelVolume` is not fixed-size. It starts from an initial chunk capacity, then
grows its internal `NativeList` storage when more chunk slots are needed.

## Model Rules

`VoxelModel` represents one voxel object.

Its raw data is always packaged as exactly two `VoxelVolume` instances:

- one opaque volume
- one transparent volume

Both volumes are defined in the same chunk / voxel grid space.

That opaque / transparent split is a render-layer preference, not a signal that
callers should manage two separate voxel objects.

`VoxelModel` does not store:

- a variable-length list of volumes
- per-volume opacity flags
- per-volume transforms or positions

That split is an internal raw-data packaging rule. The purpose of `VoxelModel`
is to wrap the lower-level volume pair in the format preferred by rendering and
GPU-lifecycle systems while higher-level code still treats the model as one
logical voxel object.

## Palette Rules

`VoxelPalette` stores exactly `256` `VoxelColor` entries.

Each `VoxelColor` is packed RGBA bytes:

- `R`
- `G`
- `B`
- `A`
