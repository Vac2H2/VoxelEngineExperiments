using System;
using System.Collections.Generic;
using Unity.Mathematics;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Importer
{
    internal readonly struct VoxImportOptions
    {
        public VoxImportOptions(int maxAabbsPerChunk)
        {
            MaxAabbsPerChunk = Math.Max(1, Math.Min(maxAabbsPerChunk, VoxelVolume.MaxAabbsPerChunk));
        }

        public int MaxAabbsPerChunk { get; }

        public static VoxImportOptions Default => new VoxImportOptions(1);
    }

    internal static class VoxelChunkAabbOptimizer
    {
        private static readonly AxisOrder[] AxisOrders =
        {
            new AxisOrder(0, 1, 2),
            new AxisOrder(0, 2, 1),
            new AxisOrder(1, 0, 2),
            new AxisOrder(1, 2, 0),
            new AxisOrder(2, 0, 1),
            new AxisOrder(2, 1, 0),
        };

        public static void BuildChunkAabbs(
            ChunkBuildState chunk,
            int maxAabbCount,
            List<VoxelChunkAabb> destination,
            out bool usedFallbackBounds)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            usedFallbackBounds = false;

            if (!chunk.HasVoxels)
            {
                return;
            }

            int clampedMaxAabbCount = Math.Max(1, Math.Min(maxAabbCount, VoxelVolume.MaxAabbsPerChunk));
            if (clampedMaxAabbCount == 1)
            {
                destination.Add(VoxelChunkAabb.CreateActive(chunk.Min, chunk.MaxExclusive));
                return;
            }

            bool[] remaining = new bool[VoxelVolume.VoxelsPerChunk];
            int remainingVoxelCount = CopyOccupiedVoxels(chunk, remaining);
            int strictBudget = clampedMaxAabbCount - 1;

            while (remainingVoxelCount > 0 && destination.Count < strictBudget)
            {
                CuboidCandidate bestCandidate = FindBestStrictCandidate(remaining);
                destination.Add(VoxelChunkAabb.CreateActive(bestCandidate.Min, bestCandidate.MaxExclusive));
                remainingVoxelCount -= ClearCuboid(remaining, bestCandidate.Min, bestCandidate.MaxExclusive);
            }

            if (remainingVoxelCount > 0)
            {
                usedFallbackBounds = true;
                ComputeRemainingBounds(remaining, out int3 remainingMin, out int3 remainingMaxExclusive);
                destination.Add(VoxelChunkAabb.CreateActive(remainingMin, remainingMaxExclusive));
            }
        }

        private static int CopyOccupiedVoxels(ChunkBuildState chunk, bool[] destination)
        {
            int occupiedVoxelCount = 0;
            for (int z = 0; z < VoxelVolume.ChunkDimension; z++)
            {
                for (int y = 0; y < VoxelVolume.ChunkDimension; y++)
                {
                    for (int x = 0; x < VoxelVolume.ChunkDimension; x++)
                    {
                        bool isOccupied = chunk.IsOccupied(x, y, z);
                        destination[VoxelVolume.FlattenChunkVoxelIndex(x, y, z)] = isOccupied;
                        if (isOccupied)
                        {
                            occupiedVoxelCount++;
                        }
                    }
                }
            }

            return occupiedVoxelCount;
        }

        private static CuboidCandidate FindBestStrictCandidate(bool[] remaining)
        {
            bool hasCandidate = false;
            CuboidCandidate bestCandidate = default;

            for (int z = 0; z < VoxelVolume.ChunkDimension; z++)
            {
                for (int y = 0; y < VoxelVolume.ChunkDimension; y++)
                {
                    for (int x = 0; x < VoxelVolume.ChunkDimension; x++)
                    {
                        if (!remaining[VoxelVolume.FlattenChunkVoxelIndex(x, y, z)])
                        {
                            continue;
                        }

                        int3 seed = new int3(x, y, z);
                        for (int i = 0; i < AxisOrders.Length; i++)
                        {
                            CuboidCandidate candidate = BuildCandidate(remaining, seed, AxisOrders[i]);
                            if (!hasCandidate || IsBetterCandidate(candidate, bestCandidate))
                            {
                                bestCandidate = candidate;
                                hasCandidate = true;
                            }
                        }
                    }
                }
            }

            if (!hasCandidate)
            {
                throw new InvalidOperationException("Failed to find a valid occupied cuboid while uncovered voxels remained.");
            }

            return bestCandidate;
        }

        private static CuboidCandidate BuildCandidate(bool[] remaining, int3 seed, AxisOrder axisOrder)
        {
            int3 min = seed;
            int3 maxExclusive = seed + new int3(1, 1, 1);

            ExpandAlongAxis(remaining, ref min, ref maxExclusive, axisOrder.FirstAxis);
            ExpandAlongAxis(remaining, ref min, ref maxExclusive, axisOrder.SecondAxis);
            ExpandAlongAxis(remaining, ref min, ref maxExclusive, axisOrder.ThirdAxis);

            int3 size = maxExclusive - min;
            return new CuboidCandidate(
                min,
                maxExclusive,
                size.x * size.y * size.z,
                ComputeSurfaceArea(size),
                ComputeAspectPenalty(size));
        }

        private static void ExpandAlongAxis(bool[] remaining, ref int3 min, ref int3 maxExclusive, int axis)
        {
            while (CanExtendAlongAxis(remaining, min, maxExclusive, axis))
            {
                SetAxis(ref maxExclusive, axis, GetAxis(maxExclusive, axis) + 1);
            }
        }

        private static bool CanExtendAlongAxis(bool[] remaining, int3 min, int3 maxExclusive, int axis)
        {
            int boundary = GetAxis(maxExclusive, axis);
            if (boundary >= VoxelVolume.ChunkDimension)
            {
                return false;
            }

            switch (axis)
            {
                case 0:
                    for (int z = min.z; z < maxExclusive.z; z++)
                    {
                        for (int y = min.y; y < maxExclusive.y; y++)
                        {
                            if (!remaining[VoxelVolume.FlattenChunkVoxelIndex(boundary, y, z)])
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                case 1:
                    for (int z = min.z; z < maxExclusive.z; z++)
                    {
                        for (int x = min.x; x < maxExclusive.x; x++)
                        {
                            if (!remaining[VoxelVolume.FlattenChunkVoxelIndex(x, boundary, z)])
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                case 2:
                    for (int y = min.y; y < maxExclusive.y; y++)
                    {
                        for (int x = min.x; x < maxExclusive.x; x++)
                        {
                            if (!remaining[VoxelVolume.FlattenChunkVoxelIndex(x, y, boundary)])
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis must be 0, 1, or 2.");
            }
        }

        private static int ClearCuboid(bool[] remaining, int3 min, int3 maxExclusive)
        {
            int clearedVoxelCount = 0;
            for (int z = min.z; z < maxExclusive.z; z++)
            {
                for (int y = min.y; y < maxExclusive.y; y++)
                {
                    for (int x = min.x; x < maxExclusive.x; x++)
                    {
                        int flatIndex = VoxelVolume.FlattenChunkVoxelIndex(x, y, z);
                        if (!remaining[flatIndex])
                        {
                            continue;
                        }

                        remaining[flatIndex] = false;
                        clearedVoxelCount++;
                    }
                }
            }

            return clearedVoxelCount;
        }

        private static void ComputeRemainingBounds(bool[] remaining, out int3 min, out int3 maxExclusive)
        {
            bool hasRemainingVoxel = false;
            min = new int3(VoxelVolume.ChunkDimension, VoxelVolume.ChunkDimension, VoxelVolume.ChunkDimension);
            int3 maxInclusive = new int3(-1, -1, -1);

            for (int z = 0; z < VoxelVolume.ChunkDimension; z++)
            {
                for (int y = 0; y < VoxelVolume.ChunkDimension; y++)
                {
                    for (int x = 0; x < VoxelVolume.ChunkDimension; x++)
                    {
                        if (!remaining[VoxelVolume.FlattenChunkVoxelIndex(x, y, z)])
                        {
                            continue;
                        }

                        int3 voxel = new int3(x, y, z);
                        min = math.min(min, voxel);
                        maxInclusive = math.max(maxInclusive, voxel);
                        hasRemainingVoxel = true;
                    }
                }
            }

            if (!hasRemainingVoxel)
            {
                throw new InvalidOperationException("Cannot build a fallback AABB because no uncovered voxels remained.");
            }

            maxExclusive = maxInclusive + new int3(1, 1, 1);
        }

        private static bool IsBetterCandidate(CuboidCandidate candidate, CuboidCandidate currentBest)
        {
            if (candidate.Volume != currentBest.Volume)
            {
                return candidate.Volume > currentBest.Volume;
            }

            if (candidate.SurfaceArea != currentBest.SurfaceArea)
            {
                return candidate.SurfaceArea < currentBest.SurfaceArea;
            }

            if (candidate.AspectPenalty != currentBest.AspectPenalty)
            {
                return candidate.AspectPenalty < currentBest.AspectPenalty;
            }

            int minComparison = CompareInt3(candidate.Min, currentBest.Min);
            if (minComparison != 0)
            {
                return minComparison < 0;
            }

            return CompareInt3(candidate.MaxExclusive, currentBest.MaxExclusive) < 0;
        }

        private static int ComputeSurfaceArea(int3 size)
        {
            return 2 * ((size.x * size.y) + (size.x * size.z) + (size.y * size.z));
        }

        private static int ComputeAspectPenalty(int3 size)
        {
            int maxDimension = Math.Max(size.x, Math.Max(size.y, size.z));
            int minDimension = Math.Min(size.x, Math.Min(size.y, size.z));
            return maxDimension - minDimension;
        }

        private static int CompareInt3(int3 lhs, int3 rhs)
        {
            if (lhs.x != rhs.x)
            {
                return lhs.x.CompareTo(rhs.x);
            }

            if (lhs.y != rhs.y)
            {
                return lhs.y.CompareTo(rhs.y);
            }

            return lhs.z.CompareTo(rhs.z);
        }

        private static int GetAxis(int3 value, int axis)
        {
            switch (axis)
            {
                case 0:
                    return value.x;
                case 1:
                    return value.y;
                case 2:
                    return value.z;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis must be 0, 1, or 2.");
            }
        }

        private static void SetAxis(ref int3 value, int axis, int coordinate)
        {
            switch (axis)
            {
                case 0:
                    value.x = coordinate;
                    break;
                case 1:
                    value.y = coordinate;
                    break;
                case 2:
                    value.z = coordinate;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis must be 0, 1, or 2.");
            }
        }

        private readonly struct AxisOrder
        {
            public AxisOrder(int firstAxis, int secondAxis, int thirdAxis)
            {
                FirstAxis = firstAxis;
                SecondAxis = secondAxis;
                ThirdAxis = thirdAxis;
            }

            public int FirstAxis { get; }

            public int SecondAxis { get; }

            public int ThirdAxis { get; }
        }

        private readonly struct CuboidCandidate
        {
            public CuboidCandidate(int3 min, int3 maxExclusive, int volume, int surfaceArea, int aspectPenalty)
            {
                Min = min;
                MaxExclusive = maxExclusive;
                Volume = volume;
                SurfaceArea = surfaceArea;
                AspectPenalty = aspectPenalty;
            }

            public int3 Min { get; }

            public int3 MaxExclusive { get; }

            public int Volume { get; }

            public int SurfaceArea { get; }

            public int AspectPenalty { get; }
        }
    }
}
