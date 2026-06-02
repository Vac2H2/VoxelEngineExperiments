using UnityEngine;

namespace VoxelEngine.Render.Cores
{
    internal readonly struct VoxelCameraState
    {
        public VoxelCameraState(Vector3 position, Vector3 forward)
        {
            Position = position;
            Forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
        }

        public Vector3 Position { get; }

        public Vector3 Forward { get; }

        public static VoxelCameraState FromCamera(Camera camera)
        {
            Matrix4x4 cameraToWorld = camera.cameraToWorldMatrix;
            Vector3 position = cameraToWorld.MultiplyPoint3x4(Vector3.zero);

            Vector3 forward = camera.transform != null ? camera.transform.forward : Vector3.forward;
            Vector3 matrixForward = cameraToWorld.MultiplyVector(Vector3.forward);
            if (Vector3.Dot(matrixForward, forward) < 0.0f)
            {
                matrixForward = -matrixForward;
            }

            if (matrixForward.sqrMagnitude > 1e-8f)
            {
                forward = matrixForward;
            }

            return new VoxelCameraState(position, forward);
        }
    }
}
