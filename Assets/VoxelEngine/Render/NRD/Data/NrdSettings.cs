using System.Runtime.InteropServices;

namespace VoxelEngine.Render.NRD.Data
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NrdSettings
    {
        public int Width;
        public int Height;
        public float DenoisingRange;
        public int MaxAccumulatedFrameNum;
        public int MaxFastAccumulatedFrameNum;
        public int HistoryFixFrameNum;
        public float HitDistanceA;
        public float HitDistanceB;
        public float HitDistanceC;
        public float DiffusePrepassBlurRadius;
        public float MinBlurRadius;
        public float MaxBlurRadius;
        public float PlaneDistanceSensitivity;
        public float FastHistoryClampingSigmaScale;
        public float MinHitDistanceWeight;
        public int MaxStabilizedFrameNum;
        public int EnableValidation;
    }
}
