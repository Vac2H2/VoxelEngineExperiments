using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Physics.Destruction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("VoxelEngine/Physics/Destruction Debug Block Bootstrap")]
    public sealed class DestructionDebugBlockBootstrap : MonoBehaviour
    {
        [SerializeField] private DestructionDebugRenderer _renderer;
        [SerializeField] private DestructionDebugObjectBinding _binding;
        [SerializeField] private Vector3Int _minVoxel = new Vector3Int(-16, 0, -16);
        [SerializeField] private Vector3Int _sizeInVoxels = new Vector3Int(32, 24, 32);
        [SerializeField] private Color32 _color = new Color32(72, 196, 84, 255);
        [SerializeField] private byte _voxelState = 1;
        [SerializeField] private bool _syncAfterBuild = true;

        public Vector3Int MinVoxel
        {
            get => _minVoxel;
            set
            {
                _minVoxel = value;
                ConfigureCollider(GetComponent<BoxCollider>());
            }
        }

        public Vector3Int SizeInVoxels
        {
            get => _sizeInVoxels;
            set
            {
                _sizeInVoxels = SanitizeSize(value);
                ConfigureCollider(GetComponent<BoxCollider>());
            }
        }

        public Color32 Color
        {
            get => _color;
            set => _color = value;
        }

        public byte VoxelState
        {
            get => _voxelState;
            set => _voxelState = DestructionDebugObject.SanitizeVoxelState(value);
        }

        public void Bind(DestructionDebugRenderer renderer, DestructionDebugObjectBinding binding)
        {
            _renderer = renderer;
            _binding = binding;
        }

        public void RefreshCollider()
        {
            SanitizeSerializedState();
            ConfigureCollider(EnsureBoxCollider());
        }

        private void Awake()
        {
            BuildRuntimeBlock();
        }

        private void Reset()
        {
            ResolveComponents();
            ConfigureCollider(GetComponent<BoxCollider>());
        }

        private void OnValidate()
        {
            SanitizeSerializedState();
            ResolveComponents();
            ConfigureCollider(GetComponent<BoxCollider>());

            if (Application.isPlaying && isActiveAndEnabled)
            {
                BuildRuntimeBlock();
            }
        }

        public void BuildRuntimeBlock()
        {
            SanitizeSerializedState();
            ResolveComponents();
            if (_renderer == null)
            {
                return;
            }

            _renderer.AutoSyncDirtyData = false;
            _renderer.ClearObjects();

            DestructionDebugObject debugObject = _renderer.CreateObject(_color);
            FillVoxelBox(debugObject);
            debugObject.Normalize();

            if (_binding != null)
            {
                _binding.Bind(_renderer, debugObject);
            }

            ConfigureCollider(EnsureBoxCollider());
            _renderer.MarkRenderDataDirty();
            if (Application.isPlaying && _syncAfterBuild)
            {
                _renderer.SyncRenderBackend();
            }
        }

        private void FillVoxelBox(DestructionDebugObject debugObject)
        {
            const int chunkSize = VoxelVolume.ChunkDimension;
            int3 minVoxel = ToInt3(_minVoxel);
            int3 maxVoxelExclusive = minVoxel + ToInt3(_sizeInVoxels);
            int3 minChunk = new int3(
                FloorDiv(minVoxel.x, chunkSize),
                FloorDiv(minVoxel.y, chunkSize),
                FloorDiv(minVoxel.z, chunkSize));
            int3 maxChunk = new int3(
                FloorDiv(maxVoxelExclusive.x - 1, chunkSize),
                FloorDiv(maxVoxelExclusive.y - 1, chunkSize),
                FloorDiv(maxVoxelExclusive.z - 1, chunkSize));

            for (int chunkZ = minChunk.z; chunkZ <= maxChunk.z; chunkZ++)
            {
                for (int chunkY = minChunk.y; chunkY <= maxChunk.y; chunkY++)
                {
                    for (int chunkX = minChunk.x; chunkX <= maxChunk.x; chunkX++)
                    {
                        int3 chunkPosition = new int3(chunkX, chunkY, chunkZ);
                        DestructionDebugChunk chunk = debugObject.GetOrCreateChunk(chunkPosition);
                        FillChunkIntersection(chunk, chunkPosition, minVoxel, maxVoxelExclusive);
                    }
                }
            }
        }

        private void FillChunkIntersection(
            DestructionDebugChunk chunk,
            int3 chunkPosition,
            int3 minVoxel,
            int3 maxVoxelExclusive)
        {
            const int chunkSize = VoxelVolume.ChunkDimension;
            int3 chunkMinVoxel = chunkPosition * chunkSize;
            int3 localMin = new int3(
                math.max(0, minVoxel.x - chunkMinVoxel.x),
                math.max(0, minVoxel.y - chunkMinVoxel.y),
                math.max(0, minVoxel.z - chunkMinVoxel.z));
            int3 localMaxExclusive = new int3(
                math.min(chunkSize, maxVoxelExclusive.x - chunkMinVoxel.x),
                math.min(chunkSize, maxVoxelExclusive.y - chunkMinVoxel.y),
                math.min(chunkSize, maxVoxelExclusive.z - chunkMinVoxel.z));

            for (int z = localMin.z; z < localMaxExclusive.z; z++)
            {
                for (int y = localMin.y; y < localMaxExclusive.y; y++)
                {
                    for (int x = localMin.x; x < localMaxExclusive.x; x++)
                    {
                        int globalY = chunkPosition.y * chunkSize + y;
                        byte voxelValue = DestructionDebugObject.BuildVoxelValue(
                            _voxelState,
                            globalY == minVoxel.y);
                        chunk.SetVoxel(x, y, z, voxelValue);
                    }
                }
            }
        }

        private void ConfigureCollider(BoxCollider boxCollider)
        {
            if (boxCollider == null)
            {
                return;
            }

            float voxelSize = VoxelEngineSettings.SanitizeVoxelSize(VoxelEngineSettings.GlobalVoxelSize);
            boxCollider.isTrigger = false;
            boxCollider.center = (ToVector3(_minVoxel) + (ToVector3(_sizeInVoxels) * 0.5f)) * voxelSize;
            boxCollider.size = ToVector3(_sizeInVoxels) * voxelSize;
        }

        private BoxCollider EnsureBoxCollider()
        {
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            return boxCollider != null ? boxCollider : gameObject.AddComponent<BoxCollider>();
        }

        private void ResolveComponents()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<DestructionDebugRenderer>();
            }

            if (_binding == null)
            {
                _binding = GetComponent<DestructionDebugObjectBinding>();
            }
        }

        private void SanitizeSerializedState()
        {
            _sizeInVoxels = SanitizeSize(_sizeInVoxels);
            _voxelState = DestructionDebugObject.SanitizeVoxelState(_voxelState);
        }

        private static Vector3Int SanitizeSize(Vector3Int size)
        {
            return new Vector3Int(
                Mathf.Max(1, size.x),
                Mathf.Max(1, size.y),
                Mathf.Max(1, size.z));
        }

        private static int3 ToInt3(Vector3Int value)
        {
            return new int3(value.x, value.y, value.z);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder != 0 && ((remainder < 0) != (divisor < 0))
                ? quotient - 1
                : quotient;
        }

        private static Vector3 ToVector3(int3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static Vector3 ToVector3(Vector3Int value)
        {
            return new Vector3(value.x, value.y, value.z);
        }
    }
}
