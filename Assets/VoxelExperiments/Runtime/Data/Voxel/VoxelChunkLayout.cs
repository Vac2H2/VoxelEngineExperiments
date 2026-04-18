using System;

namespace VoxelExperiments.Runtime.Data
{
    public static class VoxelChunkLayout
    {
        public const int Dimension = 8;
        public const int VoxelCount = Dimension * Dimension * Dimension;
        public const int OccupancyByteCount = VoxelCount / 8;
        public const int VoxelDataByteCount = VoxelCount;

        public static int FlattenVoxelDataIndex(int x, int y, int z)
        {
            return FlattenVoxelDataIndex(VoxelMemoryLayout.Linear, x, y, z);
        }

        public static int FlattenVoxelDataIndex(VoxelMemoryLayout memoryLayout, int x, int y, int z)
        {
            ValidateCoordinate(nameof(x), x);
            ValidateCoordinate(nameof(y), y);
            ValidateCoordinate(nameof(z), z);
            ValidateMemoryLayout(memoryLayout);

            switch (memoryLayout)
            {
                case VoxelMemoryLayout.Linear:
                    return x + (Dimension * (y + (Dimension * z)));
                case VoxelMemoryLayout.Octant:
                    return InterleaveCoordinateBits(x, y, z);
                default:
                    throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }
        }

        public static int FlattenLocalIndex(int x, int y, int z)
        {
            return FlattenVoxelDataIndex(VoxelMemoryLayout.Linear, x, y, z);
        }

        public static int FlattenLocalIndex(VoxelMemoryLayout memoryLayout, int x, int y, int z)
        {
            return FlattenVoxelDataIndex(memoryLayout, x, y, z);
        }

        public static int ComputeOccupancyByteIndex(int y, int z)
        {
            ValidateCoordinate(nameof(y), y);
            ValidateCoordinate(nameof(z), z);
            return y + (Dimension * z);
        }

        public static int ComputeOccupancyByteIndex(int x, int y, int z)
        {
            return ComputeOccupancyByteIndex(VoxelMemoryLayout.Linear, x, y, z);
        }

        public static int ComputeOccupancyByteIndex(VoxelMemoryLayout memoryLayout, int x, int y, int z)
        {
            ValidateCoordinate(nameof(x), x);
            ValidateCoordinate(nameof(y), y);
            ValidateCoordinate(nameof(z), z);
            ValidateMemoryLayout(memoryLayout);

            switch (memoryLayout)
            {
                case VoxelMemoryLayout.Linear:
                    return ComputeOccupancyByteIndex(y, z);
                case VoxelMemoryLayout.Octant:
                    return InterleaveCoordinateBits(x, y, z) >> 3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }
        }

        public static byte ComputeOccupancyBitMask(int x)
        {
            ValidateCoordinate(nameof(x), x);
            return checked((byte)(1 << x));
        }

        public static byte ComputeOccupancyBitMask(VoxelMemoryLayout memoryLayout, int x, int y, int z)
        {
            ValidateCoordinate(nameof(x), x);
            ValidateCoordinate(nameof(y), y);
            ValidateCoordinate(nameof(z), z);
            ValidateMemoryLayout(memoryLayout);

            switch (memoryLayout)
            {
                case VoxelMemoryLayout.Linear:
                    return ComputeOccupancyBitMask(x);
                case VoxelMemoryLayout.Octant:
                    return checked((byte)(1 << (InterleaveCoordinateBits(x, y, z) & 0x7)));
                default:
                    throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }
        }

        private static int InterleaveCoordinateBits(int x, int y, int z)
        {
            int index = 0;
            index |= (x & 1) << 0;
            index |= (y & 1) << 1;
            index |= (z & 1) << 2;
            index |= ((x >> 1) & 1) << 3;
            index |= ((y >> 1) & 1) << 4;
            index |= ((z >> 1) & 1) << 5;
            index |= ((x >> 2) & 1) << 6;
            index |= ((y >> 2) & 1) << 7;
            index |= ((z >> 2) & 1) << 8;
            return index;
        }

        private static void ValidateCoordinate(string paramName, int coordinate)
        {
            if ((uint)coordinate >= Dimension)
            {
                throw new ArgumentOutOfRangeException(paramName, $"Voxel chunk coordinate must be in the range [0, {Dimension - 1}].");
            }
        }

        private static void ValidateMemoryLayout(VoxelMemoryLayout memoryLayout)
        {
            if (!Enum.IsDefined(typeof(VoxelMemoryLayout), memoryLayout))
            {
                throw new ArgumentOutOfRangeException(nameof(memoryLayout), memoryLayout, "Unsupported voxel memory layout.");
            }
        }
    }
}
