using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VoxelEngine.Physics.Body;
using VoxelEngine.Physics.Collider;

namespace VoxelEngine.Physics.DebugRenderer
{
    public enum MiniPhysicsThrowShape
    {
        Box = 0,
        Sphere = 1,
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Physics/Mini Physics Voxel Thrower")]
    public sealed class MiniPhysicsVoxelThrower : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private MiniPhysicsThrowShape _shape = MiniPhysicsThrowShape.Sphere;
        [SerializeField] private Vector3Int _chunkDimensions = Vector3Int.one;
        [SerializeField] private float _mass = 24.0f;
        [SerializeField] private float _spawnDistance = 6.0f;
        [SerializeField] private float _launchSpeed = 34.0f;
        [SerializeField] private float _upwardVelocity = 1.5f;
        [SerializeField] private Vector3 _angularVelocity = new Vector3(0.0f, 1.5f, -0.7f);
        [SerializeField] private int _maxSpawnedObjects = 48;

        private readonly Queue<GameObject> _spawnedObjects = new Queue<GameObject>();

        private void Awake()
        {
            ResolveCamera();
            SanitizeSerializedState();
        }

        private void Update()
        {
            if (!Application.isPlaying || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            ThrowVoxelBox();
        }

        private void OnValidate()
        {
            SanitizeSerializedState();
        }

        private void ThrowVoxelBox()
        {
            Camera throwCamera = ResolveCamera();
            if (throwCamera == null)
            {
                return;
            }

            MiniColliderData colliderData = BuildColliderData(_shape, SanitizeDimensions(_chunkDimensions));
            if (!colliderData.TryGetLocalBounds(out Bounds localBounds))
            {
                return;
            }

            Vector3 size = localBounds.size;
            Quaternion rotation = Quaternion.identity;
            Vector3 localCenterOfMass = localBounds.center;
            Vector3 spawnCenter = throwCamera.transform.position + (throwCamera.transform.forward * _spawnDistance);
            Vector3 spawnPosition = spawnCenter - (rotation * localCenterOfMass);

            GameObject spawnedObject = new GameObject(_shape == MiniPhysicsThrowShape.Sphere
                ? "Thrown Mini Physics Sphere"
                : "Thrown Mini Physics Box");
            spawnedObject.transform.SetPositionAndRotation(spawnPosition, rotation);
            spawnedObject.transform.localScale = Vector3.one;

            MiniRigidBody body = spawnedObject.AddComponent<MiniRigidBody>();
            body.BodyType = MiniRigidBodyType.Dynamic;
            body.Mass = _mass;
            body.LocalCenterOfMass = localCenterOfMass;
            if (_shape == MiniPhysicsThrowShape.Sphere)
            {
                body.SetSolidSphereInertiaTensor(Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f);
            }
            else
            {
                body.SetSolidBoxInertiaTensor(size);
            }
            body.UseGravity = true;
            body.GravityScale = 1.0f;
            body.LinearDamping = 0.0f;
            body.AngularDamping = 0.05f;
            body.LinearVelocity = (throwCamera.transform.forward * _launchSpeed) + (Vector3.up * _upwardVelocity);
            body.AngularVelocity = _angularVelocity;

            MiniCollider collider = spawnedObject.AddComponent<MiniCollider>();
            collider.Data.SetChunks(colliderData.Chunks);

            TrackSpawnedObject(spawnedObject);
        }

        private void TrackSpawnedObject(GameObject spawnedObject)
        {
            _spawnedObjects.Enqueue(spawnedObject);
            while (_spawnedObjects.Count > _maxSpawnedObjects)
            {
                GameObject staleObject = _spawnedObjects.Dequeue();
                if (staleObject != null)
                {
                    Destroy(staleObject);
                }
            }
        }

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
            _chunkDimensions = SanitizeDimensions(_chunkDimensions);
            _mass = Mathf.Max(0.000001f, float.IsFinite(_mass) ? _mass : 1.0f);
            _spawnDistance = Mathf.Max(0.0f, float.IsFinite(_spawnDistance) ? _spawnDistance : 0.0f);
            _launchSpeed = Mathf.Max(0.0f, float.IsFinite(_launchSpeed) ? _launchSpeed : 0.0f);
            _upwardVelocity = float.IsFinite(_upwardVelocity) ? _upwardVelocity : 0.0f;
            _angularVelocity = SanitizeVector(_angularVelocity);
            _maxSpawnedObjects = Mathf.Max(1, _maxSpawnedObjects);
        }

        private static Vector3Int SanitizeDimensions(Vector3Int dimensions)
        {
            return new Vector3Int(
                Mathf.Max(1, dimensions.x),
                Mathf.Max(1, dimensions.y),
                Mathf.Max(1, dimensions.z));
        }

        private static MiniColliderData BuildColliderData(MiniPhysicsThrowShape shape, Vector3Int dimensions)
        {
            MiniColliderData colliderData = new MiniColliderData();
            colliderData.SetChunks(shape == MiniPhysicsThrowShape.Sphere
                ? CreateChunkSphere(dimensions)
                : CreateChunkBox(dimensions));
            return colliderData;
        }

        private static MiniColliderChunkData[] CreateChunkBox(Vector3Int dimensions)
        {
            MiniColliderChunkData[] chunks = new MiniColliderChunkData[checked(dimensions.x * dimensions.y * dimensions.z)];
            int index = 0;
            for (int z = 0; z < dimensions.z; z++)
            {
                for (int y = 0; y < dimensions.y; y++)
                {
                    for (int x = 0; x < dimensions.x; x++)
                    {
                        chunks[index++] = new MiniColliderChunkData(new int3(x, y, z));
                    }
                }
            }

            return chunks;
        }

        private static MiniColliderChunkData[] CreateChunkSphere(Vector3Int dimensions)
        {
            int chunkDimension = MiniColliderData.ChunkVoxelDimension;
            int totalVoxelX = dimensions.x * chunkDimension;
            int totalVoxelY = dimensions.y * chunkDimension;
            int totalVoxelZ = dimensions.z * chunkDimension;
            Vector3 center = new Vector3(totalVoxelX, totalVoxelY, totalVoxelZ) * 0.5f;
            float radius = Mathf.Min(totalVoxelX, Mathf.Min(totalVoxelY, totalVoxelZ)) * 0.5f;
            float radiusSq = radius * radius;

            Dictionary<int3, List<int>> occupiedIndicesByChunk = new Dictionary<int3, List<int>>();
            for (int z = 0; z < totalVoxelZ; z++)
            {
                for (int y = 0; y < totalVoxelY; y++)
                {
                    for (int x = 0; x < totalVoxelX; x++)
                    {
                        Vector3 voxelCenter = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        if ((voxelCenter - center).sqrMagnitude > radiusSq)
                        {
                            continue;
                        }

                        int3 chunkPosition = new int3(x / chunkDimension, y / chunkDimension, z / chunkDimension);
                        int localX = x % chunkDimension;
                        int localY = y % chunkDimension;
                        int localZ = z % chunkDimension;
                        if (!occupiedIndicesByChunk.TryGetValue(chunkPosition, out List<int> occupiedIndices))
                        {
                            occupiedIndices = new List<int>();
                            occupiedIndicesByChunk.Add(chunkPosition, occupiedIndices);
                        }

                        occupiedIndices.Add(MiniColliderChunkData.FlattenLocalVoxelIndex(localX, localY, localZ));
                    }
                }
            }

            MiniColliderChunkData[] chunks = new MiniColliderChunkData[occupiedIndicesByChunk.Count];
            int chunkIndex = 0;
            foreach (var pair in occupiedIndicesByChunk)
            {
                chunks[chunkIndex++] = new MiniColliderChunkData(pair.Key, pair.Value.ToArray());
            }

            return chunks;
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(
                float.IsFinite(value.x) ? value.x : 0.0f,
                float.IsFinite(value.y) ? value.y : 0.0f,
                float.IsFinite(value.z) ? value.z : 0.0f);
        }
    }
}
