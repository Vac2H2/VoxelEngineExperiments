# Destruction Shape

`DestructionShape` stores voxel masks for destruction source geometry. It is intentionally separate from the persistent `Shape` storage because destruction sources do not need normal shape-derived bitplanes such as face, edge, or corner data.

## Layout

Each destruction shape is still a fixed 8-slot chunk buffer:

```text
chunkSize               = 8
chunksPerShape          = 8
maskBytesPerChunk       = 8 * 8 = 64
maskBytesPerShape       = 8 * 64 = 512
```

The mask indexing matches the regular shape occupancy bitplane layout:

```text
shapeBase = shapeHandle * maskBytesPerShape
chunkBase = shapeBase + chunkSlot * maskBytesPerChunk
byteIndex = chunkBase + y + 8 * z
bitMask   = 1 << x
```

## Data

`DestructionDataContainer` owns only the data needed by destruction sources:

```csharp
NativeArray<byte> DestructionMasks;
NativeArray<int3> ChunkPositions;
NativeArray<byte> ChunkUsed;
```

There is no `IsFace`, `IsEdge`, or `IsCorner` buffer. Those are persistent shape rebuild concerns, not destruction-source input.

## Metadata

`DestructionShapeMetadata` only tracks slot lifetime:

```csharp
byte IsUsed;
```

It does not store a body handle. Body ownership is a persistent world-shape concern and should not be copied into destruction source storage.

## Storage

`DestructionShapeDataStorage` owns shape handle lifetime:

```csharp
int handle = storage.Acquire();
bool released = storage.Release(handle);
```

The returned handle is the fixed storage slot index. `Acquire` does not perform voxel work. `Release` clears the 8 chunk slots and the 512B destruction mask region for that shape before returning the handle to the free stack.

`DestructionShapeDataView` is a full container view. It does not represent a single destruction shape; callers use the handle to calculate offsets.

## Semantics

Destruction shapes have weaker topology requirements than persistent world shapes. Their used chunk slots do not need to be connected or continuous in `int3` chunk space. Chunk positions only describe where each destruction chunk samples against target shape chunks.

The destruction pipeline should read this storage as source geometry and emit remove commands against target shapes:

```text
DestructionShapeDataStorage -> remove commands -> packed remove masks -> target shape update
```
