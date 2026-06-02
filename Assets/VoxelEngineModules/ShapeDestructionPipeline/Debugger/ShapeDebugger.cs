using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace VoxelEngineModules.Shape.Debugger
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShapeDebugRenderer))]
    [AddComponentMenu("VoxelEngine/Shape/Shape Debugger")]
    public sealed class ShapeDebugger : MonoBehaviour
    {
        private const int ChunkSize = ShapeDataContainer.ChunkSize;
        private const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        private const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;
        private const int BitPlaneBytesPerShape = ShapeDataContainer.BitPlaneBytesPerShape;

        [SerializeField] private Camera _camera;
        [SerializeField] private int _shapeCapacity = 16;
        [SerializeField] private float _voxelSize = 1.0f;
        [SerializeField] private float _maxRayDistance = 200.0f;
        [SerializeField] private float _maxDdaDistanceInVoxels = 512.0f;
        [SerializeField] private int _maxDdaSteps = 4096;
        [SerializeField, FormerlySerializedAs("_brushRadiusInVoxels")]
        private float _destructionRadiusInVoxels = 2.0f;
        [SerializeField] private int _minimalVoxelNumber = 2;
        [SerializeField] private bool _initializeOnStart = true;
        [SerializeField] private bool _useCameraCenter = false;
        [SerializeField] private bool _syncRendererImmediately = true;
        [SerializeField] private Vector3Int _initialShapeChunkDimensions = new Vector3Int(2, 2, 2);

        private readonly List<int> _liveShapeHandles = new List<int>();
        private readonly Dictionary<int, Color32> _shapeColors = new Dictionary<int, Color32>();
        private ShapeDataStorage _storage;
        private ShapeDebugRenderer _renderer;
        private int _nextBodyHandle = 1;
        private int _nextColorIndex;

        public ShapeDataStorage Storage => _storage;

        public void Configure(
            Camera debugCamera,
            int shapeCapacity,
            float destructionRadiusInVoxels,
            int minimalVoxelNumber,
            float voxelSize)
        {
            _camera = debugCamera;
            _shapeCapacity = math.max(1, shapeCapacity);
            _destructionRadiusInVoxels = math.max(0.0f, destructionRadiusInVoxels);
            _minimalVoxelNumber = math.max(2, minimalVoxelNumber);
            _voxelSize = Mathf.Max(0.000001f, float.IsFinite(voxelSize) ? voxelSize : 1.0f);
            SanitizeSerializedState();
        }

        public bool Strike()
        {
            Camera rayCamera = ResolveCamera();
            if (rayCamera == null)
            {
                return false;
            }

            Ray ray = _useCameraCenter
                ? rayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f))
                : rayCamera.ScreenPointToRay(GetPointerScreenPosition(rayCamera));
            return Strike(ray);
        }

        public bool Strike(Ray worldRay)
        {
            EnsureStorage();
            if (_storage == null ||
                !_storage.IsCreated ||
                !TryPickShape(worldRay, out ShapeHit hit))
            {
                return false;
            }

            bool changed = RunDestructionPipeline(hit.Voxel);
            if (changed && _syncRendererImmediately)
            {
                _renderer.SyncRenderBackend();
            }

            return changed;
        }

        private void Awake()
        {
            _renderer = GetComponent<ShapeDebugRenderer>();
            SanitizeSerializedState();
            EnsureStorage();
        }

        private void Start()
        {
            if (!_initializeOnStart)
            {
                return;
            }

            EnsureInitialShape();
            if (_syncRendererImmediately)
            {
                _renderer.SyncRenderBackend();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || !WasPrimaryClickPressed())
            {
                return;
            }

            Strike();
        }

        private void OnDestroy()
        {
            if (_renderer != null)
            {
                _renderer.Clear();
            }

            if (_storage != null)
            {
                _storage.Dispose();
                _storage = null;
            }

            _liveShapeHandles.Clear();
            _shapeColors.Clear();
        }

        private void OnValidate()
        {
            SanitizeSerializedState();
            if (_renderer != null)
            {
                _renderer.VoxelSize = _voxelSize;
            }
        }


        #region Initialization

        private void EnsureStorage()
        {
            if (_storage != null && _storage.IsCreated)
            {
                return;
            }

            _storage = new ShapeDataStorage(_shapeCapacity, Allocator.Persistent);
            _renderer = _renderer != null ? _renderer : GetComponent<ShapeDebugRenderer>();
            _renderer.VoxelSize = _voxelSize;
            _renderer.Bind(_storage);
        }

        private void EnsureInitialShape()
        {
            if (_liveShapeHandles.Count > 0)
            {
                return;
            }

            int shapeHandle = _storage.Acquire(_nextBodyHandle++);
            if (shapeHandle == ShapeDataStorage.InvalidHandle)
            {
                Debug.LogWarning("Shape debugger storage has no free slot for the initial shape.", this);
                return;
            }

            WriteInitialShape(shapeHandle);
            RegisterLiveShape(shapeHandle, AllocateColor());
        }

        private void WriteInitialShape(int shapeHandle)
        {
            Vector3Int dimensions = SanitizeInitialChunkDimensions(_initialShapeChunkDimensions);
            ShapeDataView dataView = _storage.GetShapeDataView(shapeHandle);
            int bodyHandle = dataView.Shapes[shapeHandle].BodyHandle;

            ClearShapeForRewrite(shapeHandle, bodyHandle, dataView);

            int chunkSlot = 0;
            for (int z = 0; z < dimensions.z; z++)
            {
                for (int y = 0; y < dimensions.y; y++)
                {
                    for (int x = 0; x < dimensions.x; x++)
                    {
                        int3 chunkPosition = new int3(x, y, z);
                        int chunkMetadataIndex = shapeHandle * ChunksPerShape + chunkSlot;
                        WriteFullChunk(dataView.IsOccupied, chunkMetadataIndex);
                        WriteChunkSlotData(shapeHandle, bodyHandle, chunkSlot, chunkPosition, dataView);
                        chunkSlot++;
                    }
                }
            }
        }

        private static Vector3Int SanitizeInitialChunkDimensions(Vector3Int value)
        {
            int x = Mathf.Clamp(value.x, 1, ChunksPerShape);
            int y = Mathf.Clamp(value.y, 1, ChunksPerShape);
            int z = Mathf.Clamp(value.z, 1, ChunksPerShape);

            while (x * y * z > ChunksPerShape)
            {
                if (x >= y && x >= z)
                {
                    x--;
                }
                else if (y >= z)
                {
                    y--;
                }
                else
                {
                    z--;
                }
            }

            return new Vector3Int(x, y, z);
        }

        #endregion


        #region Destruction Pipeline

        private bool RunDestructionPipeline(int3 hitVoxel)
        {
            if (!TryBuildDestructionInputs(
                    hitVoxel,
                    out NativeArray<byte> removeMasks,
                    out NativeArray<int> affectedShapeHandles))
            {
                return false;
            }

            ShapeDataView dataView = _storage.GetShapeDataView(affectedShapeHandles[0]);
            ShapeDestructionUpdatePipelineOutput updateOutput = default;

            try
            {
                updateOutput = new ShapeDestructionUpdatePipeline().Run(
                    affectedShapeHandles,
                    dataView,
                    removeMasks,
                    _minimalVoxelNumber,
                    Allocator.TempJob);

                return CommitBuiltShapes(
                    affectedShapeHandles,
                    updateOutput.LocalShapeCounts,
                    updateOutput.BuiltShapeOffsets,
                    updateOutput.BuiltChunks,
                    updateOutput.BuiltIsOccupied);
            }
            finally
            {
                DisposeIfCreated(removeMasks);
                DisposeIfCreated(affectedShapeHandles);
                updateOutput.Dispose();
            }
        }

        private bool TryBuildDestructionInputs(
            int3 hitVoxel,
            out NativeArray<byte> removeMasks,
            out NativeArray<int> affectedShapeHandles)
        {
            removeMasks = default;
            affectedShapeHandles = default;

            ShapeDataView dataView = _storage.GetShapeDataView(_liveShapeHandles[0]);
            Matrix4x4 targetShapeLocalToWorld = CreateShapeVoxelToWorldMatrix();
            NativeArray<int> destructionShapeHandles = default;
            NativeArray<float4x4> destructionShapeLocalToWorlds = default;
            NativeArray<byte> destructionMasks = default;
            NativeArray<int3> destructionChunkPositions = default;
            NativeArray<byte> destructionChunkUsed = default;
            NativeArray<int> targetShapeHandles = default;
            NativeArray<int> targetShapeRangeOffsets = default;
            NativeArray<float4x4> targetShapeLocalToWorlds = default;
            NativeArray<int> pairDestructionShapeHandles = default;
            NativeArray<byte> chunkOverlapMasks = default;
            NativeArray<int> chunkOverlapCounts = default;
            NativeArray<int> chunkOverlapOffsets = default;
            NativeArray<int> totalVoxelMaskBlockCount = default;
            NativeArray<int> commandShapeHandles = default;
            NativeArray<byte> commandChunkSlots = default;
            NativeArray<byte> commandMasks = default;
            NativeArray<ShapeVoxelRemoveCommandKey> commandKeys = default;
            NativeArray<int> affectedShapeHandleScratch = default;
            NativeArray<ShapeVoxelRemoveCommandRange> shapeRanges = default;
            NativeArray<int> uniqueShapeCount = default;

            try
            {
                if (!TryCreateSphereDestructionShape(
                        hitVoxel,
                        out Bounds destructionWorldBounds,
                        out destructionShapeHandles,
                        out destructionShapeLocalToWorlds,
                        out destructionMasks,
                        out destructionChunkPositions,
                        out destructionChunkUsed))
                {
                    return false;
                }

                targetShapeHandles = CreateTargetShapeHandles(
                    destructionWorldBounds,
                    dataView,
                    targetShapeLocalToWorld);
                if (targetShapeHandles.Length == 0)
                {
                    return false;
                }

                targetShapeRangeOffsets = new NativeArray<int>(
                    2,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                targetShapeRangeOffsets[0] = 0;
                targetShapeRangeOffsets[1] = targetShapeHandles.Length;

                targetShapeLocalToWorlds = new NativeArray<float4x4>(
                    _storage.ShapeSlotCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                float4x4 targetShapeLocalToWorld4x4 = ToFloat4x4(targetShapeLocalToWorld);
                for (int i = 0; i < targetShapeHandles.Length; i++)
                {
                    targetShapeLocalToWorlds[targetShapeHandles[i]] = targetShapeLocalToWorld4x4;
                }

                int pairCount = targetShapeHandles.Length;
                int chunkOverlapCount = pairCount * ChunksPerShape;
                pairDestructionShapeHandles = new NativeArray<int>(
                    pairCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                chunkOverlapMasks = new NativeArray<byte>(
                    chunkOverlapCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                chunkOverlapCounts = new NativeArray<int>(
                    chunkOverlapCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                chunkOverlapOffsets = new NativeArray<int>(
                    chunkOverlapCount,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);
                totalVoxelMaskBlockCount = new NativeArray<int>(
                    1,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);

                JobHandle overlapHandle = new DestructionShapeChunkOverlapJob
                {
                    DestructionShapeHandles = destructionShapeHandles,
                    TargetShapeHandles = targetShapeHandles,
                    TargetShapeRangeOffsets = targetShapeRangeOffsets,
                    DestructionShapeLocalToWorlds = destructionShapeLocalToWorlds,
                    TargetShapeLocalToWorlds = targetShapeLocalToWorlds,
                    DestructionChunkPositions = destructionChunkPositions,
                    DestructionChunkUsed = destructionChunkUsed,
                    TargetChunkPositions = dataView.ChunkPositions,
                    TargetChunkUsed = dataView.ChunkUsed,
                    PairDestructionShapeHandles = pairDestructionShapeHandles,
                    ChunkOverlapMasks = chunkOverlapMasks,
                    ChunkOverlapCounts = chunkOverlapCounts
                }.Schedule(destructionShapeHandles.Length, 1);

                JobHandle overlapPrefixHandle = new DestructionChunkOverlapPrefixSumJob
                {
                    ChunkOverlapCounts = chunkOverlapCounts,
                    ChunkOverlapOffsets = chunkOverlapOffsets,
                    TotalVoxelMaskBlockCount = totalVoxelMaskBlockCount
                }.Schedule(overlapHandle);

                overlapPrefixHandle.Complete();
                int commandCount = totalVoxelMaskBlockCount[0];
                if (commandCount == 0)
                {
                    return false;
                }

                commandShapeHandles = new NativeArray<int>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                commandChunkSlots = new NativeArray<byte>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                commandMasks = new NativeArray<byte>(
                    commandCount * BitPlaneBytesPerChunk,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                ShapeVoxelRemoveCommandBuffer commandBuffer = new ShapeVoxelRemoveCommandBuffer
                {
                    ShapeHandles = commandShapeHandles,
                    ChunkSlots = commandChunkSlots,
                    Masks = commandMasks,
                    CommandCount = commandCount,
                    CommandCapacity = commandCount
                };

                JobHandle maskGenerationHandle = DestructionMaskGenerationJob.Create(
                    pairDestructionShapeHandles,
                    targetShapeHandles,
                    chunkOverlapMasks,
                    chunkOverlapOffsets,
                    destructionShapeLocalToWorlds,
                    targetShapeLocalToWorlds,
                    destructionMasks,
                    destructionChunkPositions,
                    dataView.IsOccupied,
                    dataView.ChunkPositions,
                    commandBuffer).Schedule(chunkOverlapCount, ChunksPerShape);

                commandKeys = new NativeArray<ShapeVoxelRemoveCommandKey>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                JobHandle keyHandle = ShapeVoxelRemoveBuildKeyJob.Create(
                    commandBuffer,
                    commandKeys).Schedule(commandCount, 64, maskGenerationHandle);
                keyHandle.Complete();

                commandKeys.Sort();

                affectedShapeHandleScratch = new NativeArray<int>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                shapeRanges = new NativeArray<ShapeVoxelRemoveCommandRange>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                uniqueShapeCount = new NativeArray<int>(
                    1,
                    Allocator.TempJob,
                    NativeArrayOptions.ClearMemory);

                new ShapeVoxelRemoveBuildRangeJob
                {
                    SortedKeys = commandKeys,
                    AffectedShapeHandles = affectedShapeHandleScratch,
                    ShapeRanges = shapeRanges,
                    UniqueShapeCount = uniqueShapeCount,
                    KeyCount = commandCount
                }.Schedule().Complete();

                int affectedShapeCount = uniqueShapeCount[0];
                if (affectedShapeCount == 0)
                {
                    return false;
                }

                affectedShapeHandles = new NativeArray<int>(
                    affectedShapeCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                NativeArray<int>.Copy(
                    affectedShapeHandleScratch,
                    affectedShapeHandles,
                    affectedShapeCount);

                removeMasks = new NativeArray<byte>(
                    affectedShapeCount * BitPlaneBytesPerShape,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                ShapeVoxelRemovePackJob.Create(
                    commandBuffer,
                    commandKeys,
                    shapeRanges,
                    removeMasks).Schedule(affectedShapeCount, 1).Complete();

                if (HasNoRemovedVoxel(removeMasks))
                {
                    DisposeIfCreated(removeMasks);
                    DisposeIfCreated(affectedShapeHandles);
                    removeMasks = default;
                    affectedShapeHandles = default;
                    return false;
                }

                return true;
            }
            finally
            {
                DisposeIfCreated(destructionShapeHandles);
                DisposeIfCreated(destructionShapeLocalToWorlds);
                DisposeIfCreated(destructionMasks);
                DisposeIfCreated(destructionChunkPositions);
                DisposeIfCreated(destructionChunkUsed);
                DisposeIfCreated(targetShapeHandles);
                DisposeIfCreated(targetShapeRangeOffsets);
                DisposeIfCreated(targetShapeLocalToWorlds);
                DisposeIfCreated(pairDestructionShapeHandles);
                DisposeIfCreated(chunkOverlapMasks);
                DisposeIfCreated(chunkOverlapCounts);
                DisposeIfCreated(chunkOverlapOffsets);
                DisposeIfCreated(totalVoxelMaskBlockCount);
                DisposeIfCreated(commandShapeHandles);
                DisposeIfCreated(commandChunkSlots);
                DisposeIfCreated(commandMasks);
                DisposeIfCreated(commandKeys);
                DisposeIfCreated(affectedShapeHandleScratch);
                DisposeIfCreated(shapeRanges);
                DisposeIfCreated(uniqueShapeCount);
            }
        }

        private NativeArray<int> CreateTargetShapeHandles(
            Bounds destructionWorldBounds,
            ShapeDataView dataView,
            Matrix4x4 shapeVoxelToWorldMatrix)
        {
            List<int> targets = new List<int>(_liveShapeHandles.Count);

            for (int i = 0; i < _liveShapeHandles.Count; i++)
            {
                int shapeHandle = _liveShapeHandles[i];
                ShapeMetadata shape = dataView.Shapes[shapeHandle];
                if (!shape.Used)
                {
                    continue;
                }

                if (!TryGetShapeWorldBounds(
                        shapeHandle,
                        dataView,
                        shapeVoxelToWorldMatrix,
                        out Bounds shapeWorldBounds) ||
                    !shapeWorldBounds.Intersects(destructionWorldBounds))
                {
                    continue;
                }

                targets.Add(shapeHandle);
            }

            NativeArray<int> result = new NativeArray<int>(
                targets.Count,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < targets.Count; i++)
            {
                result[i] = targets[i];
            }

            return result;
        }

        private bool TryCreateSphereDestructionShape(
            int3 hitVoxel,
            out Bounds worldBounds,
            out NativeArray<int> destructionShapeHandles,
            out NativeArray<float4x4> destructionShapeLocalToWorlds,
            out NativeArray<byte> destructionMasks,
            out NativeArray<int3> destructionChunkPositions,
            out NativeArray<byte> destructionChunkUsed)
        {
            worldBounds = default;
            destructionShapeHandles = default;
            destructionShapeLocalToWorlds = default;
            destructionMasks = default;
            destructionChunkPositions = default;
            destructionChunkUsed = default;

            int maskDimension = CreateDestructionMaskDimension(_destructionRadiusInVoxels);
            int3 maskDimensions = new int3(maskDimension, maskDimension, maskDimension);
            int chunksX = (maskDimensions.x + ChunkSize - 1) / ChunkSize;
            int chunksY = (maskDimensions.y + ChunkSize - 1) / ChunkSize;
            int chunksZ = (maskDimensions.z + ChunkSize - 1) / ChunkSize;
            int destructionChunkCount = chunksX * chunksY * chunksZ;
            if (destructionChunkCount > ChunksPerShape)
            {
                Debug.LogWarning(
                    "Shape debugger destruction radius is too large for one 8-chunk destruction shape.",
                    this);
                return false;
            }

            float3 maskCenter = new float3(maskDimensions) * 0.5f;
            Matrix4x4 shapeVoxelToWorld = CreateShapeVoxelToWorldMatrix();
            Vector3 worldCenter = shapeVoxelToWorld.MultiplyPoint(new Vector3(
                hitVoxel.x + 0.5f,
                hitVoxel.y + 0.5f,
                hitVoxel.z + 0.5f));
            Matrix4x4 destructionLocalToWorld =
                Matrix4x4.TRS(worldCenter, transform.rotation, Vector3.one * _voxelSize) *
                Matrix4x4.Translate(-(Vector3)maskCenter);

            worldBounds = TransformLocalAabbToWorldBounds(
                destructionLocalToWorld,
                Vector3.zero,
                new Vector3(maskDimensions.x, maskDimensions.y, maskDimensions.z));

            destructionShapeHandles = new NativeArray<int>(
                1,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            destructionShapeHandles[0] = 0;
            destructionShapeLocalToWorlds = new NativeArray<float4x4>(
                1,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            destructionShapeLocalToWorlds[0] = ToFloat4x4(destructionLocalToWorld);
            destructionMasks = new NativeArray<byte>(
                ChunksPerShape * BitPlaneBytesPerChunk,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            destructionChunkPositions = new NativeArray<int3>(
                ChunksPerShape,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            destructionChunkUsed = new NativeArray<byte>(
                ChunksPerShape,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            WriteDestructionChunkSlots(
                chunksX,
                chunksY,
                chunksZ,
                destructionChunkPositions,
                destructionChunkUsed);
            WriteSphereDestructionMask(
                maskDimensions,
                chunksX,
                chunksY,
                destructionMasks);
            return true;
        }

        private static void WriteDestructionChunkSlots(
            int chunksX,
            int chunksY,
            int chunksZ,
            NativeArray<int3> destructionChunkPositions,
            NativeArray<byte> destructionChunkUsed)
        {
            int chunkSlot = 0;
            for (int z = 0; z < chunksZ; z++)
            {
                for (int y = 0; y < chunksY; y++)
                {
                    for (int x = 0; x < chunksX; x++)
                    {
                        destructionChunkPositions[chunkSlot] = new int3(x, y, z);
                        destructionChunkUsed[chunkSlot] = 1;
                        chunkSlot++;
                    }
                }
            }
        }

        private void WriteSphereDestructionMask(
            int3 maskDimensions,
            int chunksX,
            int chunksY,
            NativeArray<byte> destructionMasks)
        {
            float radius = Mathf.Max(0.0f, _destructionRadiusInVoxels);
            float radiusSquared = radius * radius;
            float3 center = new float3(maskDimensions) * 0.5f;

            for (int z = 0; z < maskDimensions.z; z++)
            {
                for (int y = 0; y < maskDimensions.y; y++)
                {
                    for (int x = 0; x < maskDimensions.x; x++)
                    {
                        float3 voxelCenter = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
                        if (math.lengthsq(voxelCenter - center) > radiusSquared)
                        {
                            continue;
                        }

                        int chunkX = x / ChunkSize;
                        int chunkY = y / ChunkSize;
                        int chunkZ = z / ChunkSize;
                        int chunkSlot = (chunkZ * chunksY + chunkY) * chunksX + chunkX;
                        int rowIndex = (y & (ChunkSize - 1)) + ChunkSize * (z & (ChunkSize - 1));
                        int maskIndex = chunkSlot * BitPlaneBytesPerChunk + rowIndex;
                        destructionMasks[maskIndex] =
                            (byte)(destructionMasks[maskIndex] | (1 << (x & (ChunkSize - 1))));
                    }
                }
            }
        }

        private static bool HasNoRemovedVoxel(NativeArray<byte> removeMasks)
        {
            for (int i = 0; i < removeMasks.Length; i++)
            {
                if (removeMasks[i] != 0xFF)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetShapeWorldBounds(
            int shapeHandle,
            ShapeDataView dataView,
            Matrix4x4 shapeVoxelToWorld,
            out Bounds bounds)
        {
            if (!TryGetShapeVoxelBounds(shapeHandle, dataView, out int3 minVoxel, out int3 maxVoxel))
            {
                bounds = default;
                return false;
            }

            Vector3 min = new Vector3(minVoxel.x, minVoxel.y, minVoxel.z);
            Vector3 max = new Vector3(maxVoxel.x, maxVoxel.y, maxVoxel.z);
            bounds = TransformLocalAabbToWorldBounds(shapeVoxelToWorld, min, max);
            return true;
        }

        private static bool TryGetShapeVoxelBounds(
            int shapeHandle,
            ShapeDataView dataView,
            out int3 minVoxel,
            out int3 maxVoxel)
        {
            if (!TryGetShapeChunkBounds(shapeHandle, dataView, out int3 minChunk, out int3 maxChunk))
            {
                minVoxel = default;
                maxVoxel = default;
                return false;
            }

            minVoxel = minChunk * ChunkSize;
            maxVoxel = (maxChunk + new int3(1, 1, 1)) * ChunkSize;
            return true;
        }

        private static bool TryGetShapeChunkBounds(
            int shapeHandle,
            ShapeDataView dataView,
            out int3 minChunk,
            out int3 maxChunk)
        {
            int chunkBase = shapeHandle * ChunksPerShape;
            minChunk = default;
            maxChunk = default;
            bool found = false;

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkSlotIndex = chunkBase + chunkSlot;
                if (dataView.ChunkUsed[chunkSlotIndex] == 0)
                {
                    continue;
                }

                int3 chunkPosition = dataView.ChunkPositions[chunkSlotIndex];
                if (!found)
                {
                    minChunk = chunkPosition;
                    maxChunk = chunkPosition;
                    found = true;
                }
                else
                {
                    minChunk = math.min(minChunk, chunkPosition);
                    maxChunk = math.max(maxChunk, chunkPosition);
                }
            }

            return found;
        }

        private Matrix4x4 CreateShapeVoxelToWorldMatrix()
        {
            return transform.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * _voxelSize);
        }

        private static Bounds TransformLocalAabbToWorldBounds(
            Matrix4x4 localToWorld,
            Vector3 localMin,
            Vector3 localMax)
        {
            Vector3 first = localToWorld.MultiplyPoint3x4(localMin);
            Bounds bounds = new Bounds(first, Vector3.zero);
            EncapsulateAabbCorner(ref bounds, localToWorld, new Vector3(localMax.x, localMin.y, localMin.z));
            EncapsulateAabbCorner(ref bounds, localToWorld, new Vector3(localMin.x, localMax.y, localMin.z));
            EncapsulateAabbCorner(ref bounds, localToWorld, new Vector3(localMax.x, localMax.y, localMin.z));
            EncapsulateAabbCorner(ref bounds, localToWorld, new Vector3(localMin.x, localMin.y, localMax.z));
            EncapsulateAabbCorner(ref bounds, localToWorld, new Vector3(localMax.x, localMin.y, localMax.z));
            EncapsulateAabbCorner(ref bounds, localToWorld, new Vector3(localMin.x, localMax.y, localMax.z));
            EncapsulateAabbCorner(ref bounds, localToWorld, localMax);
            return bounds;
        }

        private static void EncapsulateAabbCorner(
            ref Bounds bounds,
            Matrix4x4 localToWorld,
            Vector3 localCorner)
        {
            bounds.Encapsulate(localToWorld.MultiplyPoint3x4(localCorner));
        }

        private static int CreateDestructionMaskDimension(float radiusInVoxels)
        {
            int dimension = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.0f, radiusInVoxels) * 2.0f) + 3);
            return (dimension & 1) == 0 ? dimension + 1 : dimension;
        }

        private static float4x4 ToFloat4x4(Matrix4x4 matrix)
        {
            return new float4x4(
                new float4(matrix.m00, matrix.m10, matrix.m20, matrix.m30),
                new float4(matrix.m01, matrix.m11, matrix.m21, matrix.m31),
                new float4(matrix.m02, matrix.m12, matrix.m22, matrix.m32),
                new float4(matrix.m03, matrix.m13, matrix.m23, matrix.m33));
        }

        #endregion


        #region Commit

        private bool CommitBuiltShapes(
            NativeArray<int> affectedShapeHandles,
            NativeArray<int> localShapeCounts,
            NativeArray<int> builtShapeOffsets,
            NativeArray<BuiltChunkMetadata> builtChunks,
            NativeArray<byte> builtIsOccupied)
        {
            if (!CanCommitBuiltShapes(
                    affectedShapeHandles,
                    localShapeCounts,
                    builtShapeOffsets,
                    builtChunks))
            {
                return false;
            }

            ShapeDataView dataView = _storage.GetShapeDataView(affectedShapeHandles[0]);
            for (int shapeListIndex = 0; shapeListIndex < affectedShapeHandles.Length; shapeListIndex++)
            {
                int sourceShapeHandle = affectedShapeHandles[shapeListIndex];
                ShapeMetadata sourceMetadata = dataView.Shapes[sourceShapeHandle];
                int bodyHandle = sourceMetadata.BodyHandle;
                int sourceBuiltShapeCount = GetBuiltShapeCount(shapeListIndex, localShapeCounts);
                int nonEmptyBuiltShapeCount = GetNonEmptyBuiltShapeCount(
                    shapeListIndex,
                    localShapeCounts,
                    builtShapeOffsets,
                    builtChunks);

                if (nonEmptyBuiltShapeCount == 0)
                {
                    UnregisterLiveShape(sourceShapeHandle);
                    _storage.Release(sourceShapeHandle);
                    continue;
                }

                if (nonEmptyBuiltShapeCount == 1)
                {
                    int localShapeId = FindFirstNonEmptyBuiltShapeId(
                        shapeListIndex,
                        sourceBuiltShapeCount,
                        builtShapeOffsets,
                        builtChunks);
                    WriteBuiltShapeToHandle(
                        sourceShapeHandle,
                        bodyHandle,
                        shapeListIndex,
                        localShapeId,
                        builtShapeOffsets,
                        builtChunks,
                        builtIsOccupied);
                    RefreshLiveShape(sourceShapeHandle);
                    continue;
                }

                Color32 firstColor = GetShapeColor(sourceShapeHandle);
                UnregisterLiveShape(sourceShapeHandle);
                _storage.Release(sourceShapeHandle);

                for (int localShapeId = 0; localShapeId < sourceBuiltShapeCount; localShapeId++)
                {
                    if (!HasBuiltShapeChunks(
                            shapeListIndex,
                            localShapeId,
                            builtShapeOffsets,
                            builtChunks))
                    {
                        continue;
                    }

                    int newShapeHandle = _storage.Acquire(bodyHandle);
                    if (newShapeHandle == ShapeDataStorage.InvalidHandle)
                    {
                        Debug.LogWarning("Shape debugger ran out of shape handles during batch split commit.", this);
                        return true;
                    }

                    WriteBuiltShapeToHandle(
                        newShapeHandle,
                        bodyHandle,
                        shapeListIndex,
                        localShapeId,
                        builtShapeOffsets,
                        builtChunks,
                        builtIsOccupied);
                    RegisterLiveShape(
                        newShapeHandle,
                        localShapeId == 0 ? firstColor : AllocateColor());
                }
            }

            return true;
        }

        private bool CanCommitBuiltShapes(
            NativeArray<int> affectedShapeHandles,
            NativeArray<int> localShapeCounts,
            NativeArray<int> builtShapeOffsets,
            NativeArray<BuiltChunkMetadata> builtChunks)
        {
            int freedShapeSlots = 0;
            int requiredNewShapeSlots = 0;

            for (int shapeListIndex = 0; shapeListIndex < affectedShapeHandles.Length; shapeListIndex++)
            {
                int builtShapeCount = GetNonEmptyBuiltShapeCount(
                    shapeListIndex,
                    localShapeCounts,
                    builtShapeOffsets,
                    builtChunks);

                if (builtShapeCount == 0)
                {
                    freedShapeSlots++;
                }
                else if (builtShapeCount > 1)
                {
                    freedShapeSlots++;
                    requiredNewShapeSlots += builtShapeCount;
                }
            }

            if (_storage.FreeShapeSlotCount + freedShapeSlots >= requiredNewShapeSlots)
            {
                return true;
            }

            Debug.LogWarning(
                "Shape debugger cannot commit destruction batch: storage has insufficient free shape slots.",
                this);
            return false;
        }

        private int GetNonEmptyBuiltShapeCount(
            int shapeListIndex,
            NativeArray<int> localShapeCounts,
            NativeArray<int> builtShapeOffsets,
            NativeArray<BuiltChunkMetadata> builtChunks)
        {
            int nonEmptyShapeCount = 0;
            int builtShapeCount = GetBuiltShapeCount(shapeListIndex, localShapeCounts);

            for (int localShapeId = 0; localShapeId < builtShapeCount; localShapeId++)
            {
                if (HasBuiltShapeChunks(
                        shapeListIndex,
                        localShapeId,
                        builtShapeOffsets,
                        builtChunks))
                {
                    nonEmptyShapeCount++;
                }
            }

            return nonEmptyShapeCount;
        }

        private static int GetBuiltShapeCount(
            int shapeListIndex,
            NativeArray<int> localShapeCounts)
        {
            return math.clamp(
                localShapeCounts[shapeListIndex],
                0,
                ShapeBuildJob.MaxBuiltShapesPerSourceShape);
        }

        private int FindFirstNonEmptyBuiltShapeId(
            int shapeListIndex,
            int builtShapeCount,
            NativeArray<int> builtShapeOffsets,
            NativeArray<BuiltChunkMetadata> builtChunks)
        {
            for (int localShapeId = 0; localShapeId < builtShapeCount; localShapeId++)
            {
                if (HasBuiltShapeChunks(
                        shapeListIndex,
                        localShapeId,
                        builtShapeOffsets,
                        builtChunks))
                {
                    return localShapeId;
                }
            }

            return 0;
        }

        private bool HasBuiltShapeChunks(
            int shapeListIndex,
            int localShapeId,
            NativeArray<int> builtShapeOffsets,
            NativeArray<BuiltChunkMetadata> builtChunks)
        {
            for (int chunkSlot = 0; chunkSlot < ShapeBuildJob.MaxBuiltChunksPerShape; chunkSlot++)
            {
                if (builtChunks[BuiltChunkIndex(
                        shapeListIndex,
                        localShapeId,
                        chunkSlot,
                        builtShapeOffsets)].Used)
                {
                    return true;
                }
            }

            return false;
        }

        private void WriteBuiltShapeToHandle(
            int destinationShapeHandle,
            int bodyHandle,
            int shapeListIndex,
            int localShapeId,
            NativeArray<int> builtShapeOffsets,
            NativeArray<BuiltChunkMetadata> builtChunks,
            NativeArray<byte> builtIsOccupied)
        {
            ShapeDataView dataView = _storage.GetShapeDataView(destinationShapeHandle);
            ClearShapeForRewrite(destinationShapeHandle, bodyHandle, dataView);

            for (int chunkSlot = 0; chunkSlot < ShapeBuildJob.MaxBuiltChunksPerShape; chunkSlot++)
            {
                int builtChunkIndex = BuiltChunkIndex(
                    shapeListIndex,
                    localShapeId,
                    chunkSlot,
                    builtShapeOffsets);
                BuiltChunkMetadata builtChunk = builtChunks[builtChunkIndex];
                if (!builtChunk.Used)
                {
                    continue;
                }

                int destinationChunkMetadataIndex =
                    destinationShapeHandle * ChunksPerShape + chunkSlot;
                int sourceBase = builtChunkIndex * BitPlaneBytesPerChunk;
                NativeArray<byte>.Copy(
                    builtIsOccupied,
                    sourceBase,
                    dataView.IsOccupied,
                    destinationChunkMetadataIndex * BitPlaneBytesPerChunk,
                    BitPlaneBytesPerChunk);

                WriteChunkSlotData(
                    destinationShapeHandle,
                    bodyHandle,
                    chunkSlot,
                    builtChunk.ChunkPosition,
                    dataView);
            }
        }

        private void ClearShapeForRewrite(
            int shapeHandle,
            int bodyHandle,
            ShapeDataView dataView)
        {
            NativeArray<ShapeMetadata> shapes = dataView.Shapes;
            NativeArray<byte> isOccupied = dataView.IsOccupied;
            NativeArray<byte> isFace = dataView.IsFace;
            NativeArray<byte> isEdge = dataView.IsEdge;
            NativeArray<byte> isCorner = dataView.IsCorner;
            NativeArray<int3> chunkPositions = dataView.ChunkPositions;
            NativeArray<byte> chunkUsed = dataView.ChunkUsed;
            int chunkBase = shapeHandle * ChunksPerShape;
            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = chunkBase + chunkSlot;
                chunkPositions[chunkMetadataIndex] = default;
                chunkUsed[chunkMetadataIndex] = 0;
            }

            int bitPlaneBase = shapeHandle * BitPlaneBytesPerShape;
            for (int i = 0; i < BitPlaneBytesPerShape; i++)
            {
                isOccupied[bitPlaneBase + i] = 0;
                isFace[bitPlaneBase + i] = 0;
                isEdge[bitPlaneBase + i] = 0;
                isCorner[bitPlaneBase + i] = 0;
            }

            shapes[shapeHandle] = new ShapeMetadata
            {
                BodyHandle = bodyHandle,
                IsUsed = 1
            };
        }

        private void WriteChunkSlotData(
            int shapeHandle,
            int bodyHandle,
            int chunkSlot,
            int3 chunkPosition,
            ShapeDataView dataView)
        {
            NativeArray<ShapeMetadata> shapes = dataView.Shapes;
            NativeArray<int3> chunkPositions = dataView.ChunkPositions;
            NativeArray<byte> chunkUsed = dataView.ChunkUsed;
            int chunkMetadataIndex = shapeHandle * ChunksPerShape + chunkSlot;
            chunkPositions[chunkMetadataIndex] = chunkPosition;
            chunkUsed[chunkMetadataIndex] = 1;

            ShapeMetadata shape = shapes[shapeHandle];
            shape.BodyHandle = bodyHandle;
            shape.IsUsed = 1;
            shapes[shapeHandle] = shape;
        }

        private void RegisterLiveShape(int shapeHandle, Color32 color)
        {
            if (!_liveShapeHandles.Contains(shapeHandle))
            {
                _liveShapeHandles.Add(shapeHandle);
            }

            _shapeColors[shapeHandle] = color;
            _renderer.RegisterOrRefreshShape(shapeHandle, color);
        }

        private void RefreshLiveShape(int shapeHandle)
        {
            _renderer.RegisterOrRefreshShape(shapeHandle, GetShapeColor(shapeHandle));
        }

        private void UnregisterLiveShape(int shapeHandle)
        {
            _liveShapeHandles.Remove(shapeHandle);
            _shapeColors.Remove(shapeHandle);
            _renderer.UnregisterShape(shapeHandle);
        }

        private Color32 GetShapeColor(int shapeHandle)
        {
            return _shapeColors.TryGetValue(shapeHandle, out Color32 color)
                ? color
                : AllocateColor();
        }

        private Color32 AllocateColor()
        {
            float hue = math.frac(_nextColorIndex++ * 0.61803398875f);
            Color color = Color.HSVToRGB(hue, 0.68f, 1.0f);
            return color;
        }

        #endregion


        #region Picking

        private bool TryPickShape(Ray worldRay, out ShapeHit hit)
        {
            hit = default;
            float voxelSize = Mathf.Max(0.000001f, _voxelSize);
            float3 origin = (float3)transform.worldToLocalMatrix.MultiplyPoint(worldRay.origin) / voxelSize;
            float3 direction = (float3)transform.worldToLocalMatrix.MultiplyVector(worldRay.direction);
            if (math.lengthsq(direction) <= 0.000000000001f)
            {
                return false;
            }

            direction = math.normalize(direction);
            float bestDistance = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < _liveShapeHandles.Count; i++)
            {
                int shapeHandle = _liveShapeHandles[i];
                ShapeDataView dataView = _storage.GetShapeDataView(shapeHandle);
                ShapeMetadata shape = dataView.Shapes[shapeHandle];
                if (!shape.Used)
                {
                    continue;
                }

                if (!TryGetShapeVoxelBounds(shapeHandle, dataView, out int3 minVoxel, out int3 maxVoxel))
                {
                    continue;
                }

                float3 min = new float3(minVoxel.x, minVoxel.y, minVoxel.z);
                float3 max = new float3(maxVoxel.x, maxVoxel.y, maxVoxel.z);
                if (!TryIntersectAabb(
                        origin,
                        direction,
                        min,
                        max,
                        out float enter,
                        out float exit))
                {
                    continue;
                }

                float startDistance = math.max(0.0f, enter + 0.0001f);
                float endDistance = math.min(
                    exit,
                    math.min(_maxDdaDistanceInVoxels, _maxRayDistance / voxelSize));
                if (endDistance < startDistance)
                {
                    continue;
                }

                float3 ddaOrigin = origin + direction * startDistance;
                if (!TryDdaFirstOccupiedVoxel(
                        shapeHandle,
                        dataView,
                        ddaOrigin,
                        direction,
                        FloorToInt3(min),
                        FloorToInt3(max),
                        _maxDdaSteps,
                        endDistance - startDistance,
                        out int3 hitVoxel,
                        out float travelledDistance))
                {
                    continue;
                }

                float totalDistance = startDistance + travelledDistance;
                if (totalDistance >= bestDistance)
                {
                    continue;
                }

                bestDistance = totalDistance;
                hit = new ShapeHit(shapeHandle, hitVoxel);
                found = true;
            }

            return found;
        }

        private bool TryDdaFirstOccupiedVoxel(
            int shapeHandle,
            ShapeDataView dataView,
            float3 origin,
            float3 direction,
            int3 min,
            int3 maxExclusive,
            int maxSteps,
            float maxDistance,
            out int3 hitVoxel,
            out float travelledDistance)
        {
            int3 voxel = FloorToInt3(origin);
            int3 step = new int3(
                DirectionStep(direction.x),
                DirectionStep(direction.y),
                DirectionStep(direction.z));
            float3 tMax = new float3(
                InitialBoundaryDistance(origin.x, voxel.x, direction.x),
                InitialBoundaryDistance(origin.y, voxel.y, direction.y),
                InitialBoundaryDistance(origin.z, voxel.z, direction.z));
            float3 tDelta = new float3(
                StepDistance(direction.x),
                StepDistance(direction.y),
                StepDistance(direction.z));

            travelledDistance = 0.0f;
            int sanitizedMaxSteps = math.max(1, maxSteps);
            float sanitizedMaxDistance = math.max(0.0f, maxDistance);

            for (int stepIndex = 0;
                 stepIndex < sanitizedMaxSteps && travelledDistance <= sanitizedMaxDistance;
                 stepIndex++)
            {
                if (math.any(voxel < min) || math.any(voxel >= maxExclusive))
                {
                    break;
                }

                if (IsVoxelOccupied(shapeHandle, dataView, voxel))
                {
                    hitVoxel = voxel;
                    return true;
                }

                if (tMax.x <= tMax.y && tMax.x <= tMax.z)
                {
                    voxel.x += step.x;
                    travelledDistance = tMax.x;
                    tMax.x += tDelta.x;
                }
                else if (tMax.y <= tMax.z)
                {
                    voxel.y += step.y;
                    travelledDistance = tMax.y;
                    tMax.y += tDelta.y;
                }
                else
                {
                    voxel.z += step.z;
                    travelledDistance = tMax.z;
                    tMax.z += tDelta.z;
                }
            }

            hitVoxel = default;
            return false;
        }

        private static bool TryIntersectAabb(
            float3 origin,
            float3 direction,
            float3 min,
            float3 max,
            out float enter,
            out float exit)
        {
            enter = 0.0f;
            exit = float.PositiveInfinity;

            if (!IntersectSlab(origin.x, direction.x, min.x, max.x, ref enter, ref exit) ||
                !IntersectSlab(origin.y, direction.y, min.y, max.y, ref enter, ref exit) ||
                !IntersectSlab(origin.z, direction.z, min.z, max.z, ref enter, ref exit))
            {
                return false;
            }

            return exit >= math.max(0.0f, enter);
        }

        private static bool IntersectSlab(
            float origin,
            float direction,
            float min,
            float max,
            ref float enter,
            ref float exit)
        {
            if (math.abs(direction) <= 0.0000001f)
            {
                return origin >= min && origin <= max;
            }

            float invDirection = 1.0f / direction;
            float t0 = (min - origin) * invDirection;
            float t1 = (max - origin) * invDirection;
            if (t0 > t1)
            {
                float temp = t0;
                t0 = t1;
                t1 = temp;
            }

            enter = math.max(enter, t0);
            exit = math.min(exit, t1);
            return enter <= exit;
        }

        private bool IsVoxelOccupied(
            int shapeHandle,
            ShapeDataView dataView,
            int3 voxel)
        {
            int3 chunkPosition = new int3(
                FloorDiv(voxel.x, ChunkSize),
                FloorDiv(voxel.y, ChunkSize),
                FloorDiv(voxel.z, ChunkSize));
            if (!TryFindChunkSlotIndex(shapeHandle, dataView, chunkPosition, out int chunkMetadataIndex))
            {
                return false;
            }

            int localX = FloorMod(voxel.x, ChunkSize);
            int localY = FloorMod(voxel.y, ChunkSize);
            int localZ = FloorMod(voxel.z, ChunkSize);
            byte row = dataView.IsOccupied[chunkMetadataIndex * BitPlaneBytesPerChunk + RowIndex(localY, localZ)];
            return (row & (1 << localX)) != 0;
        }

        private static bool TryFindChunkSlotIndex(
            int shapeHandle,
            ShapeDataView dataView,
            int3 chunkPosition,
            out int chunkSlotIndex)
        {
            int chunkBase = shapeHandle * ChunksPerShape;
            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int candidateIndex = chunkBase + chunkSlot;
                if (dataView.ChunkUsed[candidateIndex] != 0 &&
                    math.all(dataView.ChunkPositions[candidateIndex] == chunkPosition))
                {
                    chunkSlotIndex = candidateIndex;
                    return true;
                }
            }

            chunkSlotIndex = -1;
            return false;
        }

        #endregion


        #region Input

        private bool WasPrimaryClickPressed()
        {
            try
            {
                if (Input.GetMouseButtonDown(0))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }

            return TryReadInputSystemLeftButton(out bool pressed) && pressed;
        }

        private Vector3 GetPointerScreenPosition(Camera rayCamera)
        {
            try
            {
                return Input.mousePosition;
            }
            catch (InvalidOperationException)
            {
            }

            if (TryReadInputSystemPointerPosition(out Vector2 position))
            {
                return new Vector3(position.x, position.y, 0.0f);
            }

            return new Vector3(rayCamera.pixelWidth * 0.5f, rayCamera.pixelHeight * 0.5f, 0.0f);
        }

        private static bool TryReadInputSystemLeftButton(out bool pressed)
        {
            pressed = false;
            object mouse = GetInputSystemMouse();
            if (mouse == null)
            {
                return false;
            }

            object leftButton = mouse.GetType().GetProperty("leftButton")?.GetValue(mouse);
            object value = leftButton?.GetType().GetProperty("wasPressedThisFrame")?.GetValue(leftButton);
            if (value is bool boolValue)
            {
                pressed = boolValue;
                return true;
            }

            return false;
        }

        private static bool TryReadInputSystemPointerPosition(out Vector2 position)
        {
            position = default;
            object mouse = GetInputSystemMouse();
            if (mouse == null)
            {
                return false;
            }

            object positionControl = mouse.GetType().GetProperty("position")?.GetValue(mouse);
            MethodInfo readValueMethod = positionControl?.GetType().GetMethod("ReadValue", Type.EmptyTypes);
            object value = readValueMethod?.Invoke(positionControl, null);
            if (value is Vector2 vector)
            {
                position = vector;
                return true;
            }

            return false;
        }

        private static object GetInputSystemMouse()
        {
            Type mouseType = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
            return mouseType?.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        #endregion


        #region Bit Helpers

        private static void WriteFullChunk(NativeArray<byte> isOccupied, int chunkMetadataIndex)
        {
            int chunkBase = chunkMetadataIndex * BitPlaneBytesPerChunk;
            for (int row = 0; row < BitPlaneBytesPerChunk; row++)
            {
                isOccupied[chunkBase + row] = 0xFF;
            }
        }

        private static int BuiltShapeIndex(
            int shapeListIndex,
            int localShapeId,
            NativeArray<int> builtShapeOffsets)
        {
            return builtShapeOffsets[shapeListIndex] + localShapeId;
        }

        private static int BuiltChunkIndex(
            int shapeListIndex,
            int localShapeId,
            int chunkSlot,
            NativeArray<int> builtShapeOffsets)
        {
            return BuiltShapeIndex(shapeListIndex, localShapeId, builtShapeOffsets) *
                   ShapeBuildJob.MaxBuiltChunksPerShape +
                   chunkSlot;
        }

        private static int RowIndex(int y, int z)
        {
            return y + ChunkSize * z;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static int FloorMod(int value, int divisor)
        {
            return value - FloorDiv(value, divisor) * divisor;
        }

        #endregion


        #region General Helpers

        private Camera ResolveCamera()
        {
            if (_camera != null)
            {
                return _camera;
            }

            _camera = GetComponent<Camera>();
            if (_camera != null)
            {
                return _camera;
            }

            _camera = Camera.main;
            return _camera;
        }

        private void SanitizeSerializedState()
        {
            _shapeCapacity = math.max(1, _shapeCapacity);
            _voxelSize = Mathf.Max(0.000001f, float.IsFinite(_voxelSize) ? _voxelSize : 1.0f);
            _maxRayDistance = Mathf.Max(0.0f, float.IsFinite(_maxRayDistance) ? _maxRayDistance : 200.0f);
            _maxDdaDistanceInVoxels = Mathf.Max(
                0.0f,
                float.IsFinite(_maxDdaDistanceInVoxels) ? _maxDdaDistanceInVoxels : 512.0f);
            _maxDdaSteps = math.max(1, _maxDdaSteps);
            _destructionRadiusInVoxels = Mathf.Max(
                0.0f,
                float.IsFinite(_destructionRadiusInVoxels) ? _destructionRadiusInVoxels : 2.0f);
            _minimalVoxelNumber = math.max(2, _minimalVoxelNumber);
            _initialShapeChunkDimensions = SanitizeInitialChunkDimensions(_initialShapeChunkDimensions);
        }

        private static int3 FloorToInt3(float3 value)
        {
            return new int3(
                (int)math.floor(value.x),
                (int)math.floor(value.y),
                (int)math.floor(value.z));
        }

        private static int DirectionStep(float value)
        {
            if (value > 0.0f)
            {
                return 1;
            }

            return value < 0.0f ? -1 : 0;
        }

        private static float InitialBoundaryDistance(float origin, int voxel, float direction)
        {
            if (direction > 0.0f)
            {
                return ((voxel + 1.0f) - origin) / direction;
            }

            if (direction < 0.0f)
            {
                return (origin - voxel) / -direction;
            }

            return float.PositiveInfinity;
        }

        private static float StepDistance(float direction)
        {
            return direction == 0.0f ? float.PositiveInfinity : math.abs(1.0f / direction);
        }

        private static void DisposeIfCreated<T>(NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        #endregion


        private readonly struct ShapeHit
        {
            public ShapeHit(int shapeHandle, int3 voxel)
            {
                ShapeHandle = shapeHandle;
                Voxel = voxel;
            }

            public int ShapeHandle { get; }
            public int3 Voxel { get; }
        }
    }
}
