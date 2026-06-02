# Shape Destruction Pipeline

This folder owns the runtime destruction pipeline for shape occupancy data. The pipeline operates on affected shapes and keeps transient destruction results in compact, affected-shape-local buffers instead of writing intermediate results back into the global shape storage.

## Destruction Shapes

A destruction shape uses a separate data model from a persistent world shape. It keeps the same fixed chunk-grid scale, but it does not use `ShapeDataContainer`:

```text
one shape = 8 fixed chunk slots
one chunk = 64B destruction mask bit plane
chunk slot index = stable local chunk index 0..7
```

The intended runtime model is to keep destruction shapes in `DestructionShapeDataStorage`. That storage owns a `DestructionDataContainer` with only the data destruction sources need:

```text
DestructionMasks
ChunkPositions
ChunkUsed
DestructionShapeMetadata
```

There is no `IsFace`, `IsEdge`, `IsCorner`, or body handle in destruction source storage. Those belong to persistent target shapes and rebuild output, not source masks.

Destruction shapes intentionally have weaker semantic requirements than persistent world shapes. Their 8 chunk slots do not need to form a connected or continuous region in `int3` chunk space. The chunk positions only describe where each destructive chunk samples against target geometry.

This keeps the destruction side small without coupling it to persistent shape-derived data. Destruction jobs read destruction masks as source geometry, then emit remove commands against target shapes. The update side still treats the result as destructive intent only:

```text
DestructionShapeDataStorage -> remove commands -> packed remove masks -> rebuilt target shapes
```

## Pipeline Stages

```text
DestructionShapeChunkOverlapJob
    -> DestructionChunkOverlapPrefixSumJob
    -> DestructionMaskGenerationJob
    -> ShapeVoxelRemoveBuildKeyJob
    -> sort command keys
    -> ShapeVoxelRemoveBuildRangeJob
    -> ShapeVoxelRemovePackJob
    -> ShapeVoxelRemoveJob
    -> ShapeChunkFragmentJob
    -> ShapeChunkCheckMaskJob
    -> ShapeChunkConnectivityJob
    -> ShapeFragmentUnionJob
    -> ShapePrefixSumComputeJob
    -> ShapeBuildJob
    -> CommitBuiltShapes
```

Cross-chunk fragmentation is split into explicit stages: `ShapeChunkCheckMaskJob` builds candidate neighbor masks, `ShapeChunkConnectivityJob` performs the face-bit checks, `ShapeFragmentUnionJob` groups connected fragment nodes, `ShapePrefixSumComputeJob` compacts per-shape output ranges, and `ShapeBuildJob` constructs final fixed-slot shape buffers for commit.

Debug pipelines should use the same producer path as runtime destruction. A debugger can create a transient destruction shape, run chunk overlap and mask generation, then feed the resulting `ShapeVoxelRemoveCommandBuffer` through the pack jobs. The old volume-mask path is no longer the authority.

`ShapeDestructionPipelineBenchmark` is the non-physics benchmark path. It creates synthetic full-chunk shapes, runs the same destruction producer and update jobs through `ShapeBuildJob`, and reports total plus per-stage timings for several affected-shape counts. It intentionally does not commit built shapes back to storage, refresh render data, or touch physics components, so the numbers describe the destruction jobs and their local staging overhead only.

`ShapeDestructionPipelinePerformanceTests` is the formal Unity Performance Test entry point. It runs the same benchmark iteration through `Unity.PerformanceTesting.Measure.Method`, currently with 6 warmup iterations and 30 measurement iterations per shape-count case.

## Future Optimization

The main structural optimization target is the destruction producer side:

```text
ChunkOverlapMasks -> DestructionMaskGenerationJob -> ShapeVoxelRemoveCommandBuffer -> sort -> pack
```

Current command granularity is:

```text
target shape + target chunk + destruction chunk = one 64B command mask
```

This keeps `DestructionMaskGenerationJob` simple, but it can produce multiple commands for the same target shape chunk when that chunk overlaps several destruction chunks. Those commands are later sorted and merged with bitwise AND in `ShapeVoxelRemovePackJob`.

From a cache/TLB and memory-bandwidth perspective, this can cause write and read amplification:

```text
CommandMasks = sum(popcount(ChunkOverlapMasks)) * 64B
```

The final semantic target is still only one keep mask per target shape chunk:

```text
target shape + target chunk = one final 64B keep mask
```

A future optimization should consider merging all overlapped destruction chunks inside `DestructionMaskGenerationJob` and emitting at most one command per target shape chunk:

```text
CommandMasks = overlappedTargetChunkCount * 64B
```

Expected benefits:

```text
fewer command masks
less mask initialization and write bandwidth
fewer sort keys
lower sort and range-build cost
less pack-stage AND merging
better cache and TLB locality for large affected-shape counts
```

The tradeoff is that `DestructionMaskGenerationJob` becomes responsible for combining all destruction chunk hits for a target chunk into one keep mask. This should be evaluated after profiling large shape-count cases.

## Remove Command Buffer

`ShapeVoxelRemoveCommandBuffer` is a plain SoA Native container struct. It does not own container lifetime and does not reserve or clear memory. The producer pipeline or business jobs provide the backing containers and fill the valid range.

```text
ShapeHandles = commandCapacity
ChunkSlots   = commandCapacity
Masks        = commandCapacity * 64B
```

```text
CommandCount     = number of valid commands
CommandCapacity  = maximum command count
```

Only commands in `[0, CommandCount)` are valid. Data outside that range is unspecified and should be treated as garbage. Every writer must fully write the 64B mask for each command because masks are not implicitly cleared.

The intended producer flow is still external to the buffer:

```text
CountJob -> PrefixSumJob -> allocate/reserve backing range -> WriteJob
```

`ShapeVoxelRemoveBuildKeyJob` is the first pack staging job. It converts each valid SoA command into a sortable key:

```text
ShapeVoxelRemoveCommandKey
    ShapeHandle
    ChunkSlot
    CommandIndex
```

The key sort order is:

```text
ShapeHandle asc
ChunkSlot asc
CommandIndex asc
```

After sorting, all commands for the same shape are contiguous, and commands for the same shape chunk are contiguous inside that shape range.

`ShapeVoxelRemoveBuildRangeJob` scans sorted keys and emits one range per unique shape:

```text
ShapeVoxelRemoveCommandRange
    ShapeHandle
    StartKeyIndex
    KeyCount
```

Outputs:

```text
AffectedShapeHandles = uniqueShapeCount
ShapeRanges          = uniqueShapeCount
UniqueShapeCount     = 1
```

The first version is a single `IJob` scan. It only reads keys and does not touch 64B masks.

`ShapeVoxelRemovePackJob` consumes sorted keys and shape ranges, then writes the input buffers expected by `ShapeVoxelRemoveJob`:

```text
AffectedShapeHandles = uniqueShapeCount
RemoveMasks          = uniqueShapeCount * 8 * 64B
```

`AffectedShapeHandles` is written by `ShapeVoxelRemoveBuildRangeJob`. `ShapeVoxelRemovePackJob` writes only `RemoveMasks`.

Scheduling unit:

```text
ShapeVoxelRemovePackJob Execute(index) = one unique affected shape
```

For each shape it initializes all 8 chunk masks to `0xFF`, then traverses that shape's key range in order:

```text
for key in ShapeRanges[shapeListIndex]:
    chunkSlot    = key.ChunkSlot
    commandIndex = key.CommandIndex
    RemoveMasks[shapeListIndex, chunkSlot] &= CommandMasks[commandIndex]
```

This means multiple commands for the same shape chunk are merged by sequential AND inside one `Execute`, with no cross-thread writes.

Two layout guarantees are required:

```text
unaffected chunk slot = 64B of 0xFF
output local chunk slot = original source shape chunk slot
```

`ShapeVoxelRemovePackJob` satisfies both by initializing the full `8 * 64B` mask range for the shape before applying commands, then writing command masks directly to `key.ChunkSlot`. It does not compact or reorder chunk slots.

## Remove Masks

`RemoveMasks` use keep-mask semantics:

```text
bit 1 = keep voxel
bit 0 = remove voxel
```

The mask job initializes candidate masks to `0xFF` and clears bits hit by the destruction volume. During compaction, affected shapes are packed into shape-local layout:

```text
RemoveMasks = affectedShapeCount * 8 * 64B
```

Chunks that were not modified still receive an all-ones mask. This lets the remove stage process every slot uniformly:

```text
target = source & removeMask
```

## Continuous Working Set

`ShapeVoxelRemoveJob` writes the post-remove occupancy into a continuous affected-shape buffer:

```text
removedIsOccupied = affectedShapeCount * 8 * 64B
```

The output order is shape-local:

```text
localChunkIndex = shapeListIndex * 8 + chunkSlot
targetBase      = localChunkIndex * 64
```

The job reads source occupancy from global `ShapeDataView.IsOccupied`, but it writes only to the compact working set. Later fragment jobs read the compact working set, not the original source occupancy.

## Job Granularity

Current granularity is intentionally mixed:

```text
ShapeVoxelRemoveJob         Execute(index) = one affected shape, internally loops 8 chunk slots
ShapeChunkFragmentJob       Execute(index) = one affected shape chunk slot
ShapeChunkCheckMaskJob      Execute(index) = one affected shape
ShapeChunkConnectivityJob   Execute(index) = one fragment node
ShapeFragmentUnionJob       Execute(index) = one affected shape
ShapePrefixSumComputeJob    Execute() = one affected-shape batch
ShapeBuildJob               Execute(index) = one affected source shape
```

`ShapeVoxelRemoveJob` stays shape-indexed because remove work is small and fixed: one shape is exactly an 8-slot buffer. This is stronger and clearer than relying on `IJobParallelFor` batch size to keep 8 chunk indices together.

`ShapeChunkFragmentJob` stays chunk-indexed because flood-fill cost can vary a lot per chunk. Its data layout is still shape-local and contiguous, so scheduling can use a batch size of `8` when we want the job system to prefer one shape's chunk slots as a scheduling batch:

```text
index layout: shape0 chunk0..7, shape1 chunk0..7, ...
batch size:   8
```

This is only a scheduling hint. Correctness must not depend on a shape staying on the same worker thread across jobs.

## Fragment Outputs

Each input chunk slot can emit at most 4 chunk-local fragments:

```text
OutputIsOccupied           = affectedShapeCount * 8 * 4 * 64B
OutputGeneratedChunkCounts = affectedShapeCount * 8
```

Voxel counts are kept as local variables inside `ShapeChunkFragmentJob` only for top-4 selection. They are not emitted because downstream jobs currently do not read them.

## Chunk Check Masks

`ShapeChunkCheckMaskJob` is a staging job for splitting position lookup from face connectivity checks. It does not test whether two fragments are actually connected. It only tells each fragment node which other fragment nodes should be checked on each positive face direction.

The job is shape-indexed:

```text
ShapeChunkCheckMaskJob Execute(index) = one affected shape
```

For each shape it first builds a local neighbor table on the original 8 chunk slots:

```text
NeighborChunkSlots[8 * 3]

direction 0 = +X
direction 1 = +Y
direction 2 = +Z
```

Each neighbor table entry stores the adjacent original chunk slot index, or `-1` when there is no neighbor. The table is local to the job execution and is not emitted.

The output is per fragment node and per positive direction:

```text
FragmentCheckMasks = affectedShapeCount * 32 * 3
```

Indexing:

```text
node = chunkSlot * 4 + fragmentSlot
maskIndex = (shapeListIndex * 32 + node) * 3 + direction
```

Each `uint` uses 32 bits to identify candidate fragment nodes:

```text
bit N = this node should check against node N on this direction
```

For example, if node 6 has a `+X` neighbor chunk slot with three valid fragments at nodes 20, 21, and 22, then:

```text
FragmentCheckMasks[(shape * 32 + 6) * 3 + +X] = bit20 | bit21 | bit22
```

The actual face-bit connectivity test belongs to a later job. That later job can read the direction-specific check mask, perform the face test, and emit real connectivity edges for union.

## Chunk Connectivity Masks

`ShapeChunkConnectivityJob` consumes the direction-specific check masks and performs the actual face-bit tests. Its scheduling unit is one fragment node:

```text
ShapeChunkConnectivityJob Execute(index) = one fragment node
recommended batch size = 32
```

The index layout is shape-local:

```text
shapeListIndex = index / 32
node           = index % 32
```

Inputs:

```text
FragmentIsOccupied  = affectedShapeCount * 32 * 64B
FragmentCounts      = affectedShapeCount * 8
FragmentCheckMasks  = affectedShapeCount * 32 * 3
```

Output:

```text
FragmentConnectionMasks = affectedShapeCount * 32 * 3
```

The output uses the same indexing as `FragmentCheckMasks`:

```text
maskIndex = (shapeListIndex * 32 + node) * 3 + direction
```

Each `uint` contains only the nodes that are actually face-connected on that positive direction:

```text
bit N = this node's +X/+Y/+Z face is connected to node N
```

The job only writes the current node's positive-direction masks. It does not write reverse-direction masks, which avoids cross-thread writes. The later union job can still consume these directed positive edges directly:

```text
for each node:
    for each positive direction:
        union node with every bit set in FragmentConnectionMasks[node, direction]
```

Face checks use the existing 64B fragment occupancy layout directly:

```text
+X: scan 64 rows, A bit7 against B bit0
+Y: scan z=0..7, A y=7 row AND B y=0 row
+Z: scan y=0..7, A z=7 row AND B z=0 row
```

## Fragment Union

`ShapeFragmentUnionJob` consumes `FragmentConnectionMasks` and produces compact local shape ids for `ShapeBuildJob`:

```text
ShapeFragmentUnionJob Execute(index) = one affected shape
```

Inputs:

```text
FragmentCounts           = affectedShapeCount * 8
FragmentConnectionMasks  = affectedShapeCount * 32 * 3
```

Outputs:

```text
FragmentLocalShapeIds = affectedShapeCount * 32
LocalShapeCounts      = affectedShapeCount
```

The job initializes a fixed 32-node union-find for valid fragment nodes:

```text
node = chunkSlot * 4 + fragmentSlot
valid when fragmentSlot < FragmentCounts[shapeListIndex * 8 + chunkSlot]
```

It then reads every positive-direction connection mask and unions each set bit:

```text
for node in 0..31:
    for direction in +X/+Y/+Z:
        mask = FragmentConnectionMasks[node, direction]
        union node with every bit set in mask
```

Finally it compresses union roots into consecutive local shape ids:

```text
FragmentLocalShapeIds[node] = localShapeId or -1
LocalShapeCounts[shape]     = number of generated local shapes
```

## Shape Prefix Sum

`ShapePrefixSumComputeJob` consumes `LocalShapeCounts` after union and computes compact output ranges for build:

```text
ShapePrefixSumComputeJob Execute() = one affected-shape batch
```

Inputs:

```text
LocalShapeCounts = affectedShapeCount
```

Outputs:

```text
BuiltShapeOffsets    = affectedShapeCount
TotalBuiltShapeCount = 1
```

`BuiltShapeOffsets` is an exclusive prefix sum:

```text
BuiltShapeOffsets[shapeListIndex] = first compact built shape index for this source shape
TotalBuiltShapeCount              = sum(LocalShapeCounts)
```

The pipeline completes this job before allocating build output buffers, because `BuiltChunks` and `BuiltIsOccupied` are sized from `TotalBuiltShapeCount`.

## Shape Build

`ShapeBuildJob` consumes `ShapeFragmentUnionJob` output and constructs final built shape buffers. Its scheduling unit is one affected source shape:

```text
ShapeBuildJob Execute(index) = one affected source shape
```

Inputs:

```text
ShapeHandles
ChunkPositions
FragmentIsOccupied      = affectedShapeCount * 32 * 64B
FragmentLocalShapeIds   = affectedShapeCount * 32
LocalShapeCounts        = affectedShapeCount
BuiltShapeOffsets       = affectedShapeCount
```

Outputs:

```text
BuiltChunks      = TotalBuiltShapeCount * 8 BuiltChunkMetadata
BuiltIsOccupied  = TotalBuiltShapeCount * 8 * 64B
```

The shape dimension is compact:

```text
builtShapeIndex = BuiltShapeOffsets[shapeListIndex] + localShapeId
localShapeId    = 0..LocalShapeCounts[shapeListIndex]-1
```

The chunk dimension is fixed at 8 slots per built shape. A fragment node maps back to its original source chunk slot:

```text
node            = sourceChunkSlot * 4 + fragmentSlot
builtChunkSlot  = sourceChunkSlot
builtChunkIndex = builtShapeIndex * 8 + builtChunkSlot
```

When multiple fragment nodes from the same source chunk slot belong to the same local shape id, `ShapeBuildJob` ORs their occupancy into the same built chunk. This happens sequentially inside one `Execute(shapeListIndex)`, so no atomics are required. Different source shapes write disjoint output ranges.

Chunk usage is represented by `BuiltChunks[builtChunkIndex].IsUsed`. There is no separate chunk count in the new build output; commit should scan the fixed 8 slots and use `IsUsed` as the authority.
