using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VoxelEngine.Render.NRD.Data
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NrdMatrix4x4
    {
        public float M00;
        public float M01;
        public float M02;
        public float M03;
        public float M10;
        public float M11;
        public float M12;
        public float M13;
        public float M20;
        public float M21;
        public float M22;
        public float M23;
        public float M30;
        public float M31;
        public float M32;
        public float M33;

        public static NrdMatrix4x4 FromUnityMatrix(Matrix4x4 matrix)
        {
            return new NrdMatrix4x4
            {
                M00 = matrix.m00,
                M01 = matrix.m01,
                M02 = matrix.m02,
                M03 = matrix.m03,
                M10 = matrix.m10,
                M11 = matrix.m11,
                M12 = matrix.m12,
                M13 = matrix.m13,
                M20 = matrix.m20,
                M21 = matrix.m21,
                M22 = matrix.m22,
                M23 = matrix.m23,
                M30 = matrix.m30,
                M31 = matrix.m31,
                M32 = matrix.m32,
                M33 = matrix.m33
            };
        }
    }

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
        public NrdMatrix4x4 CurrentWorldToView;
        public NrdMatrix4x4 PreviousWorldToView;
        public NrdMatrix4x4 CurrentViewToClip;
        public NrdMatrix4x4 PreviousViewToClip;
    }
}
