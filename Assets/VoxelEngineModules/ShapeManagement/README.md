# ShapeManagement

`ShapeManagement` is the independent-memory Shape storage model under the `VoxelEngineModules.ShapeManagement` namespace.

The old `VoxelEngineModules.Shape` module stores many Shapes in global contiguous native arrays. The new module stores each Shape as an independent `ShapeDataContainer`. `ShapeDataStorage` manages handles, metadata, and the lifetime of those containers.

## Core Semantics

`ShapeDataContainer` owns two data containers with different meanings:

```csharp
ChunkDataContainer Chunks;
FragmentDataContainer Fragments;
```

`ChunkDataContainer` is the final authoritative data. Chunk occupancy, surface classification, chunk positions, used flags, and position lookup live here. If a voxel exists or does not exist, `Chunks` is the source of truth.

`FragmentDataContainer` is derived connectivity data. Fragment occupancy masks, chunk-to-fragment lookup, and CSR graph arrays exist to accelerate split and connectivity work. They must be rebuilt or patched from `Chunks` whenever destruction changes topology.

`ShapeDataContainer` binds these two semantics for one Shape. It owns the native buffers, exposes them, and releases them. It does not interpret edit commands.

## Shape Size

ShapeManagement Shapes are not fixed to 8 chunks. The chunk voxel scale is fixed, but the number of chunk slots is provided externally when a Shape container is created.

```text
chunkSize             = 8
bitPlaneBytesPerChunk = 8 * 8 = 64
chunkCapacity         = supplied by caller
chunkBitPlaneBytes    = chunkCapacity * bitPlaneBytesPerChunk
```

Because a `ShapeDataContainer` represents one Shape, chunk bitplane access uses local chunk offsets:

```text
chunkBase = chunkIndex * BitPlaneBytesPerChunk
byteIndex = chunkBase + y + ChunkSize * z
bitMask   = 1 << x
```

`chunkIndex` is local to this Shape.

All native allocations in this module use `Allocator.Persistent`. Allocator is not a public construction parameter.

## ChunkDataContainer

`ChunkDataContainer` stores authoritative chunk-level data:

```csharp
NativeArray<byte> IsOccupied;
NativeArray<byte> IsFace;
NativeArray<byte> IsEdge;
NativeArray<byte> IsCorner;
NativeArray<int3> Positions;
NativeArray<byte> Used;
NativeParallelHashMap<int3, int> IndexByPosition;
```

`IndexByPosition` maps:

```text
int3 chunkPosition -> int chunkIndex
```

This is the authoritative chunk position lookup for the Shape.

## FragmentDataContainer

`FragmentDataContainer` stores derived connectivity data:

```csharp
NativeList<byte> IsOccupied;
NativeParallelMultiHashMap<int3, int> IndicesByChunkPosition;
NativeList<int> ConnectionOffsets;
NativeList<int> ConnectionTargets;
```

Each fragment stores only an occupied bitplane:

```text
fragmentBase = fragmentIndex * BitPlaneBytesPerChunk
fragmentByte = fragmentBase + y + ChunkSize * z
fragmentBit  = 1 << x
```

The current fragment count is:

```text
Fragments.IsOccupied.Length / BitPlaneBytesPerChunk
```

`IndicesByChunkPosition` maps:

```text
int3 chunkPosition -> one or more fragmentIndex values
```

This allows one chunk to own multiple fragments without giving fragments separate chunk storage.

Fragment connectivity is represented as CSR:

```text
ConnectionOffsets[fragmentIndex]
ConnectionOffsets[fragmentIndex + 1]
ConnectionTargets[offset]
```

The neighbors of fragment `i` are stored in:

```text
ConnectionTargets[
    ConnectionOffsets[i] ..
    ConnectionOffsets[i + 1] - 1
]
```

Fragment data is not authoritative for voxel existence. It is a connectivity index derived from `Chunks.IsOccupied`.

## ShapeDataStorage

`ShapeDataStorage` is the lifecycle entry point. It holds:

```csharp
int[] _freeShapeHandles;
ShapeMetadata[] _shapes;
ShapeDataContainer[] _containers;
```

The storage-level containers are managed arrays for consistency. Native containers live inside each acquired `ShapeDataContainer`.

`_freeShapeHandles` is a fixed-capacity managed free stack. `Acquire(bodyHandle, chunkCapacity)` takes a handle from the stack, creates a `ShapeDataContainer` with the caller-provided chunk capacity, and marks the matching metadata slot used:

```csharp
BodyHandle = bodyHandle;
IsUsed = 1;
```

If no slot is available, `Acquire` returns:

```csharp
ShapeDataStorage.InvalidHandle
```

Because each Shape can have a different chunk capacity, containers are created on `Acquire` and disposed on `Release`. `Release(handle)` disposes that handle's `ShapeDataContainer`, clears metadata, and returns the handle to the free stack. A later `Acquire` can reuse the same handle with a different chunk capacity.

## ShapeDataContainerFactory

`ShapeDataContainerFactory` is the external construction boundary for one `ShapeDataContainer`. It takes caller-owned input data:

```csharp
ShapeDataContainer Acquire(
    NativeArray<byte> chunkData,
    NativeArray<int3> chunkPositions)
```

The factory creates a new container, copies `chunkData` into `Chunks.IsOccupied`, copies `chunkPositions` into `Chunks.Positions`, marks all chunk slots used, and builds `Chunks.IndexByPosition`.

`chunkPositions` must be unique. `chunkData.Length` must equal:

```text
chunkPositions.Length * ShapeDataContainer.BitPlaneBytesPerChunk
```

The factory does not build fragment connectivity. `Fragments` remains derived data for the update pipeline. `Release(container)` currently disposes the container directly; no pool is maintained yet.

## GetShape

Shape data access uses:

```csharp
ShapeDataContainer shape = storage.GetShape(shapeHandle);
```

`GetShape` validates that the handle is used and inside storage capacity, then returns the container reference.

Callers should not dispose containers returned by storage. Their lifetime is owned by `ShapeDataStorage`.

## Boundary Rules

- `Chunks` is final authoritative data.
- `Fragments` is derived connectivity data.
- If `Chunks` changes topology, `Fragments` must be rebuilt or patched before connectivity-dependent code uses it.
- `ShapeDataStorage` owns handles, metadata, and container lifetime.
- Pipeline code owns voxel edits, chunk writes, fragment rebuilds, CSR construction, split, and merge semantics.
- `ShapeDataContainer` does not provide `SetChunk`, `ClearChunk`, or lookup helper methods. It is a native data owner, not a command API.
