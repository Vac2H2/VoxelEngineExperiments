# Shape

`Shape` 是最基本的体素存储单位。底层内存侧不接受可变尺寸 Shape：每个 Shape 固定占用 `8` 个 chunk，每个 chunk 固定为 `8 x 8 x 8` 个 voxel。

```text
chunkSize                 = 8
chunksPerShape            = 8
voxelsPerChunk            = 8 * 8 * 8 = 512
bitPlaneBytesPerChunk     = 8 * 8 = 64
bitPlaneBytesPerShape     = 8 * 64 = 512
currentBitPlaneCount      = 4
currentDataBytesPerShape  = 4 * 512 = 2048
```

每个 chunk 的体素信息用 bitplane 表达：

```text
chunkBase        = shapeBase + chunkIndex * 64
byteIndex        = chunkBase + y + 8 * z
bitMask          = 1 << x
```

一个 bitplane chunk 固定 `64B`，也就是 `8 * 8 * 8 bits = 512 bits = 64 bytes`。一个 Shape 固定包含 `8` 个这样的 chunk，所以每个 plane 在单个 Shape 上占 `8 * 64B = 512B`。

`x` 不是 byte slice；`x` 是一个 byte 内的 bit 位。`y + 8 * z` 找到固定 `(y, z)` 的那条 X-row byte，`x` 决定这个 byte 里的具体 bit。

## 哲学

- Shape 的内存大小固定，业务层必须把输入整理成最多 `8` 个 chunk 的 Shape。
- Storage 不根据形状大小分配不同内存；`int` handle 就是 Shape slot index。
- ShapeDataStorage 容量固定，不扩容；容量不足是上层规划问题，不是底层 storage 自动解决的问题。
- ShapeDataStorage 是唯一的 shape slot 生命周期入口：负责 `Acquire/Release`、空闲栈、shape metadata，以及对 data container 的封装。
- ShapeDataContainer 只持有固定容量 native 数据：体素 bitplane 和 chunk slot SoA。
- ShapeDataView 是全量容器视图，不代表单个 Shape，也不保存 `ShapeIndex`；调用侧用 shape handle 自己算 offset。
- 每个 Shape 必须有且只属于一个 Body；这个关系记录在 `ShapeMetadata.BodyHandle`。
- Shape 内部必须连通，不能有孤岛；出现孤岛时，上层构建流程应该拆成两个或多个 Shape。
- Storage 只负责底层 slot 生命周期和数据访问，不负责猜测、修正或合并业务语义。
- Shape 不保存 chunk lookup、chunk 聚合 bounds、chunk mask 或 chunk 连接性；这些都是 `ChunkPositions/ChunkUsed/IsOccupied` 的派生结果，需要时扫描最多 8 个 chunk slot 计算。

## ShapeDataContainer

`ShapeDataContainer` 只管理固定容量 native buffer。它不分配 handle，也不解释业务命令。

它持有四个固定容量 SoA bitplane：

```csharp
NativeArray<byte> isOccupied;
NativeArray<byte> isFace;
NativeArray<byte> isEdge;
NativeArray<byte> isCorner;
```

每个数组按 shape handle 切分。`ShapeDataStorage` 的 handle 直接对应 data container 里的 shape slot：

```text
shapeBase = shapeHandle * bitPlaneBytesPerShape
chunkBase = shapeBase + chunkIndexInShape * bitPlaneBytesPerChunk
```

chunk slot 数据也保存在 `ShapeDataContainer`，布局为：

```csharp
NativeArray<int3> ChunkPositions; // shapeCapacity * 8
NativeArray<byte> ChunkUsed;      // shapeCapacity * 8
```

```text
chunkSlotIndex = shapeHandle * chunksPerShape + chunkIndexInShape
```

`ShapeDataContainer.GetView(NativeArray<ShapeMetadata> shapes)` 只把这些 buffer 组装成完整 `ShapeDataView`。shape handle 的合法性由 `ShapeDataStorage` 检查。

## ShapeDataView

`ShapeDataView` 是给 Job 和上层流程使用的全量容器视图，包含：

```csharp
NativeArray<ShapeMetadata> Shapes;
NativeArray<byte> IsOccupied;
NativeArray<byte> IsFace;
NativeArray<byte> IsEdge;
NativeArray<byte> IsCorner;
NativeArray<int3> ChunkPositions;
NativeArray<byte> ChunkUsed;
```

View 不预先切 `NativeSlice`，也不携带 `ShapeIndex`。调用侧根据 shape handle 自己计算该 Shape 的起始位置：

```text
shapeBase = shapeHandle * bitPlaneBytesPerShape
```

## ShapeMetadata

`ShapeDataStorage` 持有固定容量 shape metadata array：

```csharp
NativeArray<ShapeMetadata> shapes;
```

每个 Shape 固定拥有 `8` 个 chunk slot。slot 的 `ChunkPosition` 和 `ChunkUsed` 保存在 `ShapeDataContainer`，shape metadata 只维护 shape 生命周期和 body 归属。

`ShapeMetadata` 一一对应一个 shape handle，包含：

```csharp
int BodyHandle;
byte IsUsed;
```

需要判断 chunk 数量、chunk bounds，或从 `shapeHandle + chunkPosition` 找到具体 chunk slot 时，直接扫描该 Shape 的 8 个 `ChunkPositions/ChunkUsed` slot。Shape 不维护额外 lookup 或聚合缓存，避免冗余数据没有权威来源。

`ShapeDataView.Shapes` 返回 shape metadata array。Job 或上层流程自己决定如何查询和写入。

Shape 不保存 chunk 之间的连接性缓存。连接性是 occupancy 和 chunk slot 数据的派生结果；受 destruction 或 split 影响后，由 fragment job 从当前数据重新计算。

## ShapeDataStorage

`ShapeDataStorage` 是 shape storage 的对外入口，持有：

```csharp
NativeArray<int> _freeShapeHandles;
NativeArray<ShapeMetadata> _shapes;
ShapeDataContainer _shapeDataContainer;
```

空闲 handle 使用 `NativeArray<int> + count` 固定容量栈。原因是 storage 的容量必须在构造时确定，后续不允许增长；`NativeArray` 最直接表达这条约束。

```csharp
int handle = storage.Acquire(bodyHandle);
bool released = storage.Release(handle);
```

`Acquire()` 返回一个可用的 `int` handle。优先复用 `_freeShapeHandles` 里的 handle；没有可复用 handle 时返回 `ShapeDataStorage.InvalidHandle`，不会扩容。

`Acquire(bodyHandle)` 会写入 `ShapeMetadata.BodyHandle` 和 `ShapeMetadata.IsUsed`。`Release(handle)` 会清掉该 shape 的 8 个 chunk slot、清掉 shape metadata，并把 handle 放回 `_freeShapeHandles`。

`GetShapeDataView(handle)` 先验证 handle 已使用，再返回全量 `ShapeDataView`。Storage 不提供写 voxel 的命令；后续修改数据发生在拿到 view 之后的上层流程里。

## Shape Jobs

### ShapeVoxelRemoveJob

`ShapeVoxelRemoveJob` 是体素破坏管线的 remove 阶段。调度单位是 Shape，而不是 chunk；每个执行项固定处理一个 Shape 的 8 个 chunk slot：

```text
jobCount          = ShapeHandles.Length
shapeHandle       = ShapeHandles[shapeListIndex]
chunkSlotIndex    = shapeHandle * 8 + chunkSlot
localChunkIndex   = shapeListIndex * 8 + chunkSlot
```

compact 阶段会把候选 chunk 输出整理成 Shape 局部 buffer：

```text
RemoveMasks  = ShapeHandles.Length * 8 * 64B
```

不需要修改的 chunk slot 由 compact 阶段写入全 `1` mask。`ShapeVoxelRemoveJob` 总是处理 Shape 的 8 个 slot，从完整全局 `SourceIsOccupied` 读取原始 chunk，应用对应 `RemoveMasks`，并把结果写入受影响 Shape 的连续输出内存：

```text
sourceBase = chunkSlotIndex * 64
targetBase = localChunkIndex * 64
maskBase   = localChunkIndex * 64
target     = source & removeMask
```

实现上保持 `NativeArray<byte>` 语义，直接按 64B bitplane 做 `source & mask`。这个阶段不再写回全局 shape 槽位，也不再需要单独的 apply job；输出是 `ShapeHandles.Length * 8 * 64B` 的连续 affected-shape occupancy，后续 Shape 级 fragment 直接读取这个连续工作集。

### ShapeChunkFragmentJob

`ShapeChunkFragmentJob` 是 Shape 级 fragment。输入是一批需要重算的 Shape handle：

```csharp
NativeArray<int> ShapeHandles;
NativeArray<byte> ChunkUsed;
NativeArray<byte> SourceIsOccupied;  // affected-shape contiguous IsOccupied
```

调度长度固定为：

```text
jobCount = ShapeHandles.Length * 8
```

每个执行项负责一个 Shape 的一个固定 chunk slot：

```text
shapeListIndex    = jobIndex / 8
chunkIndexInShape = jobIndex % 8
shapeHandle       = ShapeHandles[shapeListIndex]
chunkSlotIndex = shapeHandle * 8 + chunkIndexInShape
sourceBase     = jobIndex * 64
```

如果对应 chunk slot 未使用，Job 只把 `OutputGeneratedChunkCounts[jobIndex]` 写成 `0` 并 early return；未使用的 fragment mask 槽由 count 控制，后续不读取。

已使用 chunk 会读取 `ShapeVoxelRemoveJob` 写好的连续 affected-shape `SourceIsOccupied`。Job 使用和 `ChunkFragmenterOptimizedJob` 同类的 X-run / slice flood-fill：扫描 `y + 8 * z` 对应的 X-row byte，找到包含起点 `x` 的连续 run，然后用 slice queue 沿相邻 `y/z` row 扩展重叠 run。

slice queue 使用外部传入的固定容量 `NativeArray<int>`，每个输入 chunk 独占一段：

```text
maxSliceCount  = 64 * (8 / 2) = 256
sliceQueueBase = jobIndex * maxSliceCount
```

这个布局和 `ChunkFragmenterOptimizedJob` 的 `SliceQueue[write++]` 模型一致，只是为了 `IJobParallelFor` 并行处理多个 chunk，把 queue 切成了 N 段。Job 内不创建 `NativeList`，也不扩容。

每个 fragment 完成 BFS 后立即统计体素数量：

```text
voxelCount < MinimalVoxelNumber => 丢弃
voxelCount >= MinimalVoxelNumber => 插入当前 chunk 的 Top4
```

每个输入 chunk 最多生成 `4` 个新区块。超过 4 个时，只保留体素数量最多的 4 个，并按数量降序写入输出槽：

```text
outputChunkIndex = jobIndex * 4 + fragmentSlot
outputBase       = outputChunkIndex * 64
```

输出布局固定为：

```csharp
NativeArray<byte> OutputIsOccupied;              // ShapeHandles.Length * 8 * 4 * 64
NativeArray<byte> OutputGeneratedChunkCounts;    // ShapeHandles.Length * 8
NativeArray<int> SliceQueue;                     // ShapeHandles.Length * 8 * 256
```

`OutputGeneratedChunkCounts[jobIndex]` 表示该 Shape chunk slot 实际生成了几个新区块，范围是 `0..4`。未使用或被过滤掉的输出槽由 count 忽略。

### ShapeChunkCheckMaskJob

`ShapeChunkCheckMaskJob` 紧跟在 `ShapeChunkFragmentJob` 后面执行。它每个执行项处理一个 Shape，只负责根据原始 8 个 chunk slot 的位置关系，告诉每个 fragment node 在 `+X/+Y/+Z` 三个正方向上应该检查哪些其他 fragment node：

```text
maxFragmentNodesPerShape = 8 * 4 = 32
nodeIndex                = chunkSlot * 4 + fragmentSlot
```

输出是每个方向一个 `uint` mask：

```csharp
NativeArray<uint> FragmentCheckMasks; // ShapeHandles.Length * 32 * 3
```

```text
maskIndex = (shapeListIndex * 32 + nodeIndex) * 3 + direction
bit N     = 当前 node 在该方向上需要和 node N 做连接检测
```

这个 job 不做体素面连接检测，只做候选关系构建。

### ShapeChunkConnectivityJob

`ShapeChunkConnectivityJob` 每个执行项处理一个 fragment node。它读取 `FragmentCheckMasks`，对候选 node 做真实的相邻面 bit 检测，并输出连接 mask：

```csharp
NativeArray<byte> FragmentIsOccupied;       // ShapeHandles.Length * 32 * 64
NativeArray<byte> FragmentCounts;           // ShapeHandles.Length * 8
NativeArray<uint> FragmentCheckMasks;       // ShapeHandles.Length * 32 * 3
NativeArray<uint> FragmentConnectionMasks;  // ShapeHandles.Length * 32 * 3
```

只检查 `+X/+Y/+Z`，避免重复计算，也避免跨线程反向写入：

```text
+X: A row bit 7 与 B row bit 0
+Y: A y=7 row 与 B y=0 row
+Z: A z=7 layer rows 与 B z=0 layer rows
```

建议调度时使用 `batchSize = 32`，让一个 Shape 的 32 个 fragment node 更倾向于被同一个 worker 批量领取；正确性不依赖这个调度提示。

### ShapeFragmentUnionJob

`ShapeFragmentUnionJob` 每个执行项处理一个 Shape。它读取 `FragmentConnectionMasks`，用固定 32 node 的 union-find 把真正连通的 fragment 合并成 local shape id：

```csharp
NativeArray<int> FragmentLocalShapeIds; // ShapeHandles.Length * 32
NativeArray<int> LocalShapeCounts;      // ShapeHandles.Length
```

`FragmentLocalShapeIds[shapeListIndex * 32 + nodeIndex]` 的值不是全局 Shape handle，而是这个原 Shape 被拆分后的 local shape id，范围是 `0..LocalShapeCounts[shapeListIndex]-1`。无效 fragment node 写成 `-1`。

### ShapePrefixSumComputeJob

`ShapePrefixSumComputeJob` 在 union 之后执行。它读取每个 source Shape 实际生成的 local shape 数量，并计算 build 输出的 compact 起始位置：

```csharp
NativeArray<int> LocalShapeCounts;        // ShapeHandles.Length
NativeArray<int> BuiltShapeOffsets;       // ShapeHandles.Length
NativeArray<int> TotalBuiltShapeCount;    // 1
```

语义是 exclusive prefix sum：

```text
BuiltShapeOffsets[shapeListIndex] = 当前 source shape 的第一个 compact built shape index
TotalBuiltShapeCount              = sum(LocalShapeCounts)
```

管线会在这个 job 完成后，根据 `TotalBuiltShapeCount` 分配 `BuiltChunks` 和 `BuiltIsOccupied`，避免继续为每个 source Shape 固定预留 32 个 built shape。

### ShapeBuildJob

`ShapeBuildJob` 每个执行项处理一个原 Shape。它读取 union 输出，并把 fragment 合并成最终可提交的 built shape 缓冲：

```csharp
NativeArray<int> BuiltShapeOffsets;           // ShapeHandles.Length
NativeArray<BuiltChunkMetadata> BuiltChunks;  // TotalBuiltShapeCount * 8
NativeArray<byte> BuiltIsOccupied;            // TotalBuiltShapeCount * 8 * 64
```

shape 维度是紧凑的：

```text
builtShapeIndex = BuiltShapeOffsets[shapeListIndex] + localShapeId
localShapeId    = 0..LocalShapeCounts[shapeListIndex]-1
```

chunk 维度固定为 8 个 slot，不再输出单独的 chunk count。`BuiltChunks[builtChunkIndex].IsUsed` 是该 slot 是否有效的权威数据：

```text
builtChunkIndex =
    builtShapeIndex * 8
    + sourceChunkSlot
```

同一个 local shape 内，如果多个 fragment node 来自同一个原始 chunk slot，它们会被顺序 OR 合并到同一个 built chunk：

```text
BuiltIsOccupied[builtChunk] |= FragmentIsOccupied[node]
```

合并发生在同一个 `Execute(shapeListIndex)` 内部，不会有并行写冲突。提交阶段扫描每个 built shape 的固定 8 个 slot，复用原 Shape handle 或 `Acquire` 新 handle 后写回 `ShapeDataStorage`。
