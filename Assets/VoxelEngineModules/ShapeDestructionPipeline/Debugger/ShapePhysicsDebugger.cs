using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VoxelEngineModules.Shape.Debugger
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShapeDebugRenderer))]
    [AddComponentMenu("VoxelEngine/Shape/Shape Physics Debugger")]
    public sealed class ShapePhysicsDebugger : MonoBehaviour
    {
        private const int ChunkSize = ShapeDataContainer.ChunkSize;
        private const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        private const int BitPlaneBytesPerChunk = ShapeDataContainer.BitPlaneBytesPerChunk;
        private const int BitPlaneBytesPerShape = ShapeDataContainer.BitPlaneBytesPerShape;

        [SerializeField] private Camera _camera;
        [SerializeField] private GameObject _physicsRunnerObject;
        [SerializeField] private int _shapeCapacity = 64;
        [SerializeField] private Vector3Int _floorChunkDimensions = new Vector3Int(12, 1, 12);
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
        [SerializeField] private float _dynamicShapeMass = 48.0f;
        [SerializeField] private float _dynamicLinearDamping;
        [SerializeField] private float _dynamicAngularDamping = 0.05f;
        [SerializeField] private float _dynamicGravityScale = 1.0f;

        private readonly List<ShapePhysicsEntry> _entries = new List<ShapePhysicsEntry>();
        private readonly Dictionary<int, ShapePhysicsEntry> _entriesByShapeHandle =
            new Dictionary<int, ShapePhysicsEntry>();
        private readonly ShapePhysicsBridge _physicsBridge = new ShapePhysicsBridge();

        private ShapeDataStorage _storage;
        private ShapeDebugRenderer _renderer;
        private object _physicsRunner;
        private DragState _drag;
        private int _nextFallbackBodyHandle = 1;
        private int _nextColorIndex;
        private string _lastWarningKey;

        public ShapeDataStorage Storage => _storage;

        public void Configure(
            Camera debugCamera,
            GameObject physicsRunnerObject,
            int shapeCapacity,
            float destructionRadiusInVoxels,
            int minimalVoxelNumber,
            float voxelSize)
        {
            _camera = debugCamera;
            _physicsRunnerObject = physicsRunnerObject;
            _shapeCapacity = math.max(1, shapeCapacity);
            _destructionRadiusInVoxels = math.max(0.0f, destructionRadiusInVoxels);
            _minimalVoxelNumber = math.max(2, minimalVoxelNumber);
            _voxelSize = Mathf.Max(0.000001f, float.IsFinite(voxelSize) ? voxelSize : 1.0f);
            SanitizeSerializedState();
        }

        private void Awake()
        {
            _renderer = GetComponent<ShapeDebugRenderer>();
            SanitizeSerializedState();
            ResolveGlobalVoxelSize();
            EnsureStorage();
            EnsurePhysicsRunner();
        }

        private void Start()
        {
            if (!_initializeOnStart)
            {
                return;
            }

            EnsureInitialShapes();
            if (_syncRendererImmediately)
            {
                _renderer.SyncRenderBackend();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Camera rayCamera = ResolveCamera();
            if (rayCamera == null)
            {
                return;
            }

            if (WasPrimaryClickPressed())
            {
                TryStrikeDynamicShape(rayCamera);
            }

            if (WasSecondaryClickPressed())
            {
                BeginDrag(rayCamera);
            }

            if (_drag.Active && IsSecondaryClickHeld())
            {
                UpdateDrag(rayCamera);
            }

            if (_drag.Active && WasSecondaryClickReleased())
            {
                EndDrag();
            }
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EnsurePhysicsRunner();
            _physicsBridge.TickRunner(_physicsRunner, Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            if (_renderer == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                ShapePhysicsEntry entry = _entries[i];
                if (entry.Host != null)
                {
                    _renderer.UpdateShapeTransform(entry.ShapeHandle, entry.Host.transform.localToWorldMatrix);
                }
            }

            if (_syncRendererImmediately)
            {
                _renderer.SyncRenderBackend();
            }
        }

        private void OnDestroy()
        {
            if (_renderer != null)
            {
                _renderer.Clear();
            }

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                DestroyHost(_entries[i].Host);
            }

            _entries.Clear();
            _entriesByShapeHandle.Clear();

            if (_storage != null)
            {
                _storage.Dispose();
                _storage = null;
            }
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

        private void EnsurePhysicsRunner()
        {
            if (_physicsRunner != null)
            {
                return;
            }

            if (_physicsRunnerObject == null)
            {
                _physicsRunnerObject = GameObject.Find("Mini Physics Runner");
                if (_physicsRunnerObject == null)
                {
                    _physicsRunnerObject = new GameObject("Mini Physics Runner");
                }
            }

            if (!_physicsBridge.TryCreateRunner(_physicsRunnerObject, out _physicsRunner, out string error))
            {
                LogWarningOnce($"Shape physics debugger could not initialize mini physics runner: {error}");
            }
        }

        private void EnsureInitialShapes()
        {
            if (_entries.Count > 0)
            {
                return;
            }

            CreateStaticFloorShapes();

            CreateFullChunkShape(
                "Dynamic Voxel Cube",
                CreateCubeChunkPositions(),
                worldPosition: new Vector3(-8.0f, 22.0f, -8.0f) * _voxelSize,
                worldRotation: Quaternion.identity,
                bodyKind: ShapePhysicsBodyKind.Dynamic,
                destructible: true,
                initialVelocity: Vector3.zero,
                inheritAngularVelocity: Vector3.zero,
                color: AllocateColor());
        }

        private void CreateStaticFloorShapes()
        {
            Vector3Int dimensions = SanitizeFloorChunkDimensions(_floorChunkDimensions);
            Vector3 floorOrigin = new Vector3(
                dimensions.x * ChunkSize * -0.5f,
                -ChunkSize,
                dimensions.z * ChunkSize * -0.5f) * _voxelSize;
            int floorShapeIndex = 0;

            for (int y = 0; y < dimensions.y;)
            {
                int remainingY = dimensions.y - y;
                int blockY = math.min(remainingY, ChunksPerShape);

                for (int z = 0; z < dimensions.z;)
                {
                    int remainingZ = dimensions.z - z;
                    int blockZ = math.min(remainingZ, math.max(1, ChunksPerShape / blockY));

                    for (int x = 0; x < dimensions.x;)
                    {
                        int remainingX = dimensions.x - x;
                        int blockX = math.min(remainingX, math.max(1, ChunksPerShape / (blockY * blockZ)));
                        int3[] chunkPositions = CreateChunkBlockPositions(blockX, blockY, blockZ);
                        Vector3 worldPosition = floorOrigin + new Vector3(
                            x * ChunkSize,
                            y * ChunkSize,
                            z * ChunkSize) * _voxelSize;

                        CreateFullChunkShape(
                            $"Static Voxel Floor {++floorShapeIndex}",
                            chunkPositions,
                            worldPosition,
                            Quaternion.identity,
                            ShapePhysicsBodyKind.Static,
                            destructible: false,
                            initialVelocity: Vector3.zero,
                            inheritAngularVelocity: Vector3.zero,
                            color: new Color32(86, 115, 132, 255));

                        x += blockX;
                    }

                    z += blockZ;
                }

                y += blockY;
            }
        }

        private static int3[] CreateChunkBlockPositions(
            int blockX,
            int blockY,
            int blockZ)
        {
            int chunkCount = math.min(ChunksPerShape, math.max(1, blockX * blockY * blockZ));
            int3[] positions = new int3[chunkCount];
            int index = 0;
            for (int z = 0; z < blockZ; z++)
            {
                for (int y = 0; y < blockY; y++)
                {
                    for (int x = 0; x < blockX; x++)
                    {
                        if (index >= positions.Length)
                        {
                            return positions;
                        }

                        positions[index++] = new int3(x, y, z);
                    }
                }
            }

            return positions;
        }

        private static int3[] CreateCubeChunkPositions()
        {
            int3[] positions = new int3[ChunksPerShape];
            int index = 0;
            for (int z = 0; z < 2; z++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        positions[index++] = new int3(x, y, z);
                    }
                }
            }

            return positions;
        }

        private ShapePhysicsEntry CreateFullChunkShape(
            string name,
            int3[] chunkPositions,
            Vector3 worldPosition,
            Quaternion worldRotation,
            ShapePhysicsBodyKind bodyKind,
            bool destructible,
            Vector3 initialVelocity,
            Vector3 inheritAngularVelocity,
            Color32 color)
        {
            EnsureStorage();

            GameObject host = CreatePhysicsHost(name, worldPosition, worldRotation);
            if (!_physicsBridge.TryCreatePhysicsComponents(host, out ShapePhysicsComponents components, out string error))
            {
                LogWarningOnce($"Shape physics debugger could not create physics components: {error}");
                DestroyHost(host);
                return null;
            }

            int bodyHandle = _physicsBridge.ReadBodyHandleValue(components.Body, _nextFallbackBodyHandle++);
            int shapeHandle = _storage.Acquire(bodyHandle);
            if (shapeHandle == ShapeDataStorage.InvalidHandle)
            {
                LogWarningOnce("Shape physics debugger storage has no free slot for an initial shape.");
                DestroyHost(host);
                return null;
            }

            WriteFullChunkShape(shapeHandle, bodyHandle, chunkPositions);

            ShapePhysicsEntry entry = new ShapePhysicsEntry(
                shapeHandle,
                host,
                components,
                color,
                destructible,
                bodyKind == ShapePhysicsBodyKind.Dynamic);
            RegisterEntry(entry);

            ShapePhysicsBodyState state = CreateBodyState(bodyKind, initialVelocity, inheritAngularVelocity);
            RefreshPhysicsForEntry(entry, state);
            RefreshRendererForEntry(entry);
            return entry;
        }

        private GameObject CreatePhysicsHost(
            string name,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(transform, worldPositionStays: true);
            host.transform.SetPositionAndRotation(worldPosition, NormalizeRotation(worldRotation));
            host.transform.localScale = Vector3.one;
            return host;
        }

        private ShapePhysicsBodyState CreateBodyState(
            ShapePhysicsBodyKind bodyKind,
            Vector3 linearVelocity,
            Vector3 angularVelocity)
        {
            return new ShapePhysicsBodyState
            {
                BodyKind = bodyKind,
                Mass = bodyKind == ShapePhysicsBodyKind.Dynamic ? _dynamicShapeMass : 1.0f,
                LocalCenterOfMass = Vector3.zero,
                InertiaSize = Vector3.one,
                UseGravity = bodyKind == ShapePhysicsBodyKind.Dynamic,
                GravityScale = _dynamicGravityScale,
                LinearDamping = _dynamicLinearDamping,
                AngularDamping = _dynamicAngularDamping,
                LinearVelocity = linearVelocity,
                AngularVelocity = angularVelocity
            };
        }

        #endregion


        #region Destruction Pipeline

        private bool TryStrikeDynamicShape(Camera rayCamera)
        {
            Ray ray = _useCameraCenter
                ? rayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f))
                : rayCamera.ScreenPointToRay(GetPointerScreenPosition(rayCamera));
            if (!TryPickShape(ray, requireDynamic: true, out ShapePhysicsHit hit) ||
                hit.Entry == null ||
                !hit.Entry.Destructible)
            {
                return false;
            }

            bool changed = RunDestructionPipeline(hit.WorldPoint);
            if (changed && _syncRendererImmediately)
            {
                _renderer.SyncRenderBackend();
            }

            return changed;
        }

        private bool RunDestructionPipeline(Vector3 hitWorldPoint)
        {
            if (!TryBuildDestructionInputs(
                    out NativeArray<byte> removeMasks,
                    out NativeArray<int> affectedShapeHandles,
                    out long pipelineStartTimestamp,
                    out ShapeDestructionPipelineTimings pipelineTimings,
                    hitWorldPoint))
            {
                return false;
            }

            ShapeDataView dataView = _storage.GetShapeDataView(affectedShapeHandles[0]);
            ShapeDestructionUpdatePipelineOutput updateOutput = default;

            try
            {
                int affectedShapeCount = affectedShapeHandles.Length;
                updateOutput = new ShapeDestructionUpdatePipeline().Run(
                    affectedShapeHandles,
                    dataView,
                    removeMasks,
                    _minimalVoxelNumber,
                    Allocator.TempJob,
                    (stage, elapsedMilliseconds) =>
                        pipelineTimings = RecordUpdatePipelineTiming(
                            pipelineTimings,
                            stage,
                            elapsedMilliseconds));

                LogDestructionPipelineElapsed(
                    pipelineStartTimestamp,
                    pipelineTimings,
                    affectedShapeCount,
                    updateOutput.BuiltShapeCount);

                Dictionary<int, ShapePhysicsSourceSnapshot> sourceSnapshots =
                    CaptureSourceSnapshots(affectedShapeHandles);

                return CommitBuiltShapes(
                    affectedShapeHandles,
                    sourceSnapshots,
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
            out NativeArray<byte> removeMasks,
            out NativeArray<int> affectedShapeHandles,
            out long pipelineStartTimestamp,
            out ShapeDestructionPipelineTimings pipelineTimings,
            Vector3 hitWorldPoint)
        {
            removeMasks = default;
            affectedShapeHandles = default;
            pipelineStartTimestamp = 0L;
            pipelineTimings = default;

            ShapeDataView dataView = _storage.GetShapeDataView(_entries[0].ShapeHandle);
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
                        hitWorldPoint,
                        out Bounds destructionWorldBounds,
                        out destructionShapeHandles,
                        out destructionShapeLocalToWorlds,
                        out destructionMasks,
                        out destructionChunkPositions,
                        out destructionChunkUsed))
                {
                    return false;
                }

                targetShapeHandles = CreateTargetShapeHandles(destructionWorldBounds, dataView);
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

                targetShapeLocalToWorlds = CreateTargetShapeLocalToWorlds(targetShapeHandles);

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

                long stageStartTimestamp = Stopwatch.GetTimestamp();
                pipelineStartTimestamp = stageStartTimestamp;
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
                pipelineTimings.DestructionShapeChunkOverlapJob =
                    CompleteAndMeasure(overlapHandle, stageStartTimestamp);

                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle overlapPrefixHandle = new DestructionChunkOverlapPrefixSumJob
                {
                    ChunkOverlapCounts = chunkOverlapCounts,
                    ChunkOverlapOffsets = chunkOverlapOffsets,
                    TotalVoxelMaskBlockCount = totalVoxelMaskBlockCount
                }.Schedule(overlapHandle);
                pipelineTimings.DestructionChunkOverlapPrefixSumJob =
                    CompleteAndMeasure(overlapPrefixHandle, stageStartTimestamp);

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

                stageStartTimestamp = Stopwatch.GetTimestamp();
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
                pipelineTimings.DestructionMaskGenerationJob =
                    CompleteAndMeasure(maskGenerationHandle, stageStartTimestamp);

                commandKeys = new NativeArray<ShapeVoxelRemoveCommandKey>(
                    commandCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle keyHandle = ShapeVoxelRemoveBuildKeyJob.Create(
                    commandBuffer,
                    commandKeys).Schedule(commandCount, 64, maskGenerationHandle);
                pipelineTimings.ShapeVoxelRemoveBuildKeyJob =
                    CompleteAndMeasure(keyHandle, stageStartTimestamp);

                stageStartTimestamp = Stopwatch.GetTimestamp();
                commandKeys.Sort();
                pipelineTimings.CommandKeySort = ElapsedMilliseconds(stageStartTimestamp);

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

                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle buildRangeHandle = new ShapeVoxelRemoveBuildRangeJob
                {
                    SortedKeys = commandKeys,
                    AffectedShapeHandles = affectedShapeHandleScratch,
                    ShapeRanges = shapeRanges,
                    UniqueShapeCount = uniqueShapeCount,
                    KeyCount = commandCount
                }.Schedule();
                pipelineTimings.ShapeVoxelRemoveBuildRangeJob =
                    CompleteAndMeasure(buildRangeHandle, stageStartTimestamp);

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

                stageStartTimestamp = Stopwatch.GetTimestamp();
                JobHandle packHandle = ShapeVoxelRemovePackJob.Create(
                    commandBuffer,
                    commandKeys,
                    shapeRanges,
                    removeMasks).Schedule(affectedShapeCount, 1);
                pipelineTimings.ShapeVoxelRemovePackJob =
                    CompleteAndMeasure(packHandle, stageStartTimestamp);

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

        private void LogDestructionPipelineElapsed(
            long pipelineStartTimestamp,
            ShapeDestructionPipelineTimings timings,
            int affectedShapeCount,
            int builtShapeCount)
        {
            if (pipelineStartTimestamp <= 0L)
            {
                return;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - pipelineStartTimestamp;
            double elapsedMilliseconds = TicksToMilliseconds(elapsedTicks);
            double measuredStageMilliseconds = timings.TotalMeasuredMilliseconds;
            double untrackedMilliseconds = Math.Max(0.0, elapsedMilliseconds - measuredStageMilliseconds);
            Debug.Log(
                "Shape destruction pipeline " +
                $"DestructionShapeChunkOverlapJob -> ShapeBuildJob: {elapsedMilliseconds:F3} ms " +
                $"(affected shapes: {affectedShapeCount}, built shapes: {builtShapeCount})\n" +
                $"  Measured stages subtotal: {measuredStageMilliseconds:F3} ms\n" +
                $"  Outer overhead / rounding: {untrackedMilliseconds:F3} ms\n" +
                $"  DestructionShapeChunkOverlapJob: {timings.DestructionShapeChunkOverlapJob:F3} ms\n" +
                $"  DestructionChunkOverlapPrefixSumJob: {timings.DestructionChunkOverlapPrefixSumJob:F3} ms\n" +
                $"  DestructionMaskGenerationJob: {timings.DestructionMaskGenerationJob:F3} ms\n" +
                $"  ShapeVoxelRemoveBuildKeyJob: {timings.ShapeVoxelRemoveBuildKeyJob:F3} ms\n" +
                $"  CommandKeySort: {timings.CommandKeySort:F3} ms\n" +
                $"  ShapeVoxelRemoveBuildRangeJob: {timings.ShapeVoxelRemoveBuildRangeJob:F3} ms\n" +
                $"  ShapeVoxelRemovePackJob: {timings.ShapeVoxelRemovePackJob:F3} ms\n" +
                $"  ShapeVoxelRemoveJob: {timings.ShapeVoxelRemoveJob:F3} ms\n" +
                $"  ShapeChunkFragmentJob: {timings.ShapeChunkFragmentJob:F3} ms\n" +
                $"  ShapeChunkCheckMaskJob: {timings.ShapeChunkCheckMaskJob:F3} ms\n" +
                $"  ShapeChunkConnectivityJob: {timings.ShapeChunkConnectivityJob:F3} ms\n" +
                $"  ShapeFragmentUnionJob: {timings.ShapeFragmentUnionJob:F3} ms\n" +
                $"  ShapePrefixSumComputeJob: {timings.ShapePrefixSumComputeJob:F3} ms\n" +
                $"  ShapeBuildJob: {timings.ShapeBuildJob:F3} ms",
                this);
        }

        private static double CompleteAndMeasure(JobHandle handle, long startTimestamp)
        {
            handle.Complete();
            return ElapsedMilliseconds(startTimestamp);
        }

        private static ShapeDestructionPipelineTimings RecordUpdatePipelineTiming(
            ShapeDestructionPipelineTimings timings,
            ShapeDestructionUpdatePipelineStage stage,
            double elapsedMilliseconds)
        {
            switch (stage)
            {
                case ShapeDestructionUpdatePipelineStage.ShapeVoxelRemoveJob:
                    timings.ShapeVoxelRemoveJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeChunkFragmentJob:
                    timings.ShapeChunkFragmentJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeChunkCheckMaskJob:
                    timings.ShapeChunkCheckMaskJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeChunkConnectivityJob:
                    timings.ShapeChunkConnectivityJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeFragmentUnionJob:
                    timings.ShapeFragmentUnionJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapePrefixSumComputeJob:
                    timings.ShapePrefixSumComputeJob = elapsedMilliseconds;
                    break;
                case ShapeDestructionUpdatePipelineStage.ShapeBuildJob:
                    timings.ShapeBuildJob = elapsedMilliseconds;
                    break;
            }

            return timings;
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            return TicksToMilliseconds(Stopwatch.GetTimestamp() - startTimestamp);
        }

        private static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private struct ShapeDestructionPipelineTimings
        {
            public double DestructionShapeChunkOverlapJob;
            public double DestructionChunkOverlapPrefixSumJob;
            public double DestructionMaskGenerationJob;
            public double ShapeVoxelRemoveBuildKeyJob;
            public double CommandKeySort;
            public double ShapeVoxelRemoveBuildRangeJob;
            public double ShapeVoxelRemovePackJob;
            public double ShapeVoxelRemoveJob;
            public double ShapeChunkFragmentJob;
            public double ShapeChunkCheckMaskJob;
            public double ShapeChunkConnectivityJob;
            public double ShapeFragmentUnionJob;
            public double ShapePrefixSumComputeJob;
            public double ShapeBuildJob;

            public double TotalMeasuredMilliseconds =>
                DestructionShapeChunkOverlapJob +
                DestructionChunkOverlapPrefixSumJob +
                DestructionMaskGenerationJob +
                ShapeVoxelRemoveBuildKeyJob +
                CommandKeySort +
                ShapeVoxelRemoveBuildRangeJob +
                ShapeVoxelRemovePackJob +
                ShapeVoxelRemoveJob +
                ShapeChunkFragmentJob +
                ShapeChunkCheckMaskJob +
                ShapeChunkConnectivityJob +
                ShapeFragmentUnionJob +
                ShapePrefixSumComputeJob +
                ShapeBuildJob;
        }

        private NativeArray<int> CreateTargetShapeHandles(
            Bounds destructionWorldBounds,
            ShapeDataView dataView)
        {
            List<int> targets = new List<int>(_entries.Count);

            for (int i = 0; i < _entries.Count; i++)
            {
                ShapePhysicsEntry entry = _entries[i];
                if (entry == null ||
                    entry.Host == null ||
                    !entry.DynamicBody ||
                    !entry.Destructible)
                {
                    continue;
                }

                ShapeMetadata shape = dataView.Shapes[entry.ShapeHandle];
                Matrix4x4 shapeVoxelToWorldMatrix = CreateShapeVoxelToWorldMatrix(entry);
                if (!shape.Used)
                {
                    continue;
                }

                if (!TryGetShapeWorldBounds(
                        entry.ShapeHandle,
                        dataView,
                        shapeVoxelToWorldMatrix,
                        out Bounds shapeWorldBounds) ||
                    !shapeWorldBounds.Intersects(destructionWorldBounds))
                {
                    continue;
                }

                targets.Add(entry.ShapeHandle);
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

        private NativeArray<float4x4> CreateTargetShapeLocalToWorlds(
            NativeArray<int> targetShapeHandles)
        {
            NativeArray<float4x4> result = new NativeArray<float4x4>(
                _storage.ShapeSlotCount,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            for (int i = 0; i < targetShapeHandles.Length; i++)
            {
                int shapeHandle = targetShapeHandles[i];
                if (!_entriesByShapeHandle.TryGetValue(shapeHandle, out ShapePhysicsEntry entry) ||
                    entry == null ||
                    entry.Host == null)
                {
                    continue;
                }

                result[shapeHandle] = ToFloat4x4(CreateShapeVoxelToWorldMatrix(entry));
            }

            return result;
        }

        private Dictionary<int, ShapePhysicsSourceSnapshot> CaptureSourceSnapshots(
            NativeArray<int> affectedShapeHandles)
        {
            Dictionary<int, ShapePhysicsSourceSnapshot> snapshots =
                new Dictionary<int, ShapePhysicsSourceSnapshot>(affectedShapeHandles.Length);

            for (int i = 0; i < affectedShapeHandles.Length; i++)
            {
                int shapeHandle = affectedShapeHandles[i];
                if (!_entriesByShapeHandle.TryGetValue(shapeHandle, out ShapePhysicsEntry entry) ||
                    entry == null ||
                    entry.Host == null)
                {
                    continue;
                }

                ShapePhysicsBodyState state = _physicsBridge.CaptureBodyState(entry.Components.Body);
                state.BodyKind = ShapePhysicsBodyKind.Dynamic;

                snapshots.Add(shapeHandle, new ShapePhysicsSourceSnapshot(
                    entry,
                    state,
                    _physicsBridge.ReadWorldCenterOfMass(entry.Components.Body, entry.Host.transform),
                    entry.Host.transform.position,
                    entry.Host.transform.rotation));
            }

            return snapshots;
        }

        private bool TryCreateSphereDestructionShape(
            Vector3 hitWorldPoint,
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
                LogWarningOnce("Shape physics debugger destruction radius is too large for one 8-chunk destruction shape.");
                return false;
            }

            float3 maskCenter = new float3(maskDimensions) * 0.5f;
            Matrix4x4 destructionLocalToWorld =
                Matrix4x4.TRS(hitWorldPoint, Quaternion.identity, Vector3.one * _voxelSize) *
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

        private Matrix4x4 CreateShapeVoxelToWorldMatrix(ShapePhysicsEntry entry)
        {
            return entry.Host.transform.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * _voxelSize);
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
            Dictionary<int, ShapePhysicsSourceSnapshot> sourceSnapshots,
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
                if (!sourceSnapshots.TryGetValue(sourceShapeHandle, out ShapePhysicsSourceSnapshot snapshot))
                {
                    continue;
                }

                int sourceBodyHandle = dataView.Shapes[sourceShapeHandle].BodyHandle;
                int sourceBuiltShapeCount = GetBuiltShapeCount(shapeListIndex, localShapeCounts);
                int nonEmptyBuiltShapeCount = GetNonEmptyBuiltShapeCount(
                    shapeListIndex,
                    localShapeCounts,
                    builtShapeOffsets,
                    builtChunks);

                if (nonEmptyBuiltShapeCount == 0)
                {
                    RemoveEntry(snapshot.SourceEntry, releaseShape: true, destroyHost: true);
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
                        sourceBodyHandle,
                        shapeListIndex,
                        localShapeId,
                        builtShapeOffsets,
                        builtChunks,
                        builtIsOccupied);
                    RefreshPhysicsForEntry(snapshot.SourceEntry, snapshot.SourceState);
                    RefreshRendererForEntry(snapshot.SourceEntry);
                    continue;
                }

                Color32 firstColor = snapshot.SourceEntry.Color;
                bool sourceDestructible = snapshot.SourceEntry.Destructible;
                RemoveEntry(snapshot.SourceEntry, releaseShape: true, destroyHost: true);

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

                    GameObject host = CreatePhysicsHost(
                        $"Dynamic Voxel Fragment {localShapeId + 1}",
                        snapshot.SourceWorldPosition,
                        snapshot.SourceWorldRotation);
                    if (!_physicsBridge.TryCreatePhysicsComponents(
                            host,
                            out ShapePhysicsComponents components,
                            out string error))
                    {
                        LogWarningOnce($"Shape physics debugger could not create split physics components: {error}");
                        DestroyHost(host);
                        continue;
                    }

                    int bodyHandle = _physicsBridge.ReadBodyHandleValue(
                        components.Body,
                        _nextFallbackBodyHandle++);
                    int newShapeHandle = _storage.Acquire(bodyHandle);
                    if (newShapeHandle == ShapeDataStorage.InvalidHandle)
                    {
                        LogWarningOnce("Shape physics debugger ran out of shape handles during split commit.");
                        DestroyHost(host);
                        continue;
                    }

                    WriteBuiltShapeToHandle(
                        newShapeHandle,
                        bodyHandle,
                        shapeListIndex,
                        localShapeId,
                        builtShapeOffsets,
                        builtChunks,
                        builtIsOccupied);

                    Color32 color = localShapeId == 0 ? firstColor : AllocateColor();
                    ShapePhysicsEntry newEntry = new ShapePhysicsEntry(
                        newShapeHandle,
                        host,
                        components,
                        color,
                        sourceDestructible,
                        dynamicBody: true);
                    RegisterEntry(newEntry);

                    ShapePhysicsBodyState childState = snapshot.SourceState;
                    childState.BodyKind = ShapePhysicsBodyKind.Dynamic;
                    if (TryGetShapeLocalBounds(newShapeHandle, out Bounds bounds))
                    {
                        Vector3 newWorldCenterOfMass = host.transform.TransformPoint(bounds.center);
                        childState.LinearVelocity =
                            snapshot.SourceState.LinearVelocity +
                            Vector3.Cross(
                                snapshot.SourceState.AngularVelocity,
                                newWorldCenterOfMass - snapshot.SourceWorldCenterOfMass);
                    }

                    RefreshPhysicsForEntry(newEntry, childState);
                    RefreshRendererForEntry(newEntry);
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

            LogWarningOnce(
                "Shape physics debugger cannot commit destruction batch: storage has insufficient free shape slots.");
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
                NativeArray<byte>.Copy(
                    builtIsOccupied,
                    builtChunkIndex * BitPlaneBytesPerChunk,
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

        private void WriteFullChunkShape(
            int shapeHandle,
            int bodyHandle,
            int3[] chunkPositions)
        {
            ShapeDataView dataView = _storage.GetShapeDataView(shapeHandle);
            ClearShapeForRewrite(shapeHandle, bodyHandle, dataView);

            int chunkCount = math.min(chunkPositions.Length, ChunksPerShape);
            for (int chunkSlot = 0; chunkSlot < chunkCount; chunkSlot++)
            {
                int chunkMetadataIndex = shapeHandle * ChunksPerShape + chunkSlot;
                WriteFullChunk(dataView.IsOccupied, chunkMetadataIndex);
                WriteChunkSlotData(
                    shapeHandle,
                    bodyHandle,
                    chunkSlot,
                    chunkPositions[chunkSlot],
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

        #endregion


        #region Physics Sync

        private void RefreshPhysicsForEntry(
            ShapePhysicsEntry entry,
            ShapePhysicsBodyState state)
        {
            if (entry == null)
            {
                return;
            }

            if (!_physicsBridge.RefreshColliderFromShape(
                    entry.Components.Collider,
                    _storage,
                    entry.ShapeHandle,
                    out string error))
            {
                LogWarningOnce($"Shape physics debugger could not refresh MiniCollider: {error}");
            }

            if (TryGetShapeLocalBounds(entry.ShapeHandle, out Bounds bounds))
            {
                state.LocalCenterOfMass = bounds.center;
                state.InertiaSize = SanitizePositiveVector(bounds.size, 0.000001f);
            }

            if (!entry.DynamicBody)
            {
                state.BodyKind = ShapePhysicsBodyKind.Static;
                state.UseGravity = false;
                state.LinearVelocity = Vector3.zero;
                state.AngularVelocity = Vector3.zero;
            }

            _physicsBridge.ConfigureBody(entry.Components.Body, state);
        }

        private bool TryGetShapeLocalBounds(int shapeHandle, out Bounds bounds)
        {
            ShapeDataView dataView = _storage.GetShapeDataView(shapeHandle);
            int chunkBase = shapeHandle * ChunksPerShape;
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool found = false;

            for (int chunkSlot = 0; chunkSlot < ChunksPerShape; chunkSlot++)
            {
                int chunkMetadataIndex = chunkBase + chunkSlot;
                if (dataView.ChunkUsed[chunkMetadataIndex] == 0)
                {
                    continue;
                }

                int sourceBase = chunkMetadataIndex * BitPlaneBytesPerChunk;
                int3 chunkVoxelOrigin = dataView.ChunkPositions[chunkMetadataIndex] * ChunkSize;
                for (int z = 0; z < ChunkSize; z++)
                {
                    for (int y = 0; y < ChunkSize; y++)
                    {
                        byte row = dataView.IsOccupied[sourceBase + RowIndex(y, z)];
                        while (row != 0)
                        {
                            int x = math.tzcnt((uint)row);
                            int3 voxel = chunkVoxelOrigin + new int3(x, y, z);
                            Vector3 voxelMin = new Vector3(voxel.x, voxel.y, voxel.z) * _voxelSize;
                            Vector3 voxelMax = voxelMin + (Vector3.one * _voxelSize);
                            min = Vector3.Min(min, voxelMin);
                            max = Vector3.Max(max, voxelMax);
                            found = true;
                            row = (byte)(row & ~(1 << x));
                        }
                    }
                }
            }

            if (!found)
            {
                bounds = default;
                return false;
            }

            Vector3 size = max - min;
            bounds = new Bounds(min + (size * 0.5f), size);
            return true;
        }

        private void RefreshRendererForEntry(ShapePhysicsEntry entry)
        {
            if (entry == null || entry.Host == null)
            {
                return;
            }

            _renderer.RegisterOrRefreshShape(
                entry.ShapeHandle,
                entry.Color,
                entry.Host.transform.localToWorldMatrix);
        }

        #endregion


        #region Drag

        private void BeginDrag(Camera rayCamera)
        {
            Ray ray = _useCameraCenter
                ? rayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f))
                : rayCamera.ScreenPointToRay(GetPointerScreenPosition(rayCamera));
            if (!TryPickShape(ray, requireDynamic: true, out ShapePhysicsHit hit) ||
                hit.Entry == null)
            {
                return;
            }

            ShapePhysicsBodyState state = _physicsBridge.CaptureBodyState(hit.Entry.Components.Body);
            Vector3 rayDirection = NormalizeOrFallback(ray.direction, rayCamera.transform.forward);
            float rayDistance = Vector3.Dot(hit.WorldPoint - ray.origin, rayDirection);
            rayDistance = Mathf.Max(0.0f, rayDistance);

            _drag = new DragState
            {
                Active = true,
                Entry = hit.Entry,
                LocalGrabOffset = hit.Entry.Host.transform.InverseTransformPoint(hit.WorldPoint),
                RayDistance = rayDistance,
                RestoreUseGravity = state.UseGravity,
                RestoreAngularVelocity = state.AngularVelocity,
                PreviousWorldPosition = hit.Entry.Host.transform.position,
                ReleaseVelocity = Vector3.zero
            };

            _physicsBridge.SetBodyKind(hit.Entry.Components.Body, ShapePhysicsBodyKind.Kinematic);
            _physicsBridge.SetUseGravity(hit.Entry.Components.Body, false);
            _physicsBridge.SetLinearVelocity(hit.Entry.Components.Body, Vector3.zero);
            _physicsBridge.SetAngularVelocity(hit.Entry.Components.Body, Vector3.zero);
        }

        private void UpdateDrag(Camera rayCamera)
        {
            if (!_drag.Active || _drag.Entry == null || _drag.Entry.Host == null)
            {
                return;
            }

            Ray ray = _useCameraCenter
                ? rayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f))
                : rayCamera.ScreenPointToRay(GetPointerScreenPosition(rayCamera));
            Vector3 rayDirection = NormalizeOrFallback(ray.direction, rayCamera.transform.forward);
            Vector3 targetGrabPoint = ray.origin + (rayDirection * _drag.RayDistance);
            Transform hostTransform = _drag.Entry.Host.transform;
            Vector3 worldOffset = hostTransform.rotation * _drag.LocalGrabOffset;
            Vector3 newPosition = targetGrabPoint - worldOffset;
            float deltaTime = Mathf.Max(Time.deltaTime, 0.000001f);

            _drag.ReleaseVelocity = (newPosition - _drag.PreviousWorldPosition) / deltaTime;
            _drag.PreviousWorldPosition = newPosition;
            _physicsBridge.MoveBodyPosition(_drag.Entry.Components.Body, hostTransform, newPosition);
        }

        private void EndDrag()
        {
            if (!_drag.Active || _drag.Entry == null)
            {
                _drag = default;
                return;
            }

            _physicsBridge.SetBodyKind(_drag.Entry.Components.Body, ShapePhysicsBodyKind.Dynamic);
            _physicsBridge.SetUseGravity(_drag.Entry.Components.Body, _drag.RestoreUseGravity);
            _physicsBridge.SetLinearVelocity(_drag.Entry.Components.Body, _drag.ReleaseVelocity);
            _physicsBridge.SetAngularVelocity(_drag.Entry.Components.Body, _drag.RestoreAngularVelocity);
            _drag = default;
        }

        #endregion


        #region Entry Management

        private void RegisterEntry(ShapePhysicsEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _entries.Add(entry);
            _entriesByShapeHandle[entry.ShapeHandle] = entry;
        }

        private void RemoveEntry(
            ShapePhysicsEntry entry,
            bool releaseShape,
            bool destroyHost)
        {
            if (entry == null)
            {
                return;
            }

            _renderer.UnregisterShape(entry.ShapeHandle);
            _entries.Remove(entry);
            _entriesByShapeHandle.Remove(entry.ShapeHandle);

            if (releaseShape && _storage != null && _storage.IsCreated)
            {
                _storage.Release(entry.ShapeHandle);
            }

            if (destroyHost)
            {
                DestroyHost(entry.Host);
            }
        }

        private void DestroyHost(GameObject host)
        {
            if (host == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(host);
            }
            else
            {
                DestroyImmediate(host);
            }
        }

        private Color32 AllocateColor()
        {
            float hue = math.frac(_nextColorIndex++ * 0.61803398875f);
            Color color = Color.HSVToRGB(hue, 0.72f, 1.0f);
            return color;
        }

        #endregion


        #region Picking

        private bool TryPickShape(
            Ray worldRay,
            bool requireDynamic,
            out ShapePhysicsHit hit)
        {
            hit = default;
            float voxelSize = Mathf.Max(0.000001f, _voxelSize);
            float bestDistance = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < _entries.Count; i++)
            {
                ShapePhysicsEntry entry = _entries[i];
                if (entry == null ||
                    entry.Host == null ||
                    (requireDynamic && !entry.DynamicBody))
                {
                    continue;
                }

                ShapeDataView dataView = _storage.GetShapeDataView(entry.ShapeHandle);
                ShapeMetadata shape = dataView.Shapes[entry.ShapeHandle];
                if (!shape.Used)
                {
                    continue;
                }

                Matrix4x4 worldToLocal = entry.Host.transform.worldToLocalMatrix;
                float3 origin = (float3)worldToLocal.MultiplyPoint(worldRay.origin) / voxelSize;
                float3 direction = (float3)worldToLocal.MultiplyVector(worldRay.direction);
                if (math.lengthsq(direction) <= 0.000000000001f)
                {
                    continue;
                }

                direction = math.normalize(direction);
                if (!TryGetShapeVoxelBounds(entry.ShapeHandle, dataView, out int3 minVoxel, out int3 maxVoxel))
                {
                    continue;
                }

                float3 min = new float3(minVoxel.x, minVoxel.y, minVoxel.z);
                float3 max = new float3(maxVoxel.x, maxVoxel.y, maxVoxel.z);
                if (!TryIntersectAabb(origin, direction, min, max, out float enter, out float exit))
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
                        entry.ShapeHandle,
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
                Vector3 localHitPoint = (Vector3)(origin + direction * totalDistance) * voxelSize;
                Vector3 worldHitPoint = entry.Host.transform.TransformPoint(localHitPoint);
                hit = new ShapePhysicsHit(entry, hitVoxel, worldHitPoint);
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

            return TryReadInputSystemButton("leftButton", "wasPressedThisFrame", out bool pressed) && pressed;
        }

        private bool WasSecondaryClickPressed()
        {
            try
            {
                if (Input.GetMouseButtonDown(1))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }

            return TryReadInputSystemButton("rightButton", "wasPressedThisFrame", out bool pressed) && pressed;
        }

        private bool IsSecondaryClickHeld()
        {
            try
            {
                if (Input.GetMouseButton(1))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }

            return TryReadInputSystemButton("rightButton", "isPressed", out bool pressed) && pressed;
        }

        private bool WasSecondaryClickReleased()
        {
            try
            {
                if (Input.GetMouseButtonUp(1))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }

            return TryReadInputSystemButton("rightButton", "wasReleasedThisFrame", out bool released) && released;
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

        private static bool TryReadInputSystemButton(
            string buttonPropertyName,
            string valuePropertyName,
            out bool value)
        {
            value = false;
            object mouse = GetInputSystemMouse();
            if (mouse == null)
            {
                return false;
            }

            object button = mouse.GetType().GetProperty(buttonPropertyName)?.GetValue(mouse);
            object propertyValue = button?.GetType().GetProperty(valuePropertyName)?.GetValue(button);
            if (propertyValue is bool boolValue)
            {
                value = boolValue;
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

        private void ResolveGlobalVoxelSize()
        {
            float globalVoxelSize = _physicsBridge.ReadGlobalVoxelSize();
            if (globalVoxelSize > 0.0f && float.IsFinite(globalVoxelSize))
            {
                _voxelSize = globalVoxelSize;
            }
        }

        private void SanitizeSerializedState()
        {
            _floorChunkDimensions = SanitizeFloorChunkDimensions(_floorChunkDimensions);
            _shapeCapacity = math.max(
                math.max(1, _shapeCapacity),
                GetInitialShapeCapacityFloor(_floorChunkDimensions));
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
            _dynamicShapeMass = Mathf.Max(
                0.000001f,
                float.IsFinite(_dynamicShapeMass) ? _dynamicShapeMass : 48.0f);
            _dynamicLinearDamping = Mathf.Max(
                0.0f,
                float.IsFinite(_dynamicLinearDamping) ? _dynamicLinearDamping : 0.0f);
            _dynamicAngularDamping = Mathf.Max(
                0.0f,
                float.IsFinite(_dynamicAngularDamping) ? _dynamicAngularDamping : 0.05f);
            _dynamicGravityScale = float.IsFinite(_dynamicGravityScale) ? _dynamicGravityScale : 1.0f;
        }

        private static Vector3Int SanitizeFloorChunkDimensions(Vector3Int dimensions)
        {
            return new Vector3Int(
                Mathf.Max(1, dimensions.x),
                Mathf.Max(1, dimensions.y),
                Mathf.Max(1, dimensions.z));
        }

        private static int GetInitialShapeCapacityFloor(Vector3Int floorChunkDimensions)
        {
            int floorChunkCount = checked(floorChunkDimensions.x * floorChunkDimensions.y * floorChunkDimensions.z);
            int floorShapeCount = (floorChunkCount + ChunksPerShape - 1) / ChunksPerShape;
            return floorShapeCount + 1 + ChunksPerShape;
        }

        private void LogWarningOnce(string message)
        {
            if (string.IsNullOrEmpty(message) || _lastWarningKey == message)
            {
                return;
            }

            _lastWarningKey = message;
            Debug.LogWarning(message, this);
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

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            float magnitude = value.magnitude;
            return magnitude > 0.000001f ? value / magnitude : fallback;
        }

        private static Quaternion NormalizeRotation(Quaternion rotation)
        {
            float lengthSquared =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            if (lengthSquared <= 0.0f || !float.IsFinite(lengthSquared))
            {
                return Quaternion.identity;
            }

            float inverseLength = 1.0f / Mathf.Sqrt(lengthSquared);
            return new Quaternion(
                rotation.x * inverseLength,
                rotation.y * inverseLength,
                rotation.z * inverseLength,
                rotation.w * inverseLength);
        }

        private static Vector3 SanitizePositiveVector(Vector3 value, float minimum)
        {
            return new Vector3(
                Mathf.Max(minimum, float.IsFinite(value.x) ? value.x : minimum),
                Mathf.Max(minimum, float.IsFinite(value.y) ? value.y : minimum),
                Mathf.Max(minimum, float.IsFinite(value.z) ? value.z : minimum));
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


        private sealed class ShapePhysicsEntry
        {
            public ShapePhysicsEntry(
                int shapeHandle,
                GameObject host,
                ShapePhysicsComponents components,
                Color32 color,
                bool destructible,
                bool dynamicBody)
            {
                ShapeHandle = shapeHandle;
                Host = host;
                Components = components;
                Color = color;
                Destructible = destructible;
                DynamicBody = dynamicBody;
            }

            public int ShapeHandle { get; }
            public GameObject Host { get; }
            public ShapePhysicsComponents Components { get; }
            public Color32 Color { get; }
            public bool Destructible { get; }
            public bool DynamicBody { get; }
        }


        private struct DragState
        {
            public bool Active;
            public ShapePhysicsEntry Entry;
            public Vector3 LocalGrabOffset;
            public float RayDistance;
            public bool RestoreUseGravity;
            public Vector3 RestoreAngularVelocity;
            public Vector3 PreviousWorldPosition;
            public Vector3 ReleaseVelocity;
        }


        private readonly struct ShapePhysicsSourceSnapshot
        {
            public ShapePhysicsSourceSnapshot(
                ShapePhysicsEntry sourceEntry,
                ShapePhysicsBodyState sourceState,
                Vector3 sourceWorldCenterOfMass,
                Vector3 sourceWorldPosition,
                Quaternion sourceWorldRotation)
            {
                SourceEntry = sourceEntry;
                SourceState = sourceState;
                SourceWorldCenterOfMass = sourceWorldCenterOfMass;
                SourceWorldPosition = sourceWorldPosition;
                SourceWorldRotation = sourceWorldRotation;
            }

            public ShapePhysicsEntry SourceEntry { get; }
            public ShapePhysicsBodyState SourceState { get; }
            public Vector3 SourceWorldCenterOfMass { get; }
            public Vector3 SourceWorldPosition { get; }
            public Quaternion SourceWorldRotation { get; }
        }


        private readonly struct ShapePhysicsHit
        {
            public ShapePhysicsHit(
                ShapePhysicsEntry entry,
                int3 voxel,
                Vector3 worldPoint)
            {
                Entry = entry;
                Voxel = voxel;
                WorldPoint = worldPoint;
            }

            public ShapePhysicsEntry Entry { get; }
            public int3 Voxel { get; }
            public Vector3 WorldPoint { get; }
        }
    }
}
