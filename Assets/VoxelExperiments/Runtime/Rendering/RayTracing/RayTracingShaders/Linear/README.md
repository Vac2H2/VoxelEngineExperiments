# RayTracingShaders

## Occupancy Procedural

`VoxelOccupancyProcedural` is the occupancy-only procedural hit shader for voxel chunks.

Its current contract is:

- `intersection` resolves the current model chunk from `_VoxelModelResidencyId`, `_VoxelModelChunkStartBuffer`, and `PrimitiveIndex()`
- chunk occupancy is read only from `_VoxelOccupancyChunkBuffer`
- per-primitive chunk bounds are read from `_VoxelChunkAabbBuffer`
- traversal uses the same branchless DDA stepping pattern as the hierarchy prototype
- `closesthit` does not touch raw voxel bytes or palette buffers
- `closesthit` evaluates the screen-door keep/discard decision for transparent hits
- the returned payload remains hit-information only and packs face-normal id plus palette residency id into one `uint`

## Payload Contract

`Standard` now treats payload validity as a flag instead of a dedicated `hit` field.

The payload layout is:

- `float hitT`
- `uint packedNormalAndPaletteId`
- `uint flags`

`packedNormalAndPaletteId` uses:

- low `3` bits = face id
- remaining high bits = palette residency id

`flags` currently uses:

- `bit 0` = valid hit
- `bit 1` = transparent material
- `bit 2` = keep current screen-door hit

## Transparent Path

`closesthit` is responsible for classifying the current hit:

- opaque hits always set `keep current screen-door hit`
- transparent hits evaluate the checkerboard screen-door rule and encode the result into `flags`

`raygeneration` still owns ray orchestration:

- it reads the payload flags
- if the first hit is transparent and not marked to keep, it continues tracing against the same RTAS with the internal opaque-only instance mask
- otherwise it shades the returned hit directly

This keeps the current fullscreen module output unchanged while moving the
screen-door decision into the hit stage to validate the payload/flag
architecture.

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
