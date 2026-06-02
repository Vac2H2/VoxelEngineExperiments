# Destruction Jobs

This folder contains jobs that produce remove-mask data. These jobs decide which existing voxels should be removed, but they do not rewrite shape storage and they do not build new shapes.

## Boundary

Destruction source geometry uses `DestructionShapeDataStorage`, not regular `ShapeDataStorage`. A destruction shape has 8 fixed chunk slots, and each used chunk stores a 64B destruction mask bit plane.

The source container is intentionally smaller than persistent shape storage:

```text
DestructionMasks
ChunkPositions
ChunkUsed
DestructionShapeMetadata
```

It does not store `IsFace`, `IsEdge`, `IsCorner`, or a body handle.

Destruction shapes do not need the same internal topology guarantees as persistent world shapes. Their used chunk slots do not need to be connected or continuous in `int3` chunk space.

Destruction jobs may read that storage as source geometry, but their output remains remove-mask intent for target shapes.

Destruction jobs are producers. Their output is destructive intent:

```text
bit 1 = keep voxel
bit 0 = remove voxel
```

The consumer side expects the intent to become this packed shape-local layout:

```text
AffectedShapeHandles = affectedShapeCount
RemoveMasks          = affectedShapeCount * 8 * 64B
```

Every affected shape appears once in `AffectedShapeHandles`. Every shape has exactly 8 chunk masks. Chunk slots with no destructive command must be all `0xFF`.

## Current Jobs

### DestructionShapeChunkOverlapJob

`DestructionShapeChunkOverlapJob` refines known destruction-shape / target-shape collision pairs into chunk-level candidate masks. It does not read voxel masks and does not emit remove commands.

The job is indexed by destruction shape:

```text
DestructionShapeChunkOverlapJob Execute(index) = one destruction shape
```

Inputs:

```text
DestructionShapeHandles
TargetShapeHandles
TargetShapeRangeOffsets
DestructionShapeLocalToWorlds
TargetShapeLocalToWorlds
DestructionChunkPositions
DestructionChunkUsed
TargetChunkPositions
TargetChunkUsed
```

`TargetShapeRangeOffsets` is an exclusive prefix-sum array with length `DestructionShapeHandles.Length + 1`:

```text
targetStart = TargetShapeRangeOffsets[destructionShapeListIndex]
targetEnd   = TargetShapeRangeOffsets[destructionShapeListIndex + 1]
```

Output:

```text
PairDestructionShapeHandles = targetShapePairCount
ChunkOverlapMasks           = targetShapePairCount * 8 bytes
ChunkOverlapCounts          = targetShapePairCount * 8 ints
```

Each collision pair owns 8 bytes, one byte per target chunk slot:

```text
maskIndex = pairIndex * 8 + targetChunkSlot
bit D     = target chunk overlaps destruction chunk slot D
```

`PairDestructionShapeHandles[pairIndex]` stores the destruction shape handle for the pair. `TargetShapeHandles[pairIndex]` already stores the target shape handle, so later jobs do not need to reverse-search `TargetShapeRangeOffsets`.

`ChunkOverlapCounts` uses the same indexing as `ChunkOverlapMasks`:

```text
ChunkOverlapCounts[maskIndex] = popcount(ChunkOverlapMasks[maskIndex])
```

This count is the number of destruction-chunk / target-chunk voxel mask blocks needed by the next stage. The pipeline can run an exclusive prefix sum over `ChunkOverlapCounts`:

```text
blockOffset = ChunkOverlapOffsets[maskIndex]
blockCount  = ChunkOverlapCounts[maskIndex]
```

Then the voxel mask job can write directly into:

```text
[blockOffset, blockOffset + blockCount)
```

with one 64B remove mask block per set destruction chunk bit.

Chunk-level overlap is tested as world-space OBB overlap. The job does not store transforms in `ShapeDataStorage` or `DestructionShapeDataStorage`; both transforms are pipeline inputs:

```text
target chunk local AABB      = [targetChunkPosition * 8, targetChunkPosition * 8 + 8)
destruction chunk local AABB = [destructionChunkPosition * 8, destructionChunkPosition * 8 + 8)

target chunk OBB      = TargetShapeLocalToWorlds[targetShapeHandle] * target chunk local AABB
destruction chunk OBB = DestructionShapeLocalToWorlds[destructionShapeHandle] * destruction chunk local AABB
```

Every pair byte and count is fully written by the job. Unused target chunks, unused destruction chunks, and non-overlapping chunk positions produce `0`.

### DestructionChunkOverlapPrefixSumJob

`DestructionChunkOverlapPrefixSumJob` converts chunk overlap counts into compact voxel mask block ranges.

The job is a single `IJob` synchronization point:

```text
DestructionChunkOverlapPrefixSumJob Execute() = one chunk-overlap count array
```

Inputs:

```text
ChunkOverlapCounts = targetShapePairCount * 8 ints
```

Outputs:

```text
ChunkOverlapOffsets       = targetShapePairCount * 8 ints
TotalVoxelMaskBlockCount  = 1 int
```

The prefix sum is exclusive:

```text
ChunkOverlapOffsets[i]      = sum(ChunkOverlapCounts[0..i))
TotalVoxelMaskBlockCount[0] = sum(ChunkOverlapCounts)
```

The next voxel mask job uses the offset and count for each pair target chunk:

```text
blockOffset = ChunkOverlapOffsets[maskIndex]
blockCount  = ChunkOverlapCounts[maskIndex]
```

Then it writes one 64B remove mask block per set destruction chunk bit into:

```text
[blockOffset, blockOffset + blockCount)
```

### DestructionMaskGenerationJob

`DestructionMaskGenerationJob` consumes chunk-overlap masks and writes directly into `ShapeVoxelRemoveCommandBuffer`.

The job is indexed by pair target chunk:

```text
DestructionMaskGenerationJob Execute(index) = one pair target chunk
index = pairIndex * 8 + targetChunkSlot
recommended batch size = 8
```

Inputs:

```text
PairDestructionShapeHandles
TargetShapeHandles
ChunkOverlapMasks
ChunkOverlapOffsets
DestructionShapeLocalToWorlds
TargetShapeLocalToWorlds
DestructionMasks
DestructionChunkPositions
TargetIsOccupied
TargetChunkPositions
```

Output:

```text
ShapeVoxelRemoveCommandBuffer.ShapeHandles
ShapeVoxelRemoveCommandBuffer.ChunkSlots
ShapeVoxelRemoveCommandBuffer.Masks
```

Each set bit in `ChunkOverlapMasks[index]` becomes one command:

```text
commandIndex = ChunkOverlapOffsets[index] + localSetBitIndex

ShapeHandles[commandIndex] = targetShapeHandle
ChunkSlots[commandIndex]   = targetChunkSlot
Masks[commandIndex]        = 64B keep-mask
```

The command mask uses keep-mask semantics:

```text
bit 1 = keep target voxel
bit 0 = remove target voxel
```

For each occupied target voxel, the job maps the target voxel center into destruction shape local space. It then checks the destruction chunk cells within `+-0.5` voxel around that point, clamped to the candidate destruction chunk. This keeps the per-voxel search bounded to at most 8 destruction voxels in the common case while still handling fractional local positions. If a destructive source voxel is found, the target keep bit is cleared.

The job does not compact out all-`0xFF` commands. `TotalVoxelMaskBlockCount` comes from chunk overlap counts, so a command may represent a chunk-level overlap that removes no final voxels. This is valid because later packing merges command masks with bitwise AND.

## Rules

- Do not mutate `ShapeDataStorage` here.
- Do not fragment or rebuild shapes here.
- Emit keep-mask data only.
- Multiple destructive sources may produce multiple masks for the same shape chunk; later packing merges them with bitwise AND.
