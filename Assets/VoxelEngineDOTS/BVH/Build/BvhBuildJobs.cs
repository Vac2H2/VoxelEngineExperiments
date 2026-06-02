using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineDOTS.BVH
{
    [BurstCompile]
    public struct ComputeBodyProxiesJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<BodyState> Bodies;

        public NativeArray<BodyProxy> Proxies;
        public float3 WorldMin;
        public float3 WorldMax;
        public int MortonBitsPerAxis;

        public void Execute(int index)
        {
            BodyState body = Bodies[index];
            Aabb bounds = new Aabb
            {
                Min = body.Position - body.HalfSize,
                Max = body.Position + body.HalfSize
            };

            Proxies[index] = new BodyProxy
            {
                BodyId = body.BodyId,
                Bounds = bounds,
                Center = body.Position,
                MortonCode = Morton3D.Encode(body.Position, WorldMin, WorldMax, MortonBitsPerAxis)
            };
        }
    }

    [BurstCompile]
    public struct CreateLeafNodesJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<BodyProxy> SortedProxies;

        public NativeArray<BvhNode> Nodes;
        public NativeArray<int> CurrentLevelNodeIndices;
        public int LeafSize;
        public int BodyCount;

        public void Execute(int leafIndex)
        {
            BvhBuildUtility.CreateLeafNode(
                SortedProxies,
                Nodes,
                CurrentLevelNodeIndices,
                leafIndex,
                LeafSize,
                BodyCount);
        }
    }

    [BurstCompile]
    public struct BuildParentLevelJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> CurrentLevelNodeIndices;

        public NativeArray<int> NextLevelNodeIndices;

        [NativeDisableParallelForRestriction]
        public NativeArray<BvhNode> Nodes;

        public int NextNodeStart;

        public void Execute(int pairIndex)
        {
            int leftNode = CurrentLevelNodeIndices[pairIndex * 2];
            int rightNode = CurrentLevelNodeIndices[pairIndex * 2 + 1];
            int parentNode = NextNodeStart + pairIndex;

            Nodes[parentNode] = new BvhNode
            {
                Bounds = Aabb.Union(Nodes[leftNode].Bounds, Nodes[rightNode].Bounds),
                Left = leftNode,
                Right = rightNode,
                FirstBodyIndex = -1,
                BodyCount = 0,
                IsLeaf = 0
            };

            NextLevelNodeIndices[pairIndex] = parentNode;
        }
    }

    public static class BvhBuildUtility
    {
        public static void ComputeProxy(
            NativeArray<BodyState> bodies,
            NativeArray<BodyProxy> proxies,
            int index,
            float3 worldMin,
            float3 worldMax,
            int mortonBitsPerAxis)
        {
            BodyState body = bodies[index];
            Aabb bounds = new Aabb
            {
                Min = body.Position - body.HalfSize,
                Max = body.Position + body.HalfSize
            };

            proxies[index] = new BodyProxy
            {
                BodyId = body.BodyId,
                Bounds = bounds,
                Center = body.Position,
                MortonCode = Morton3D.Encode(body.Position, worldMin, worldMax, mortonBitsPerAxis)
            };
        }

        public static void CreateLeafNode(
            NativeArray<BodyProxy> sortedProxies,
            NativeArray<BvhNode> nodes,
            NativeArray<int> currentLevelNodeIndices,
            int leafIndex,
            int leafSize,
            int bodyCount)
        {
            int first = leafIndex * leafSize;
            int count = math.min(leafSize, bodyCount - first);
            Aabb bounds = sortedProxies[first].Bounds;

            for (int i = 1; i < count; i++)
            {
                bounds = Aabb.Union(bounds, sortedProxies[first + i].Bounds);
            }

            nodes[leafIndex] = new BvhNode
            {
                Bounds = bounds,
                Left = -1,
                Right = -1,
                FirstBodyIndex = first,
                BodyCount = count,
                IsLeaf = 1
            };

            currentLevelNodeIndices[leafIndex] = leafIndex;
        }

        public static void CreateParentNode(
            NativeArray<int> currentLevelNodeIndices,
            NativeArray<int> nextLevelNodeIndices,
            NativeArray<BvhNode> nodes,
            int pairIndex,
            int nextNodeStart)
        {
            int leftNode = currentLevelNodeIndices[pairIndex * 2];
            int rightNode = currentLevelNodeIndices[pairIndex * 2 + 1];
            int parentNode = nextNodeStart + pairIndex;

            nodes[parentNode] = new BvhNode
            {
                Bounds = Aabb.Union(nodes[leftNode].Bounds, nodes[rightNode].Bounds),
                Left = leftNode,
                Right = rightNode,
                FirstBodyIndex = -1,
                BodyCount = 0,
                IsLeaf = 0
            };

            nextLevelNodeIndices[pairIndex] = parentNode;
        }
    }

    public static class TlasBuilder
    {
        public static void Build(
            NativeArray<BodyProxy> sortedProxies,
            int bodyCount,
            int leafSize,
            NativeArray<BvhNode> nodes,
            NativeArray<int> levelA,
            NativeArray<int> levelB,
            bool useJobs,
            int innerLoopBatchCount,
            out int rootNodeIndex,
            out int leafCount,
            out int nodeCount)
        {
            rootNodeIndex = -1;
            leafCount = bodyCount <= 0 ? 0 : (bodyCount + math.max(1, leafSize) - 1) / math.max(1, leafSize);
            nodeCount = 0;

            if (bodyCount <= 0 || leafCount <= 0)
            {
                return;
            }

            int batchCount = math.max(1, innerLoopBatchCount);

            if (useJobs)
            {
                new CreateLeafNodesJob
                {
                    SortedProxies = sortedProxies,
                    Nodes = nodes,
                    CurrentLevelNodeIndices = levelA,
                    LeafSize = math.max(1, leafSize),
                    BodyCount = bodyCount
                }.Schedule(leafCount, batchCount).Complete();
            }
            else
            {
                for (int leafIndex = 0; leafIndex < leafCount; leafIndex++)
                {
                    BvhBuildUtility.CreateLeafNode(
                        sortedProxies,
                        nodes,
                        levelA,
                        leafIndex,
                        math.max(1, leafSize),
                        bodyCount);
                }
            }

            NativeArray<int> currentLevelNodeIndices = levelA;
            NativeArray<int> nextLevelNodeIndices = levelB;
            int currentCount = leafCount;
            int nextNodeStart = leafCount;

            while (currentCount > 1)
            {
                int pairCount = currentCount / 2;

                if (useJobs)
                {
                    new BuildParentLevelJob
                    {
                        CurrentLevelNodeIndices = currentLevelNodeIndices,
                        NextLevelNodeIndices = nextLevelNodeIndices,
                        Nodes = nodes,
                        NextNodeStart = nextNodeStart
                    }.Schedule(pairCount, batchCount).Complete();
                }
                else
                {
                    for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
                    {
                        BvhBuildUtility.CreateParentNode(
                            currentLevelNodeIndices,
                            nextLevelNodeIndices,
                            nodes,
                            pairIndex,
                            nextNodeStart);
                    }
                }

                int nextCount = pairCount;
                if ((currentCount & 1) != 0)
                {
                    nextLevelNodeIndices[pairCount] = currentLevelNodeIndices[currentCount - 1];
                    nextCount++;
                }

                nextNodeStart += pairCount;
                NativeArray<int> swap = currentLevelNodeIndices;
                currentLevelNodeIndices = nextLevelNodeIndices;
                nextLevelNodeIndices = swap;
                currentCount = nextCount;
            }

            rootNodeIndex = currentLevelNodeIndices[0];
            nodeCount = nextNodeStart;
        }
    }
}
