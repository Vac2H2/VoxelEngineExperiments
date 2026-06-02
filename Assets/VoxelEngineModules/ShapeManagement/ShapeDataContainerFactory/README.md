# ShapeDataContainerFactory

`ShapeDataContainerFactory` is the construction and recycling entry point for `ShapeDataContainer`.

The factory does not maintain a pool yet. `Acquire` creates a new container and initializes authoritative chunk data. `Release` disposes the container.

## Acquire

```csharp
ShapeDataContainer Acquire(
    NativeArray<byte> chunkData,
    NativeArray<int3> chunkPositions)
```

Inputs:

- `chunkData`: contiguous chunk occupied bitplanes.
- `chunkPositions`: one position per chunk.
- `chunkPositions` must be unique inside one Shape.

The required layout is:

```text
chunkCount       = chunkPositions.Length
chunkData.Length = chunkCount * ShapeDataContainer.BitPlaneBytesPerChunk
chunkBase        = chunkIndex * ShapeDataContainer.BitPlaneBytesPerChunk
```

The factory initializes:

```text
container.Chunks.IsOccupied
container.Chunks.Positions
container.Chunks.Used
container.Chunks.IndexByPosition
```

Initialization is optimized for the creation path:

- `Chunks.IsOccupied` uses a native bulk copy from input `chunkData`.
- `Chunks.Positions` uses a native bulk copy from input `chunkPositions`.
- `Chunks.Used[0..chunkPositions.Length]` is filled with `1` through one native memory set.
- `Chunks.IndexByPosition` is registered in one preallocated hash map pass.

`container.Fragments` is left empty. Fragment connectivity is derived data and is built later by Shape update jobs.

If initialization fails after the container is allocated, the factory disposes the container before rethrowing the error.

## Release

```csharp
void Release(ShapeDataContainer container)
```

Current behavior is simple disposal. If pooling is introduced later, this method becomes the point where containers are cleared and returned to the pool.
