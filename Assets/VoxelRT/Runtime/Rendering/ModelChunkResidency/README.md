# ModelChunkResidency

## Purpose

`ModelChunkResidency` is the runtime module that owns deduplicated model chunk residency on the GPU.

It is responsible for:

- deduplicating model chunk data by `modelKey`
- allocating one contiguous chunk span per resident model
- uploading occupancy chunk data and voxel data chunk data
- exposing bindable GPU buffers for rendering
- returning a stable runtime `modelResidencyId` while the model is resident
- compacting chunk storage after the last release without changing any live `modelResidencyId`

It is not responsible for:

- scene instance registration
- RTAS instance creation/removal
- transform, mask, flags, material, or per-instance metadata
- shader binding policy outside of exposing the buffers

## Public API

The public contract is [`IModelChunkResidencyService.cs`](./IModelChunkResidencyService.cs).

It exposes:

- `GraphicsBuffer OccupancyChunkBuffer`
- `GraphicsBuffer VoxelDataChunkBuffer`
- `GraphicsBuffer ModelChunkStartBuffer`
- `uint ModelChunkStartStrideBytes`
- `int Retain(object modelKey, uint chunkCount, NativeArray<byte> occupancyBytes, NativeArray<byte> voxelBytes)`
- `void Release(int residencyId)`

## Buffer Contract

### `OccupancyChunkBuffer`

- raw chunk payload buffer
- one chunk = `64` bytes
- indexed by global chunk index

### `VoxelDataChunkBuffer`

- raw chunk payload buffer
- one chunk = `512` bytes
- indexed by global chunk index

### `ModelChunkStartBuffer`

- structured buffer of `uint`
- one element per live or reusable residency slot
- value = global `startSlot` for that model
- invalid slot sentinel = `uint.MaxValue`

`ModelChunkStartStrideBytes` is currently `4`.

## Runtime Semantics

### Retain

`Retain(...)` does two different things depending on whether the model is already resident.

If the `modelKey` is already resident:

- no duplicate upload happens
- no new chunk span is allocated
- internal refcount increments
- the existing `modelResidencyId` is returned

If the `modelKey` is not resident:

- a contiguous chunk span is allocated
- occupancy bytes and voxel bytes are uploaded once
- a residency slot id is allocated or reused
- `ModelChunkStartBuffer[modelResidencyId]` is updated to the model's `startSlot`

### Release

`Release(residencyId)` decrements the internal refcount.

If the refcount is still above zero:

- nothing is moved
- the residency stays live

If the refcount reaches zero:

- the model is removed from the residency map
- the residency id becomes reusable
- chunk storage is compacted
- all live models get new internal `startSlot` values as needed
- `ModelChunkStartBuffer` is rewritten so external code can still resolve live models by the same `modelResidencyId`

## Stability Rules

The module guarantees:

- live `modelResidencyId` values are stable
- `modelResidencyId` is the only public identity
- compact never changes any live `modelResidencyId`
- external systems do not need to know `ChunkSpan`, allocator state, or store internals

The module does **not** guarantee:

- `startSlot` stability across compaction
- released `modelResidencyId` values remain reserved forever

Released ids are returned to an internal free-list and may be reused later.

## Expected Shader Mapping

For the pure model-residency path, the intended mapping is:

```hlsl
uint modelResidencyId = InstanceID();
uint startSlot = _ModelChunkStartBuffer[modelResidencyId];
uint globalChunkIndex = startSlot + PrimitiveIndex();
```

This assumes:

- one RTAS procedural instance references exactly one `modelResidencyId`
- multiple RTAS instances may reference the same resident model
- `PrimitiveIndex()` maps 1:1 to the model's local chunk index
- the RTAS AABB order matches the model chunk order

If your rendering path needs scene-instance data, add another layer outside this module:

- `sceneInstanceId -> modelResidencyId`

Do not put scene-instance ownership back into `ModelChunkResidency`.

## Internal Structure

The module currently contains these internal responsibilities:

- `ChunkSpanAllocator`: manages contiguous chunk span allocation
- `OccupancyChunkStore`: manages the 64B-per-chunk raw occupancy buffer
- `VoxelDataChunkStore`: manages the 512B-per-chunk raw voxel data buffer
- `ModelChunkResidencyService`: owns deduplication, slot id reuse, and compaction

These types are implementation details. External systems should depend only on `IModelChunkResidencyService`.

## Invariants

- one resident model owns exactly one contiguous chunk span
- occupancy and voxel data always share the same logical chunk span
- `modelKey` deduplicates uploads
- `ModelChunkStartBuffer[id] == uint.MaxValue` means the slot is invalid
- live residency ids are stable
- released residency ids may be reused

## Integration Recommendation

Use this module as the model data layer only.

Recommended layering:

1. `ModelChunkResidencyService`
   - owns model deduplication and model-to-chunk residency
2. `SceneInstanceRegistry` or equivalent
   - owns scene instance ids and maps instances to `modelResidencyId`
3. RTAS management
   - owns `AddInstance`, `RemoveInstance`, transforms, masks, and rebuilds

This keeps model deduplication, scene lifetime, and RTAS lifetime separated.
