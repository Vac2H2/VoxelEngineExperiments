using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Physics.Collider;

namespace VoxelEngine.Physics.Simulation.Stages.Narrowphase.VoxelContactGeneration
{
    internal sealed class MiniVoxelContactGenerator
    {
        private const float Epsilon = 0.000001f;

        private static readonly MiniVoxelKey[] NeighborOffsets =
        {
            new MiniVoxelKey(1, 0, 0),
            new MiniVoxelKey(-1, 0, 0),
            new MiniVoxelKey(0, 1, 0),
            new MiniVoxelKey(0, -1, 0),
            new MiniVoxelKey(0, 0, 1),
            new MiniVoxelKey(0, 0, -1),
        };

        private readonly Dictionary<int, ShapeCacheEntry> _shapeCacheByColliderId = new Dictionary<int, ShapeCacheEntry>();

        public void Generate(
            MiniColliderFrame colliderAFrame,
            MiniColliderFrame colliderBFrame,
            List<MiniVoxelContact> contacts)
        {
            if (contacts == null)
            {
                throw new ArgumentNullException(nameof(contacts));
            }

            MiniCollider colliderA = colliderAFrame.Collider;
            MiniCollider colliderB = colliderBFrame.Collider;
            if (colliderA == null || colliderB == null)
            {
                return;
            }

            MiniVoxelShape shapeA = GetShape(colliderA);
            MiniVoxelShape shapeB = GetShape(colliderB);
            if (shapeA.SurfaceVoxelCount == 0 || shapeB.SurfaceVoxelCount == 0)
            {
                return;
            }

            bool iterateA = shapeA.SurfaceVoxelCount <= shapeB.SurfaceVoxelCount;
            MiniVoxelShape sourceShape = iterateA ? shapeA : shapeB;
            MiniVoxelShape targetShape = iterateA ? shapeB : shapeA;
            MiniCollider sourceCollider = iterateA ? colliderA : colliderB;
            MiniCollider targetCollider = iterateA ? colliderB : colliderA;

            Matrix4x4 sourceLocalToWorld = BuildScaleFreeLocalToWorld(sourceCollider.transform);
            Matrix4x4 targetLocalToWorld = BuildScaleFreeLocalToWorld(targetCollider.transform);
            Matrix4x4 targetWorldToLocal = targetLocalToWorld.inverse;
            float radiusA = GetWorldVoxelRadius(colliderA);
            float radiusB = GetWorldVoxelRadius(colliderB);
            float sourceWorldRadius = GetWorldVoxelRadius(sourceCollider);
            float targetWorldRadius = GetWorldVoxelRadius(targetCollider);
            float candidateWorldRadius = sourceWorldRadius + targetWorldRadius;
            float targetMinWorldVoxelStep = Mathf.Max(Epsilon, GetMinWorldVoxelStep(targetCollider));
            int candidateRange = Mathf.Max(1, Mathf.CeilToInt(candidateWorldRadius / targetMinWorldVoxelStep) + 1);

            var sourceVoxels = sourceShape.SurfaceVoxels;
            for (int sourceIndex = 0; sourceIndex < sourceVoxels.Count; sourceIndex++)
            {
                MiniSurfaceVoxel sourceVoxel = sourceVoxels[sourceIndex];
                Vector3 sourceWorldCenter = sourceLocalToWorld.MultiplyPoint3x4(sourceVoxel.LocalCenter);
                Vector3 sourceInTargetLocal = targetWorldToLocal.MultiplyPoint3x4(sourceWorldCenter);
                MiniVoxelKey targetCenterKey = FloorToVoxelKey(sourceInTargetLocal, targetShape.VoxelSize);

                bool hasBestContact = false;
                MiniVoxelContact bestContact = default;
                for (int z = -candidateRange; z <= candidateRange; z++)
                {
                    for (int y = -candidateRange; y <= candidateRange; y++)
                    {
                        for (int x = -candidateRange; x <= candidateRange; x++)
                        {
                            MiniVoxelKey targetKey = new MiniVoxelKey(
                                targetCenterKey.X + x,
                                targetCenterKey.Y + y,
                                targetCenterKey.Z + z);
                            if (!targetShape.TryGetSurfaceVoxel(targetKey, out MiniSurfaceVoxel targetVoxel))
                            {
                                continue;
                            }

                            Vector3 targetWorldCenter = targetLocalToWorld.MultiplyPoint3x4(targetVoxel.LocalCenter);
                            Vector3 worldCenterA = iterateA ? sourceWorldCenter : targetWorldCenter;
                            Vector3 worldCenterB = iterateA ? targetWorldCenter : sourceWorldCenter;
                            float penetration = TryComputeSphereContact(
                                worldCenterA,
                                worldCenterB,
                                radiusA,
                                radiusB,
                                colliderAFrame.WorldObb.Center,
                                colliderBFrame.WorldObb.Center,
                                out Vector3 normal,
                                out Vector3 position);
                            if (penetration <= 0.0f)
                            {
                                continue;
                            }

                            MiniVoxelContactFeature featureA = iterateA ? sourceVoxel.Feature : targetVoxel.Feature;
                            MiniVoxelContactFeature featureB = iterateA ? targetVoxel.Feature : sourceVoxel.Feature;
                            if (!hasBestContact || penetration > bestContact.Penetration)
                            {
                                hasBestContact = true;
                                bestContact = new MiniVoxelContact(position, normal, penetration, featureA, featureB);
                            }
                        }
                    }
                }

                if (hasBestContact)
                {
                    contacts.Add(bestContact);
                }
            }
        }

        private MiniVoxelShape GetShape(MiniCollider collider)
        {
            int colliderId = collider.GetInstanceID();
            int signature = BuildShapeSignature(collider.Data);
            if (_shapeCacheByColliderId.TryGetValue(colliderId, out ShapeCacheEntry entry) &&
                entry.Collider == collider &&
                entry.Signature == signature)
            {
                return entry.Shape;
            }

            MiniVoxelShape shape = MiniVoxelShape.Build(collider.Data);
            _shapeCacheByColliderId[colliderId] = new ShapeCacheEntry(collider, signature, shape);
            return shape;
        }

        private static int BuildShapeSignature(MiniColliderData data)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + data.VoxelSize.GetHashCode();
                hash = (hash * 31) + data.ChunkCount;
                MiniColliderChunkData[] chunks = data.Chunks;
                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    int3 position = chunks[chunkIndex].Position;
                    hash = (hash * 31) + position.x;
                    hash = (hash * 31) + position.y;
                    hash = (hash * 31) + position.z;
                    int[] occupiedVoxelIndices = chunks[chunkIndex].OccupiedVoxelIndices;
                    hash = (hash * 31) + occupiedVoxelIndices.Length;
                    for (int voxelIndex = 0; voxelIndex < occupiedVoxelIndices.Length; voxelIndex++)
                    {
                        hash = (hash * 31) + occupiedVoxelIndices[voxelIndex];
                    }
                }

                return hash;
            }
        }

        private static float TryComputeSphereContact(
            Vector3 worldCenterA,
            Vector3 worldCenterB,
            float radiusA,
            float radiusB,
            Vector3 colliderCenterA,
            Vector3 colliderCenterB,
            out Vector3 normal,
            out Vector3 position)
        {
            Vector3 centerDelta = worldCenterB - worldCenterA;
            float distanceSq = centerDelta.sqrMagnitude;
            float radiusSum = radiusA + radiusB;
            float radiusSumSq = radiusSum * radiusSum;
            if (distanceSq >= radiusSumSq)
            {
                normal = Vector3.up;
                position = Vector3.zero;
                return 0.0f;
            }

            float distance = Mathf.Sqrt(Mathf.Max(0.0f, distanceSq));
            normal = distance > Epsilon
                ? centerDelta / distance
                : NormalizeOrFallback(colliderCenterB - colliderCenterA, Vector3.up);
            float penetration = radiusSum - distance;
            position = worldCenterA + (normal * (radiusA - (penetration * 0.5f)));
            return penetration;
        }

        private static MiniVoxelKey FloorToVoxelKey(Vector3 localPosition, float voxelSize)
        {
            float inverseVoxelSize = 1.0f / Mathf.Max(Epsilon, voxelSize);
            return new MiniVoxelKey(
                Mathf.FloorToInt(localPosition.x * inverseVoxelSize),
                Mathf.FloorToInt(localPosition.y * inverseVoxelSize),
                Mathf.FloorToInt(localPosition.z * inverseVoxelSize));
        }

        private static float GetWorldVoxelRadius(MiniCollider collider)
        {
            return collider.Data.VoxelSize * 0.5f;
        }

        private static float GetMinWorldVoxelStep(MiniCollider collider)
        {
            return collider.Data.VoxelSize;
        }

        private static Matrix4x4 BuildScaleFreeLocalToWorld(Transform transform)
        {
            return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            float magnitude = value.magnitude;
            return magnitude > Epsilon ? value / magnitude : fallback;
        }

        private readonly struct ShapeCacheEntry
        {
            public ShapeCacheEntry(MiniCollider collider, int signature, MiniVoxelShape shape)
            {
                Collider = collider;
                Signature = signature;
                Shape = shape;
            }

            public MiniCollider Collider { get; }

            public int Signature { get; }

            public MiniVoxelShape Shape { get; }
        }

        private readonly struct MiniVoxelShape
        {
            private readonly Dictionary<MiniVoxelKey, MiniSurfaceVoxel> _surfaceVoxelsByKey;

            private MiniVoxelShape(
                float voxelSize,
                List<MiniSurfaceVoxel> surfaceVoxels,
                Dictionary<MiniVoxelKey, MiniSurfaceVoxel> surfaceVoxelsByKey)
            {
                VoxelSize = voxelSize;
                SurfaceVoxels = surfaceVoxels;
                _surfaceVoxelsByKey = surfaceVoxelsByKey;
            }

            public float VoxelSize { get; }

            public List<MiniSurfaceVoxel> SurfaceVoxels { get; }

            public int SurfaceVoxelCount => SurfaceVoxels.Count;

            public bool TryGetSurfaceVoxel(MiniVoxelKey key, out MiniSurfaceVoxel voxel)
            {
                return _surfaceVoxelsByKey.TryGetValue(key, out voxel);
            }

            public static MiniVoxelShape Build(MiniColliderData data)
            {
                HashSet<MiniVoxelKey> occupiedVoxels = BuildOccupiedVoxels(data);
                List<MiniSurfaceVoxel> surfaceVoxels = new List<MiniSurfaceVoxel>();
                Dictionary<MiniVoxelKey, MiniSurfaceVoxel> surfaceVoxelsByKey = new Dictionary<MiniVoxelKey, MiniSurfaceVoxel>();

                foreach (MiniVoxelKey key in occupiedVoxels)
                {
                    MiniVoxelContactFeature feature = Classify(key, occupiedVoxels);
                    if (feature == MiniVoxelContactFeature.Inside)
                    {
                        continue;
                    }

                    MiniSurfaceVoxel voxel = new MiniSurfaceVoxel(
                        key,
                        new Vector3(
                            (key.X + 0.5f) * data.VoxelSize,
                            (key.Y + 0.5f) * data.VoxelSize,
                            (key.Z + 0.5f) * data.VoxelSize),
                        feature);
                    surfaceVoxels.Add(voxel);
                    surfaceVoxelsByKey[key] = voxel;
                }

                return new MiniVoxelShape(data.VoxelSize, surfaceVoxels, surfaceVoxelsByKey);
            }

            private static HashSet<MiniVoxelKey> BuildOccupiedVoxels(MiniColliderData data)
            {
                HashSet<MiniVoxelKey> occupiedVoxels = new HashSet<MiniVoxelKey>();
                MiniColliderChunkData[] chunks = data.Chunks;
                for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
                {
                    int3 chunkPosition = chunks[chunkIndex].Position;
                    int baseX = chunkPosition.x * MiniColliderData.ChunkVoxelDimension;
                    int baseY = chunkPosition.y * MiniColliderData.ChunkVoxelDimension;
                    int baseZ = chunkPosition.z * MiniColliderData.ChunkVoxelDimension;

                    if (chunks[chunkIndex].HasExplicitVoxelOccupancy)
                    {
                        int[] occupiedVoxelIndices = chunks[chunkIndex].OccupiedVoxelIndices;
                        for (int index = 0; index < occupiedVoxelIndices.Length; index++)
                        {
                            MiniColliderChunkData.UnflattenLocalVoxelIndex(occupiedVoxelIndices[index], out int x, out int y, out int z);
                            occupiedVoxels.Add(new MiniVoxelKey(baseX + x, baseY + y, baseZ + z));
                        }
                    }
                    else
                    {
                        for (int z = 0; z < MiniColliderData.ChunkVoxelDimension; z++)
                        {
                            for (int y = 0; y < MiniColliderData.ChunkVoxelDimension; y++)
                            {
                                for (int x = 0; x < MiniColliderData.ChunkVoxelDimension; x++)
                                {
                                    occupiedVoxels.Add(new MiniVoxelKey(baseX + x, baseY + y, baseZ + z));
                                }
                            }
                        }
                    }
                }

                return occupiedVoxels;
            }

            private static MiniVoxelContactFeature Classify(MiniVoxelKey key, HashSet<MiniVoxelKey> occupiedVoxels)
            {
                int exposedNeighborCount = 0;
                for (int offsetIndex = 0; offsetIndex < NeighborOffsets.Length; offsetIndex++)
                {
                    if (!occupiedVoxels.Contains(key + NeighborOffsets[offsetIndex]))
                    {
                        exposedNeighborCount++;
                    }
                }

                return exposedNeighborCount switch
                {
                    0 => MiniVoxelContactFeature.Inside,
                    1 => MiniVoxelContactFeature.Face,
                    2 => MiniVoxelContactFeature.Edge,
                    _ => MiniVoxelContactFeature.Corner,
                };
            }
        }

        private readonly struct MiniSurfaceVoxel
        {
            public MiniSurfaceVoxel(MiniVoxelKey key, Vector3 localCenter, MiniVoxelContactFeature feature)
            {
                Key = key;
                LocalCenter = localCenter;
                Feature = feature;
            }

            public MiniVoxelKey Key { get; }

            public Vector3 LocalCenter { get; }

            public MiniVoxelContactFeature Feature { get; }
        }

        private readonly struct MiniVoxelKey : IEquatable<MiniVoxelKey>
        {
            public MiniVoxelKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public int X { get; }

            public int Y { get; }

            public int Z { get; }

            public static MiniVoxelKey operator +(MiniVoxelKey left, MiniVoxelKey right)
            {
                return new MiniVoxelKey(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
            }

            public bool Equals(MiniVoxelKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is MiniVoxelKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + X;
                    hash = (hash * 31) + Y;
                    hash = (hash * 31) + Z;
                    return hash;
                }
            }
        }
    }
}
