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
        public int EnableValidation;
    }
}
