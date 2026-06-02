using System;
using Unity.Mathematics;

namespace VoxelEngineDOTS.BVH
{
    public struct Aabb
    {
        public float3 Min;
        public float3 Max;

        public readonly float3 Center => (Min + Max) * 0.5f;

        public readonly float3 Extents => (Max - Min) * 0.5f;

        public static Aabb Union(Aabb a, Aabb b)
        {
            return new Aabb
            {
                Min = math.min(a.Min, b.Min),
                Max = math.max(a.Max, b.Max)
            };
        }

        public static bool Overlap(Aabb a, Aabb b)
        {
            return a.Min.x <= b.Max.x && a.Max.x >= b.Min.x &&
                   a.Min.y <= b.Max.y && a.Max.y >= b.Min.y &&
                   a.Min.z <= b.Max.z && a.Max.z >= b.Min.z;
        }

        public readonly bool Contains(Aabb other)
        {
            return Min.x <= other.Min.x && Max.x >= other.Max.x &&
                   Min.y <= other.Min.y && Max.y >= other.Max.y &&
                   Min.z <= other.Min.z && Max.z >= other.Max.z;
        }

        public readonly float SurfaceArea()
        {
            float3 d = math.max(Max - Min, 0.0f);
            return 2.0f * (d.x * d.y + d.x * d.z + d.y * d.z);
        }
    }

    public struct BodyState
    {
        public int BodyId;
        public float3 Position;
        public float3 HalfSize;
        public float3 Velocity;
    }

    public struct BodyProxy : IComparable<BodyProxy>
    {
        public int BodyId;
        public Aabb Bounds;
        public float3 Center;
        public uint MortonCode;

        public readonly int CompareTo(BodyProxy other)
        {
            int mortonCompare = MortonCode.CompareTo(other.MortonCode);
            return mortonCompare != 0 ? mortonCompare : BodyId.CompareTo(other.BodyId);
        }
    }

    public struct BvhNode
    {
        public Aabb Bounds;
        public int Left;
        public int Right;
        public int FirstBodyIndex;
        public int BodyCount;
        public byte IsLeaf;

        public readonly bool Leaf => IsLeaf != 0;
    }

    public struct NodePair
    {
        public int A;
        public int B;

        public NodePair(int a, int b)
        {
            A = a;
            B = b;
        }
    }

    public struct BodyPair : IComparable<BodyPair>
    {
        public int A;
        public int B;

        public BodyPair(int a, int b)
        {
            if (a < b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public readonly int CompareTo(BodyPair other)
        {
            int aCompare = A.CompareTo(other.A);
            return aCompare != 0 ? aCompare : B.CompareTo(other.B);
        }
    }

    public enum BodyDistribution
    {
        Uniform,
        Clustered,
        DenseCenter,
        Line,
        Plane,
        LargeBodiesStress
    }

    public enum BvhBuildMode
    {
        MortonPairingBuild,
        MortonMiddleSplitReference
    }

    public enum BvhQueryMode
    {
        NaiveAllPairs,
        BodyVsTreeReference,
        SingleThreadNodePairSelfQuery,
        ParallelNodePairSelfQuery
    }

    public struct QueryStats
    {
        public int NodePairTests;
        public int AabbPruned;
        public int LeafInternalChecks;
        public int LeafLeafChecks;
        public int BodyAabbTests;
        public int EmittedPairs;
        public int StackOverflow;
        public int OutputOverflow;

        public void Add(QueryStats other)
        {
            NodePairTests += other.NodePairTests;
            AabbPruned += other.AabbPruned;
            LeafInternalChecks += other.LeafInternalChecks;
            LeafLeafChecks += other.LeafLeafChecks;
            BodyAabbTests += other.BodyAabbTests;
            EmittedPairs += other.EmittedPairs;
            StackOverflow += other.StackOverflow;
            OutputOverflow += other.OutputOverflow;
        }
    }

    public struct FrameStats
    {
        public int BodyCount;
        public int LeafCount;
        public int NodeCount;
        public int SeedCount;
        public int BodyPairCount;
        public int NodePairTests;
        public int AabbPruned;
        public int LeafInternalChecks;
        public int LeafLeafChecks;
        public int BodyAabbTests;
        public int StackOverflowCount;
        public int OutputOverflowCount;
        public double SimulateMs;
        public double ProxyMs;
        public double SortMs;
        public double BuildMs;
        public double SeedMs;
        public double QueryMs;
        public double ReadbackMs;
        public double BuildAndFindPairsMs;
        public double ValidateMs;
        public double RenderMs;
        public double TotalMs;
        public float Fps;
    }
}
