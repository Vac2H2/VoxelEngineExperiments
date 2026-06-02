using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngineModules.Shape
{
    [BurstCompile]
    public struct ShapeFragmentUnionJob : IJobParallelFor
    {
        public const int ChunksPerShape = ShapeDataContainer.ChunksPerShape;
        public const int MaxFragmentsPerChunk = ShapeChunkFragmentJob.MaxGeneratedChunksPerChunk;
        public const int MaxFragmentNodesPerShape = ChunksPerShape * MaxFragmentsPerChunk;
        public const int PositiveDirectionCount = ShapeChunkCheckMaskJob.PositiveDirectionCount;
        public const int InvalidLocalShapeId = -1;

        [ReadOnly]
        public NativeArray<byte> FragmentCounts;

        [ReadOnly]
        public NativeArray<uint> FragmentConnectionMasks;

        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<int> FragmentLocalShapeIds;

        [WriteOnly]
        public NativeArray<int> LocalShapeCounts;

        public void Execute(int shapeListIndex)
        {
            int outputBase = shapeListIndex * MaxFragmentNodesPerShape;
            UnionFind32 unionFind = default;

            unionFind.Initialize();
            InitializeSets(shapeListIndex, outputBase, ref unionFind);
            ApplyConnections(shapeListIndex, ref unionFind);
            WriteLocalShapeIds(outputBase, ref unionFind, out int localShapeCount);

            LocalShapeCounts[shapeListIndex] = localShapeCount;
        }

        private void InitializeSets(
            int shapeListIndex,
            int outputBase,
            ref UnionFind32 unionFind)
        {
            for (int node = 0; node < MaxFragmentNodesPerShape; node++)
            {
                if (!IsValidNode(shapeListIndex, node))
                {
                    FragmentLocalShapeIds[outputBase + node] = InvalidLocalShapeId;
                    continue;
                }

                unionFind.MakeSet(node);
            }
        }

        private void ApplyConnections(
            int shapeListIndex,
            ref UnionFind32 unionFind)
        {
            int connectionBase = shapeListIndex * MaxFragmentNodesPerShape * PositiveDirectionCount;

            for (int nodeA = 0; nodeA < MaxFragmentNodesPerShape; nodeA++)
            {
                if (!unionFind.IsValid(nodeA))
                {
                    continue;
                }

                int nodeConnectionBase = connectionBase + nodeA * PositiveDirectionCount;
                for (int direction = 0; direction < PositiveDirectionCount; direction++)
                {
                    UnionConnectedNodes(
                        nodeA,
                        FragmentConnectionMasks[nodeConnectionBase + direction],
                        ref unionFind);
                }
            }
        }

        private void UnionConnectedNodes(
            int nodeA,
            uint connectionMask,
            ref UnionFind32 unionFind)
        {
            while (connectionMask != 0u)
            {
                int nodeB = math.tzcnt(connectionMask);
                connectionMask &= connectionMask - 1u;

                if (unionFind.IsValid(nodeB))
                {
                    unionFind.Union(nodeA, nodeB);
                }
            }
        }

        private void WriteLocalShapeIds(
            int outputBase,
            ref UnionFind32 unionFind,
            out int localShapeCount)
        {
            IntSlots32 rootLocalShapeIds = default;
            rootLocalShapeIds.Fill(InvalidLocalShapeId);
            localShapeCount = 0;

            for (int node = 0; node < MaxFragmentNodesPerShape; node++)
            {
                if (!unionFind.IsValid(node))
                {
                    FragmentLocalShapeIds[outputBase + node] = InvalidLocalShapeId;
                    continue;
                }

                int root = unionFind.Find(node);
                int localShapeId = rootLocalShapeIds.Get(root);

                if (localShapeId == InvalidLocalShapeId)
                {
                    localShapeId = localShapeCount++;
                    rootLocalShapeIds.Set(root, localShapeId);
                }

                FragmentLocalShapeIds[outputBase + node] = localShapeId;
            }
        }

        private bool IsValidNode(int shapeListIndex, int node)
        {
            int chunkSlot = node / MaxFragmentsPerChunk;
            int fragmentSlot = node - chunkSlot * MaxFragmentsPerChunk;
            int fragmentCount = GetFragmentCount(shapeListIndex, chunkSlot);
            return fragmentSlot < fragmentCount;
        }

        private int GetFragmentCount(int shapeListIndex, int chunkSlot)
        {
            int shapeChunkIndex = shapeListIndex * ChunksPerShape + chunkSlot;
            return math.min(FragmentCounts[shapeChunkIndex], MaxFragmentsPerChunk);
        }


        private struct UnionFind32
        {
            private IntSlots32 _parents;

            public void Initialize()
            {
                _parents.Fill(InvalidLocalShapeId);
            }

            public bool IsValid(int node)
            {
                return _parents.Get(node) != InvalidLocalShapeId;
            }

            public void MakeSet(int node)
            {
                _parents.Set(node, node);
            }

            public int Find(int node)
            {
                int root = node;

                while (_parents.Get(root) != root)
                {
                    root = _parents.Get(root);
                }

                while (_parents.Get(node) != node)
                {
                    int parent = _parents.Get(node);
                    _parents.Set(node, root);
                    node = parent;
                }

                return root;
            }

            public void Union(int nodeA, int nodeB)
            {
                int rootA = Find(nodeA);
                int rootB = Find(nodeB);

                if (rootA == rootB)
                {
                    return;
                }

                if (rootB < rootA)
                {
                    int temp = rootA;
                    rootA = rootB;
                    rootB = temp;
                }

                _parents.Set(rootB, rootA);
            }
        }


        private struct IntSlots32
        {
            private int _value0;
            private int _value1;
            private int _value2;
            private int _value3;
            private int _value4;
            private int _value5;
            private int _value6;
            private int _value7;
            private int _value8;
            private int _value9;
            private int _value10;
            private int _value11;
            private int _value12;
            private int _value13;
            private int _value14;
            private int _value15;
            private int _value16;
            private int _value17;
            private int _value18;
            private int _value19;
            private int _value20;
            private int _value21;
            private int _value22;
            private int _value23;
            private int _value24;
            private int _value25;
            private int _value26;
            private int _value27;
            private int _value28;
            private int _value29;
            private int _value30;
            private int _value31;

            public void Fill(int value)
            {
                _value0 = value;
                _value1 = value;
                _value2 = value;
                _value3 = value;
                _value4 = value;
                _value5 = value;
                _value6 = value;
                _value7 = value;
                _value8 = value;
                _value9 = value;
                _value10 = value;
                _value11 = value;
                _value12 = value;
                _value13 = value;
                _value14 = value;
                _value15 = value;
                _value16 = value;
                _value17 = value;
                _value18 = value;
                _value19 = value;
                _value20 = value;
                _value21 = value;
                _value22 = value;
                _value23 = value;
                _value24 = value;
                _value25 = value;
                _value26 = value;
                _value27 = value;
                _value28 = value;
                _value29 = value;
                _value30 = value;
                _value31 = value;
            }

            public int Get(int index)
            {
                switch (index)
                {
                    case 0:
                        return _value0;
                    case 1:
                        return _value1;
                    case 2:
                        return _value2;
                    case 3:
                        return _value3;
                    case 4:
                        return _value4;
                    case 5:
                        return _value5;
                    case 6:
                        return _value6;
                    case 7:
                        return _value7;
                    case 8:
                        return _value8;
                    case 9:
                        return _value9;
                    case 10:
                        return _value10;
                    case 11:
                        return _value11;
                    case 12:
                        return _value12;
                    case 13:
                        return _value13;
                    case 14:
                        return _value14;
                    case 15:
                        return _value15;
                    case 16:
                        return _value16;
                    case 17:
                        return _value17;
                    case 18:
                        return _value18;
                    case 19:
                        return _value19;
                    case 20:
                        return _value20;
                    case 21:
                        return _value21;
                    case 22:
                        return _value22;
                    case 23:
                        return _value23;
                    case 24:
                        return _value24;
                    case 25:
                        return _value25;
                    case 26:
                        return _value26;
                    case 27:
                        return _value27;
                    case 28:
                        return _value28;
                    case 29:
                        return _value29;
                    case 30:
                        return _value30;
                    default:
                        return _value31;
                }
            }

            public void Set(int index, int value)
            {
                switch (index)
                {
                    case 0:
                        _value0 = value;
                        break;
                    case 1:
                        _value1 = value;
                        break;
                    case 2:
                        _value2 = value;
                        break;
                    case 3:
                        _value3 = value;
                        break;
                    case 4:
                        _value4 = value;
                        break;
                    case 5:
                        _value5 = value;
                        break;
                    case 6:
                        _value6 = value;
                        break;
                    case 7:
                        _value7 = value;
                        break;
                    case 8:
                        _value8 = value;
                        break;
                    case 9:
                        _value9 = value;
                        break;
                    case 10:
                        _value10 = value;
                        break;
                    case 11:
                        _value11 = value;
                        break;
                    case 12:
                        _value12 = value;
                        break;
                    case 13:
                        _value13 = value;
                        break;
                    case 14:
                        _value14 = value;
                        break;
                    case 15:
                        _value15 = value;
                        break;
                    case 16:
                        _value16 = value;
                        break;
                    case 17:
                        _value17 = value;
                        break;
                    case 18:
                        _value18 = value;
                        break;
                    case 19:
                        _value19 = value;
                        break;
                    case 20:
                        _value20 = value;
                        break;
                    case 21:
                        _value21 = value;
                        break;
                    case 22:
                        _value22 = value;
                        break;
                    case 23:
                        _value23 = value;
                        break;
                    case 24:
                        _value24 = value;
                        break;
                    case 25:
                        _value25 = value;
                        break;
                    case 26:
                        _value26 = value;
                        break;
                    case 27:
                        _value27 = value;
                        break;
                    case 28:
                        _value28 = value;
                        break;
                    case 29:
                        _value29 = value;
                        break;
                    case 30:
                        _value30 = value;
                        break;
                    default:
                        _value31 = value;
                        break;
                }
            }
        }
    }
}
