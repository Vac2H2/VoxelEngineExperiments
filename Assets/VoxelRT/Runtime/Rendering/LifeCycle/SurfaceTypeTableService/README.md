# SurfaceTypeTableService

## Purpose

`SurfaceTypeTableService` is the runtime module that owns the global GPU surface-type table used by voxel shading.

It is responsible for:

- owning one GPU table buffer for all surface types
- exposing that buffer for global shader binding
- replacing the entire table content when surface-type data changes

It is not responsible for:

- per-model residency
- per-instance registration
- RTAS instance creation/removal
- chunk allocation or compaction

## Public API

The public contract is [`ISurfaceTypeTableService.cs`](./ISurfaceTypeTableService.cs).

It exposes:

- `GraphicsBuffer SurfaceTypeTableBuffer`
- `uint SurfaceTypeTableStrideBytes`
- `uint SurfaceTypeEntryCount`
- `void Update(NativeArray<uint> packedEntries)`

## Buffer Contract

### `SurfaceTypeTableBuffer`

- structured buffer of `uint`
- fixed `256` entries
- one entry = `4` bytes
- total table size = `1024` bytes
- indexed directly by `surfaceType`

Each packed entry is expected to contain:

- `reflectivity8`
- `smoothness8`
- `metallic8`
- `emissive8`

The packing layout is intentionally opaque to this service.

## Runtime Semantics

This is a global table service, not a residency system.

That means:

- there is exactly one table buffer
- the table is updated as a whole
- there is no `Retain` / `Release`
- there is no slot allocation

## Stability Rules

The current implementation creates one `GraphicsBuffer` in the constructor and updates its content in place through `SetData`.

So for the current implementation:

- the buffer reference is stable for the service lifetime
- table updates replace contents, not the buffer object

## Integration Recommendation

Use this service as part of a higher-level voxel GPU resource system.

Typical layering:

1. `ModelChunkResidency`
2. `PaletteChunkResidency`
3. `SurfaceTypeTableService`
4. voxel GPU resource binder

This keeps global surface material properties separate from model and palette residency.
