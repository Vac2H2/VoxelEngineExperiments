# RayTracingShaders

## Occupancy Procedural

`VoxelOccupancyProcedural` is the occupancy-only procedural hit shader for voxel chunks.

Its current contract is:

- `intersection` resolves the current model chunk from `_VoxelModelResidencyId`, `_VoxelModelChunkStartBuffer`, and `PrimitiveIndex()`
- chunk occupancy is read only from `_VoxelOccupancyChunkBuffer`
- per-primitive chunk bounds are read from `_VoxelChunkAabbBuffer`
- traversal uses the same branchless DDA stepping pattern as the hierarchy prototype
- `closesthit` does not touch raw voxel bytes or palette buffers
- the returned payload packs face-normal id and palette residency id into one `uint`

## Occupancy Layout

Per chunk:

- chunk size = `8 x 8 x 8`
- occupancy size = `64` bytes
- one byte stores one `x` row for a fixed `(y, z)`
- bit `0..7` maps to `x = 0..7`
- byte order is `y` first, then `z`

That means the occupancy byte address inside a chunk is:

```text
byteIndex = y + 8 * z
bitIndex = x
```

This layout must stay aligned with the mesh voxelizer CPU path and GPU path.
