# Update Jobs

This folder contains jobs that consume packed remove masks and produce rebuilt shape buffers. Update jobs start after destruction intent has already been packed into unique affected shapes.

## Boundary

Inputs from the producer side:

```text
AffectedShapeHandles = affectedShapeCount
RemoveMasks          = affectedShapeCount * 8 * 64B
```

`RemoveMasks` uses keep-mask semantics:

```text
bit 1 = keep voxel
bit 0 = remove voxel
```

Update jobs consume those masks, compute the post-removal occupancy, split disconnected pieces, and build compact output buffers for commit back into shape storage.

`ShapeDestructionUpdatePipeline` is the class wrapper for this whole stage. Call `Run(...)` with the data needed by `ShapeVoxelRemoveJob` plus the shape chunk metadata needed by later stages. It owns and disposes intermediate arrays, then returns `ShapeDestructionUpdatePipelineOutput` containing:

```text
LocalShapeCounts
BuiltShapeOffsets
BuiltChunks
BuiltIsOccupied
BuiltShapeCount
```

The caller owns the returned output and must dispose it after commit.

## Stage Order

```text
ShapeVoxelRemoveJob
    -> ShapeChunkFragmentJob
    -> ShapeChunkCheckMaskJob
    -> ShapeChunkConnectivityJob
    -> ShapeFragmentUnionJob
    -> ShapePrefixSumComputeJob
    -> ShapeBuildJob
```

## Jobs

### ShapeVoxelRemoveJob

Scheduling unit:

```text
Execute(index) = one affected shape
```

It reads global source occupancy and applies the packed shape-local remove masks:

```text
target = source & removeMask
```

Output:

```text
TargetIsOccupied = affectedShapeCount * 8 * 64B
```

This is a compact affected-shape working set. Later update jobs read this working set, not the original source occupancy.

### ShapeChunkFragmentJob

Scheduling unit:

```text
Execute(index) = one affected shape chunk slot
```

Each input chunk can emit up to 4 chunk-local fragments:

```text
OutputIsOccupied           = affectedShapeCount * 8 * 4 * 64B
OutputGeneratedChunkCounts = affectedShapeCount * 8
```

The job keeps only the largest valid fragments for the chunk.

### ShapeChunkCheckMaskJob

Scheduling unit:

```text
Execute(index) = one affected shape
```

It builds direction-specific candidate check masks from the original 8 chunk positions. It does not test voxel face connectivity.

Output:

```text
FragmentCheckMasks = affectedShapeCount * 32 * 3
```

Each `uint` marks candidate fragment nodes to check on `+X`, `+Y`, or `+Z`.

### ShapeChunkConnectivityJob

Scheduling unit:

```text
Execute(index) = one fragment node
recommended batch size = 32
```

It consumes `FragmentCheckMasks`, performs real face-bit tests, and outputs actual connection masks:

```text
FragmentConnectionMasks = affectedShapeCount * 32 * 3
```

Only positive directions are written, so each execute writes only its own node output.

### ShapeFragmentUnionJob

Scheduling unit:

```text
Execute(index) = one affected shape
```

It consumes `FragmentConnectionMasks`, runs a fixed 32-node union-find, and writes compact local shape ids:

```text
FragmentLocalShapeIds = affectedShapeCount * 32
LocalShapeCounts      = affectedShapeCount
```

### ShapePrefixSumComputeJob

Scheduling unit:

```text
Execute() = one affected-shape batch
```

It computes compact output offsets from `LocalShapeCounts`:

```text
BuiltShapeOffsets    = affectedShapeCount
TotalBuiltShapeCount = 1
```

The pipeline completes this stage before allocating final build output buffers.

### ShapeBuildJob

Scheduling unit:

```text
Execute(index) = one affected source shape
```

It uses `BuiltShapeOffsets` and `FragmentLocalShapeIds` to build final compact shape buffers:

```text
BuiltChunks      = TotalBuiltShapeCount * 8 BuiltChunkMetadata
BuiltIsOccupied  = TotalBuiltShapeCount * 8 * 64B
```

Each built shape still has fixed 8 chunk slots. Valid slots are marked by `BuiltChunks[index].IsUsed`.

## Rules

- Update jobs consume remove masks; they do not decide destruction volume overlap.
- Intermediate occupancy stays in affected-shape-local buffers.
- Final output is built shape data, not direct storage mutation.
- Commit/apply code decides whether to reuse the source shape handle, release it, or acquire new handles.
