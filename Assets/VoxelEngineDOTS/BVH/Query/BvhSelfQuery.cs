using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace VoxelEngineDOTS.BVH
{
    public static class BvhSelfQuery
    {
        public static void SingleThreadQuery(
            NativeArray<BvhNode> nodes,
            int rootNodeIndex,
            NativeArray<BodyProxy> sortedProxies,
            NativeList<BodyPair> outputPairs,
            NativeList<NodePair> stack,
            int maxOutputPairs,
            out QueryStats stats)
        {
            outputPairs.Clear();
            stack.Clear();
            stats = default;

            if (rootNodeIndex < 0)
            {
                return;
            }

            stack.Add(new NodePair(rootNodeIndex, rootNodeIndex));

            while (stack.Length > 0)
            {
                NodePair pair = Pop(stack);
                ProcessPair(nodes, sortedProxies, pair, stack, outputPairs, maxOutputPairs, ref stats);
            }
        }

        public static void BodyVsTreeReferenceQuery(
            NativeArray<BvhNode> nodes,
            int rootNodeIndex,
            NativeArray<BodyProxy> sortedProxies,
            int bodyCount,
            NativeList<BodyPair> outputPairs,
            NativeList<int> stack,
            int maxOutputPairs,
            out QueryStats stats)
        {
            outputPairs.Clear();
            stack.Clear();
            stats = default;

            if (rootNodeIndex < 0)
            {
                return;
            }

            for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
            {
                BodyProxy queryBody = sortedProxies[bodyIndex];
                stack.Clear();
                stack.Add(rootNodeIndex);

                while (stack.Length > 0)
                {
                    int nodeIndex = stack[stack.Length - 1];
                    stack.RemoveAtSwapBack(stack.Length - 1);
                    BvhNode node = nodes[nodeIndex];
                    stats.NodePairTests++;

                    if (!Aabb.Overlap(queryBody.Bounds, node.Bounds))
                    {
                        stats.AabbPruned++;
                        continue;
                    }

                    if (node.Leaf)
                    {
                        stats.LeafLeafChecks++;
                        for (int i = 0; i < node.BodyCount; i++)
                        {
                            BodyProxy other = sortedProxies[node.FirstBodyIndex + i];
                            if (other.BodyId <= queryBody.BodyId)
                            {
                                continue;
                            }

                            stats.BodyAabbTests++;
                            if (Aabb.Overlap(queryBody.Bounds, other.Bounds))
                            {
                                EmitPair(outputPairs, new BodyPair(queryBody.BodyId, other.BodyId), maxOutputPairs, ref stats);
                            }
                        }
                    }
                    else
                    {
                        stack.Add(node.Left);
                        stack.Add(node.Right);
                    }
                }
            }
        }

        public static void GenerateSeedPairs(
            NativeArray<BvhNode> nodes,
            int rootNodeIndex,
            int targetSeedCount,
            NativeList<NodePair> seeds,
            NativeList<NodePair> stack)
        {
            seeds.Clear();
            stack.Clear();

            if (rootNodeIndex < 0)
            {
                return;
            }

            int target = targetSeedCount <= 0 ? 1 : targetSeedCount;
            stack.Add(new NodePair(rootNodeIndex, rootNodeIndex));

            while (stack.Length > 0 && seeds.Length < target)
            {
                NodePair pair = Pop(stack);

                if (CanPrune(nodes, pair))
                {
                    continue;
                }

                int pendingWork = seeds.Length + stack.Length;
                if (CanExpand(nodes, pair) && pendingWork + ExpandedPairCount(nodes, pair) < target)
                {
                    Expand(nodes, pair, stack);
                }
                else
                {
                    seeds.Add(pair);
                }
            }

            while (stack.Length > 0)
            {
                NodePair pair = Pop(stack);
                if (!CanPrune(nodes, pair))
                {
                    seeds.Add(pair);
                }
            }
        }

        private static void ProcessPair(
            NativeArray<BvhNode> nodes,
            NativeArray<BodyProxy> sortedProxies,
            NodePair pair,
            NativeList<NodePair> stack,
            NativeList<BodyPair> outputPairs,
            int maxOutputPairs,
            ref QueryStats stats)
        {
            stats.NodePairTests++;
            BvhNode a = nodes[pair.A];

            if (pair.A == pair.B)
            {
                if (a.Leaf)
                {
                    CheckLeafInternal(sortedProxies, a, outputPairs, maxOutputPairs, ref stats);
                    return;
                }

                stack.Add(new NodePair(a.Left, a.Left));
                stack.Add(new NodePair(a.Right, a.Right));
                stack.Add(new NodePair(a.Left, a.Right));
                return;
            }

            BvhNode b = nodes[pair.B];
            if (!Aabb.Overlap(a.Bounds, b.Bounds))
            {
                stats.AabbPruned++;
                return;
            }

            if (a.Leaf && b.Leaf)
            {
                CheckLeafVsLeaf(sortedProxies, a, b, outputPairs, maxOutputPairs, ref stats);
                return;
            }

            if (a.Leaf)
            {
                stack.Add(new NodePair(pair.A, b.Left));
                stack.Add(new NodePair(pair.A, b.Right));
                return;
            }

            if (b.Leaf)
            {
                stack.Add(new NodePair(a.Left, pair.B));
                stack.Add(new NodePair(a.Right, pair.B));
                return;
            }

            if (a.Bounds.SurfaceArea() > b.Bounds.SurfaceArea())
            {
                stack.Add(new NodePair(a.Left, pair.B));
                stack.Add(new NodePair(a.Right, pair.B));
            }
            else
            {
                stack.Add(new NodePair(pair.A, b.Left));
                stack.Add(new NodePair(pair.A, b.Right));
            }
        }

        private static void CheckLeafInternal(
            NativeArray<BodyProxy> sortedProxies,
            BvhNode leaf,
            NativeList<BodyPair> outputPairs,
            int maxOutputPairs,
            ref QueryStats stats)
        {
            stats.LeafInternalChecks++;
            for (int i = 0; i < leaf.BodyCount; i++)
            {
                BodyProxy a = sortedProxies[leaf.FirstBodyIndex + i];
                for (int j = i + 1; j < leaf.BodyCount; j++)
                {
                    BodyProxy b = sortedProxies[leaf.FirstBodyIndex + j];
                    stats.BodyAabbTests++;
                    if (Aabb.Overlap(a.Bounds, b.Bounds))
                    {
                        EmitPair(outputPairs, new BodyPair(a.BodyId, b.BodyId), maxOutputPairs, ref stats);
                    }
                }
            }
        }

        private static void CheckLeafVsLeaf(
            NativeArray<BodyProxy> sortedProxies,
            BvhNode leafA,
            BvhNode leafB,
            NativeList<BodyPair> outputPairs,
            int maxOutputPairs,
            ref QueryStats stats)
        {
            stats.LeafLeafChecks++;
            for (int i = 0; i < leafA.BodyCount; i++)
            {
                BodyProxy a = sortedProxies[leafA.FirstBodyIndex + i];
                for (int j = 0; j < leafB.BodyCount; j++)
                {
                    BodyProxy b = sortedProxies[leafB.FirstBodyIndex + j];
                    if (a.BodyId == b.BodyId)
                    {
                        continue;
                    }

                    stats.BodyAabbTests++;
                    if (Aabb.Overlap(a.Bounds, b.Bounds))
                    {
                        EmitPair(outputPairs, new BodyPair(a.BodyId, b.BodyId), maxOutputPairs, ref stats);
                    }
                }
            }
        }

        private static void EmitPair(
            NativeList<BodyPair> outputPairs,
            BodyPair pair,
            int maxOutputPairs,
            ref QueryStats stats)
        {
            int outputLimit = maxOutputPairs <= 0 ? int.MaxValue : maxOutputPairs;
            if (outputPairs.Length < outputLimit)
            {
                outputPairs.Add(pair);
                stats.EmittedPairs++;
            }
            else
            {
                stats.OutputOverflow++;
            }
        }

        private static bool CanPrune(NativeArray<BvhNode> nodes, NodePair pair)
        {
            return pair.A != pair.B && !Aabb.Overlap(nodes[pair.A].Bounds, nodes[pair.B].Bounds);
        }

        private static bool CanExpand(NativeArray<BvhNode> nodes, NodePair pair)
        {
            BvhNode a = nodes[pair.A];
            if (pair.A == pair.B)
            {
                return !a.Leaf;
            }

            BvhNode b = nodes[pair.B];
            return !a.Leaf || !b.Leaf;
        }

        private static int ExpandedPairCount(NativeArray<BvhNode> nodes, NodePair pair)
        {
            if (pair.A == pair.B)
            {
                return 3;
            }

            BvhNode a = nodes[pair.A];
            BvhNode b = nodes[pair.B];
            return a.Leaf && b.Leaf ? 0 : 2;
        }

        private static void Expand(NativeArray<BvhNode> nodes, NodePair pair, NativeList<NodePair> stack)
        {
            BvhNode a = nodes[pair.A];

            if (pair.A == pair.B)
            {
                stack.Add(new NodePair(a.Left, a.Left));
                stack.Add(new NodePair(a.Right, a.Right));
                stack.Add(new NodePair(a.Left, a.Right));
                return;
            }

            BvhNode b = nodes[pair.B];
            if (a.Leaf)
            {
                stack.Add(new NodePair(pair.A, b.Left));
                stack.Add(new NodePair(pair.A, b.Right));
                return;
            }

            if (b.Leaf)
            {
                stack.Add(new NodePair(a.Left, pair.B));
                stack.Add(new NodePair(a.Right, pair.B));
                return;
            }

            if (a.Bounds.SurfaceArea() > b.Bounds.SurfaceArea())
            {
                stack.Add(new NodePair(a.Left, pair.B));
                stack.Add(new NodePair(a.Right, pair.B));
            }
            else
            {
                stack.Add(new NodePair(pair.A, b.Left));
                stack.Add(new NodePair(pair.A, b.Right));
            }
        }

        private static NodePair Pop(NativeList<NodePair> stack)
        {
            NodePair pair = stack[stack.Length - 1];
            stack.RemoveAtSwapBack(stack.Length - 1);
            return pair;
        }
    }

    [BurstCompile]
    public struct TraverseSeedPairsJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<BvhNode> Nodes;

        [ReadOnly]
        public NativeArray<BodyProxy> SortedProxies;

        [ReadOnly]
        public NativeArray<NodePair> SeedPairs;

        public NativeStream.Writer PairWriter;
        public NativeArray<QueryStats> PerSeedStats;

        [NativeDisableParallelForRestriction]
        public NativeArray<NodePair> StackBuffer;

        public int LocalStackCapacity;
        public int MaxPairsPerSeed;

        public void Execute(int seedIndex)
        {
            NativeStream.Writer writer = PairWriter;
            writer.BeginForEachIndex(seedIndex);

            QueryStats stats = default;
            int stackStart = seedIndex * LocalStackCapacity;
            int stackCount = 0;
            Push(SeedPairs[seedIndex], ref stackCount, stackStart, ref stats);

            while (stackCount > 0)
            {
                NodePair pair = Pop(ref stackCount, stackStart);
                ProcessPair(pair, ref stackCount, stackStart, ref writer, ref stats);
            }

            PerSeedStats[seedIndex] = stats;
            writer.EndForEachIndex();
        }

        private void ProcessPair(
            NodePair pair,
            ref int stackCount,
            int stackStart,
            ref NativeStream.Writer writer,
            ref QueryStats stats)
        {
            stats.NodePairTests++;
            BvhNode a = Nodes[pair.A];

            if (pair.A == pair.B)
            {
                if (a.Leaf)
                {
                    CheckLeafInternal(a, ref writer, ref stats);
                    return;
                }

                Push(new NodePair(a.Left, a.Left), ref stackCount, stackStart, ref stats);
                Push(new NodePair(a.Right, a.Right), ref stackCount, stackStart, ref stats);
                Push(new NodePair(a.Left, a.Right), ref stackCount, stackStart, ref stats);
                return;
            }

            BvhNode b = Nodes[pair.B];
            if (!Aabb.Overlap(a.Bounds, b.Bounds))
            {
                stats.AabbPruned++;
                return;
            }

            if (a.Leaf && b.Leaf)
            {
                CheckLeafVsLeaf(a, b, ref writer, ref stats);
                return;
            }

            if (a.Leaf)
            {
                Push(new NodePair(pair.A, b.Left), ref stackCount, stackStart, ref stats);
                Push(new NodePair(pair.A, b.Right), ref stackCount, stackStart, ref stats);
                return;
            }

            if (b.Leaf)
            {
                Push(new NodePair(a.Left, pair.B), ref stackCount, stackStart, ref stats);
                Push(new NodePair(a.Right, pair.B), ref stackCount, stackStart, ref stats);
                return;
            }

            if (a.Bounds.SurfaceArea() > b.Bounds.SurfaceArea())
            {
                Push(new NodePair(a.Left, pair.B), ref stackCount, stackStart, ref stats);
                Push(new NodePair(a.Right, pair.B), ref stackCount, stackStart, ref stats);
            }
            else
            {
                Push(new NodePair(pair.A, b.Left), ref stackCount, stackStart, ref stats);
                Push(new NodePair(pair.A, b.Right), ref stackCount, stackStart, ref stats);
            }
        }

        private void CheckLeafInternal(BvhNode leaf, ref NativeStream.Writer writer, ref QueryStats stats)
        {
            stats.LeafInternalChecks++;
            for (int i = 0; i < leaf.BodyCount; i++)
            {
                BodyProxy a = SortedProxies[leaf.FirstBodyIndex + i];
                for (int j = i + 1; j < leaf.BodyCount; j++)
                {
                    BodyProxy b = SortedProxies[leaf.FirstBodyIndex + j];
                    stats.BodyAabbTests++;
                    if (Aabb.Overlap(a.Bounds, b.Bounds))
                    {
                        EmitPair(ref writer, new BodyPair(a.BodyId, b.BodyId), ref stats);
                    }
                }
            }
        }

        private void CheckLeafVsLeaf(BvhNode leafA, BvhNode leafB, ref NativeStream.Writer writer, ref QueryStats stats)
        {
            stats.LeafLeafChecks++;
            for (int i = 0; i < leafA.BodyCount; i++)
            {
                BodyProxy a = SortedProxies[leafA.FirstBodyIndex + i];
                for (int j = 0; j < leafB.BodyCount; j++)
                {
                    BodyProxy b = SortedProxies[leafB.FirstBodyIndex + j];
                    if (a.BodyId == b.BodyId)
                    {
                        continue;
                    }

                    stats.BodyAabbTests++;
                    if (Aabb.Overlap(a.Bounds, b.Bounds))
                    {
                        EmitPair(ref writer, new BodyPair(a.BodyId, b.BodyId), ref stats);
                    }
                }
            }
        }

        private void EmitPair(ref NativeStream.Writer writer, BodyPair pair, ref QueryStats stats)
        {
            if (MaxPairsPerSeed <= 0 || stats.EmittedPairs < MaxPairsPerSeed)
            {
                writer.Write(pair);
                stats.EmittedPairs++;
            }
            else
            {
                stats.OutputOverflow++;
            }
        }

        private void Push(NodePair pair, ref int stackCount, int stackStart, ref QueryStats stats)
        {
            if (stackCount >= LocalStackCapacity)
            {
                stats.StackOverflow++;
                return;
            }

            StackBuffer[stackStart + stackCount] = pair;
            stackCount++;
        }

        private NodePair Pop(ref int stackCount, int stackStart)
        {
            stackCount--;
            return StackBuffer[stackStart + stackCount];
        }
    }
}
