# Gbuffer

## Purpose

`VoxelOccupancyProceduralGbuffer` is the full GBuffer procedural shader family
for voxel chunks.

It extends the `Standard` occupancy traversal path and keeps the same
screen-door transparency orchestration, but the final `.raytrace` stage writes
the full voxel GBuffer set.

## Usage Constraint

The `RtGbuffer` render module expects voxel instances to use the material asset:

- `VoxelOccupancyProceduralGbuffer.mat`

That material provides the `VoxelOccupancyDXR` pass used by the RTAS hit stage.

## Payload Contract

The payload is still hit-information only:

- `float hitT`
- `uint packedNormalAndPaletteId`
- `uint paletteEntryIndex`
- `uint flags`

Where:

- `packedNormalAndPaletteId`
  - low `3` bits = face id
  - remaining high bits = palette residency id
- `paletteEntryIndex`
  - the hit voxel's palette entry index read from `_VoxelDataChunkBuffer`
- `flags`
  - `bit 0` = valid hit
  - `bit 1` = transparent material
  - `bit 2` = keep current screen-door hit

`closesthit` does not fetch palette or surface-type table data.

## GBuffer Outputs

`.raytrace` resolves the final visible hit, reads the palette entry, extracts the
surface type, reads the packed surface info table entry, and writes:

- `GBuffer0`: `RGBA8`, `rgb = albedo`, `a = reserved`
- `GBuffer1`: `RGBA8`, `rgb = encoded normal`, `a = reserved`
- `GBuffer2`: `R16_SFloat`, `r = linear view depth`
- `SurfaceInfo`: `RGBA8`, `rgba = reflectivity, smoothness, metallic, emissive`

## Data Sources

- `_VoxelOccupancyChunkBuffer`
  occupancy traversal
- `_VoxelDataChunkBuffer`
  palette entry index lookup for the hit voxel
- `_VoxelPaletteChunkBuffer`
  `RGBA8` palette entry fetch
- `_VoxelPaletteChunkStartBuffer`
  palette residency indirection
- `_VoxelSurfaceTypeTableBuffer`
  packed surface info lookup by `surfaceType`
