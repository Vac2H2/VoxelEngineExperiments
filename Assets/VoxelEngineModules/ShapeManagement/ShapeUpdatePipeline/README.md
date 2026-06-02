# ShapeUpdatePipeline

`ShapeUpdatePipeline` is the update and destruction pipeline planned for `VoxelEngineModules.ShapeManagement`.

This pipeline is built around the new ShapeManagement memory model: each Shape owns an independent `ShapeDataContainer`, and pipeline jobs operate on that container's local buffers instead of on one global contiguous Shape buffer.

## Scope

The pipeline will coordinate updates to persistent Shape data after voxel edits, destruction masks, fragmentation, or rebuild steps. The concrete job sequence is not defined yet; this directory only establishes the module boundary and the job placement convention.

Expected responsibilities:

- Read and write `ShapeDataContainer` data returned by `ShapeDataStorage.GetShape(handle)`.
- Treat chunk slots as local indices in the range `0..shape.ChunkCapacity - 1`.
- Treat `shape.Chunks` as final authoritative data.
- Treat `shape.Fragments` as derived connectivity data.
- Maintain or rebuild `Chunks.Positions`, `Chunks.Used`, and `Chunks.IndexByPosition` when a stage changes chunk topology.
- Rebuild or patch `Fragments.IsOccupied`, `Fragments.IndicesByChunkPosition`, and fragment CSR connections after authoritative chunk data changes.
- Keep Shape metadata lifecycle under `ShapeDataStorage`; pipeline stages should not allocate or release shape handles directly unless that becomes an explicit pipeline step.

Out of scope:

- The old global layout based on `shapeHandle * BitPlaneBytesPerShape`.
- `ShapeDataView` style aggregation.
- Job implementations that assume all Shapes share one contiguous bitplane array.

## Memory Model

Old Shape pipeline jobs usually use global offsets:

```text
shapeBase      = shapeHandle * BitPlaneBytesPerShape
chunkSlotIndex = shapeHandle * OldChunksPerShape + chunkIndex
```

ShapeManagement jobs should use local offsets inside one `ShapeDataContainer`:

```text
chunkBase      = chunkIndex * BitPlaneBytesPerChunk
chunkSlotIndex = chunkIndex
```

Shape chunk count is not fixed. The job input boundary should make it clear whether a job processes one Shape, one chunk in one Shape, or a batch of handles that will be resolved to independent containers before scheduling.

## Fragment Graph

Connectivity should be maintained at fragment level. A chunk can contain multiple fragments, and those fragments are stored as extra data instead of separate chunks:

```text
Fragments.IsOccupied                // fragmentIndex * 64B
Fragments.IndicesByChunkPosition    // int3 chunkPosition -> fragmentIndex values
Fragments.ConnectionOffsets         // CSR offsets
Fragments.ConnectionTargets         // CSR neighbor fragment indices
```

When a chunk is destroyed, the planned update path is:

```text
dirty chunk
-> remove its old fragment entries and related CSR edges
-> BFS only this chunk to produce new fragment masks
-> connect new fragments to neighbor fragments through face checks
-> rebuild or patch fragment CSR
-> flood fill the fragment graph to determine independent Shapes
```

This trades memory for computation: stable fragments and connectivity are stored so the pipeline does not need to parse every chunk after each destruction event.

The authoritative source remains `Chunks.IsOccupied`. Fragment masks and CSR data are indexes derived from chunk data. If there is a conflict, the chunk container wins and the fragment container must be regenerated.

## Jobs Folder

The `Jobs` folder is reserved for ShapeManagement-specific jobs. These jobs should be written for independent Shape containers and should not depend on the old `VoxelEngineModules.Shape.ShapeDataView` layout.

When adding jobs later, keep names tied to the stage they implement, for example:

```text
ShapeVoxelApplyJob
ShapeChunkFragmentJob
ShapeChunkConnectivityJob
ShapeRebuildLookupJob
```

The exact job list and scheduling order should be documented here once the pipeline stages are designed.
