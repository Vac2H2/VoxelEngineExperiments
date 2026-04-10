using System;

namespace VoxelRT.Runtime.Data
{
    public static class VoxelChunkLayout
    {
        public const int Dimension = 8;
        public const int VoxelCount = Dimension * Dimension * Dimension;
        public const int OccupancyByteCount = VoxelCount / 8;
        public const int VoxelDataByteCount = VoxelCount;

        public static int FlattenLocalIndex(int x, int y, int z)
        {
            ValidateCoordinate(nameof(x), x);
            ValidateCoordinate(nameof(y), y);
            ValidateCoordinate(nameof(z), z);
            return x + (Dimension * (y + (Dimension * z)));
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
