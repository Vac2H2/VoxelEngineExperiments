using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VoxelEngine.Render.NRD.Data
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NrdFrameData
    {
        public IntPtr NoisyAmbientNormalizedHitDistance;
        public IntPtr Motion;
        public IntPtr NormalRoughness;
        public IntPtr ViewZ;
        public IntPtr DenoisedAmbientOutput;
        public int CameraId;
        public int Width;
        public int Height;
        public int FrameIndex;
        public Matrix4x4 CurrentWorldToView;
        public Matrix4x4 PreviousWorldToView;
        public Matrix4x4 CurrentViewToClip;
        public Matrix4x4 PreviousViewToClip;
    }
}
