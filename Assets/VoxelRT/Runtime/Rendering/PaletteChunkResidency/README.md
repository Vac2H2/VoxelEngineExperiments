# PaletteChunkResidency

## Purpose

`PaletteChunkResidency` is the runtime module that owns deduplicated palette residency on the GPU.

It is responsible for:

- deduplicating palette data by `paletteKey`
- allocating one GPU slot per resident palette
- uploading one raw palette chunk per palette
- exposing bindable GPU buffers for rendering
- returning a stable runtime `paletteResidencyId` while the palette is resident
- compacting palette storage after the last release without changing any live `paletteResidencyId`

It is not responsible for:

- scene instance registration
- model residency
- RTAS instance creation/removal
- shader binding policy outside of exposing the buffers

## Public API

The public contract is [`IPaletteChunkResidencyService.cs`](./IPaletteChunkResidencyService.cs).

It exposes:

- `GraphicsBuffer PaletteChunkBuffer`
- `GraphicsBuffer PaletteChunkStartBuffer`
- `uint PaletteChunkStartStrideBytes`
- `int Retain(object paletteKey, NativeArray<byte> paletteBytes)`
- `void Release(int residencyId)`

## Buffer Contract

### `PaletteChunkBuffer`

- raw chunk payload buffer
- one palette = exactly one chunk
- one chunk = `256` entries
- one entry = `4` bytes
- total chunk size = `1024` bytes
- indexed by global palette chunk slot

The module treats the payload as opaque bytes. The intended logical layout is:

- `R`
- `G`
- `B`
- `surfaceType`

### `PaletteChunkStartBuffer`

- structured buffer of `uint`
- one element per live or reusable residency slot
- value = global slot for that palette's single chunk
- invalid slot sentinel = `uint.MaxValue`

`PaletteChunkStartStrideBytes` is currently `4`.

## Runtime Semantics

### Retain

`Retain(...)` does two different things depending on whether the palette is already resident.

If the `paletteKey` is already resident:

- no duplicate upload happens
- no new slot is allocated
- internal refcount increments
- the existing `paletteResidencyId` is returned

If the `paletteKey` is not resident:

- one slot is allocated
- exactly `1024` bytes are uploaded once
- a residency slot id is allocated or reused
- `PaletteChunkStartBuffer[paletteResidencyId]` is updated to that palette's slot

### Release

`Release(residencyId)` decrements the internal refcount.

If the refcount is still above zero:

- nothing is moved
- the residency stays live

If the refcount reaches zero:

- the palette is removed from the residency map
- the residency id becomes reusable
- palette storage is compacted
- all live palettes get new internal slot values as needed
- `PaletteChunkStartBuffer` is rewritten so external code can still resolve live palettes by the same `paletteResidencyId`

## Stability Rules

The module guarantees:

- live `paletteResidencyId` values are stable
- `paletteResidencyId` is the only public identity
- compact never changes any live `paletteResidencyId`
- external systems do not need to know allocator state or store internals

The module does **not** guarantee:

- slot stability across compaction
- released `paletteResidencyId` values remain reserved forever

Released ids are returned to an internal free-list and may be reused later.

## Expected Shader Mapping

Because one palette always maps to one chunk, the GPU lookup is just:

```hlsl
uint paletteResidencyId = ...;
uint paletteChunkSlot = _PaletteChunkStartBuffer[paletteResidencyId];
```

No chunk count is needed for this module, and there is no per-palette local chunk index.

## Internal Structure

The module currently contains these internal responsibilities:

- `PaletteSlotAllocator`: manages single-slot allocation and reuse
- `PaletteChunkStore`: manages the 1024B-per-palette raw buffer
- `PaletteChunkResidencyService`: owns deduplication, residency id reuse, and compaction

These types are implementation details. External systems should depend only on `IPaletteChunkResidencyService`.

## Invariants

- one resident palette owns exactly one chunk slot
- one chunk is always `256` entries x `4` bytes = `1024` bytes
- `paletteKey` deduplicates uploads
- `PaletteChunkStartBuffer[id] == uint.MaxValue` means the slot is invalid
- live residency ids are stable
- released residency ids may be reused

## Integration Recommendation

Use this module as the palette data layer only.

Typical layering:

1. `PaletteChunkResidencyService`
   - owns palette deduplication and palette-to-slot residency
2. model or material data layer
   - references `paletteResidencyId` where needed
3. rendering / RTAS layer
   - binds the buffers and resolves palette slot on demand

This keeps palette residency separate from model residency and instance lifetime.
