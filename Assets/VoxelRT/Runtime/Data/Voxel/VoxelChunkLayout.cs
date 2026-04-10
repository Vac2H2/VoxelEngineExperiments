using System;

namespace VoxelRT.Runtime.Data
{
    public static class VoxelChunkLayout
    {
        public const int Dimension = 8;
        public const int VoxelCount = Dimension * Dimension * Dimension;
        public const int OccupancyByteCount = VoxelCount / 8;
        public const int VoxelDataByteCount = VoxelCount;

        public static int FlattenVoxelDataIndex(int x, int y, int z)
        {
            ValidateCoordinate(nameof(x), x);
            ValidateCoordinate(nameof(y), y);
            ValidateCoordinate(nameof(z), z);
            return x + (Dimension * (y + (Dimension * z)));
        }

        public static int FlattenLocalIndex(int x, int y, int z)
        {
            return FlattenVoxelDataIndex(x, y, z);
        }

        public static int ComputeOccupancyByteIndex(int y, int z)
        {
            ValidateCoordinate(nameof(y), y);
            ValidateCoordinate(nameof(z), z);
            return y + (Dimension * z);
        }

        public static int ComputeOccupancyByteIndex(int x, int y, int z)
        {
            ValidateCoordinate(nameof(x), x);
            return ComputeOccupancyByteIndex(y, z);
        }

        public static byte ComputeOccupancyBitMask(int x)
        {
            ValidateCoordinate(nameof(x), x);
            return checked((byte)(1 << x));
        }

        private static void ValidateCoordinate(string paramName, int coordinate)
        {
            if ((uint)coordinate >= Dimension)
            {
                throw new ArgumentOutOfRangeException(paramName, $"Voxel chunk coordinate must be in the range [0, {Dimension - 1}].");
            }
        }
    }
}
