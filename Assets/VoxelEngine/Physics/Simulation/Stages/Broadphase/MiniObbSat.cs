using UnityEngine;

namespace VoxelEngine.Physics.Simulation.Stages.Broadphase
{
    internal static class MiniObbSat
    {
        private const float Epsilon = 0.000001f;

        public static bool Overlaps(MiniObbFrame a, MiniObbFrame b)
        {
            Vector3 a0 = a.AxisX;
            Vector3 a1 = a.AxisY;
            Vector3 a2 = a.AxisZ;
            Vector3 b0 = b.AxisX;
            Vector3 b1 = b.AxisY;
            Vector3 b2 = b.AxisZ;
            Vector3 ae = a.Extents;
            Vector3 be = b.Extents;

            float r00 = Vector3.Dot(a0, b0);
            float r01 = Vector3.Dot(a0, b1);
            float r02 = Vector3.Dot(a0, b2);
            float r10 = Vector3.Dot(a1, b0);
            float r11 = Vector3.Dot(a1, b1);
            float r12 = Vector3.Dot(a1, b2);
            float r20 = Vector3.Dot(a2, b0);
            float r21 = Vector3.Dot(a2, b1);
            float r22 = Vector3.Dot(a2, b2);

            float ar00 = Mathf.Abs(r00) + Epsilon;
            float ar01 = Mathf.Abs(r01) + Epsilon;
            float ar02 = Mathf.Abs(r02) + Epsilon;
            float ar10 = Mathf.Abs(r10) + Epsilon;
            float ar11 = Mathf.Abs(r11) + Epsilon;
            float ar12 = Mathf.Abs(r12) + Epsilon;
            float ar20 = Mathf.Abs(r20) + Epsilon;
            float ar21 = Mathf.Abs(r21) + Epsilon;
            float ar22 = Mathf.Abs(r22) + Epsilon;

            Vector3 centerDelta = b.Center - a.Center;
            float t0 = Vector3.Dot(centerDelta, a0);
            float t1 = Vector3.Dot(centerDelta, a1);
            float t2 = Vector3.Dot(centerDelta, a2);

            if (Mathf.Abs(t0) > ae.x + (be.x * ar00) + (be.y * ar01) + (be.z * ar02))
            {
                return false;
            }

            if (Mathf.Abs(t1) > ae.y + (be.x * ar10) + (be.y * ar11) + (be.z * ar12))
            {
                return false;
            }

            if (Mathf.Abs(t2) > ae.z + (be.x * ar20) + (be.y * ar21) + (be.z * ar22))
            {
                return false;
            }

            if (Mathf.Abs(Vector3.Dot(centerDelta, b0)) > ((ae.x * ar00) + (ae.y * ar10) + (ae.z * ar20)) + be.x)
            {
                return false;
            }

            if (Mathf.Abs(Vector3.Dot(centerDelta, b1)) > ((ae.x * ar01) + (ae.y * ar11) + (ae.z * ar21)) + be.y)
            {
                return false;
            }

            if (Mathf.Abs(Vector3.Dot(centerDelta, b2)) > ((ae.x * ar02) + (ae.y * ar12) + (ae.z * ar22)) + be.z)
            {
                return false;
            }

            return TestCrossAxes(
                ae,
                be,
                t0,
                t1,
                t2,
                r00,
                r01,
                r02,
                r10,
                r11,
                r12,
                r20,
                r21,
                r22,
                ar00,
                ar01,
                ar02,
                ar10,
                ar11,
                ar12,
                ar20,
                ar21,
                ar22);
        }

        private static bool TestCrossAxes(
            Vector3 ae,
            Vector3 be,
            float t0,
            float t1,
            float t2,
            float r00,
            float r01,
            float r02,
            float r10,
            float r11,
            float r12,
            float r20,
            float r21,
            float r22,
            float ar00,
            float ar01,
            float ar02,
            float ar10,
            float ar11,
            float ar12,
            float ar20,
            float ar21,
            float ar22)
        {
            return
                TestAxis(Mathf.Abs((t2 * r10) - (t1 * r20)), (ae.y * ar20) + (ae.z * ar10), (be.y * ar02) + (be.z * ar01)) &&
                TestAxis(Mathf.Abs((t2 * r11) - (t1 * r21)), (ae.y * ar21) + (ae.z * ar11), (be.x * ar02) + (be.z * ar00)) &&
                TestAxis(Mathf.Abs((t2 * r12) - (t1 * r22)), (ae.y * ar22) + (ae.z * ar12), (be.x * ar01) + (be.y * ar00)) &&
                TestAxis(Mathf.Abs((t0 * r20) - (t2 * r00)), (ae.x * ar20) + (ae.z * ar00), (be.y * ar12) + (be.z * ar11)) &&
                TestAxis(Mathf.Abs((t0 * r21) - (t2 * r01)), (ae.x * ar21) + (ae.z * ar01), (be.x * ar12) + (be.z * ar10)) &&
                TestAxis(Mathf.Abs((t0 * r22) - (t2 * r02)), (ae.x * ar22) + (ae.z * ar02), (be.x * ar11) + (be.y * ar10)) &&
                TestAxis(Mathf.Abs((t1 * r00) - (t0 * r10)), (ae.x * ar10) + (ae.y * ar00), (be.y * ar22) + (be.z * ar21)) &&
                TestAxis(Mathf.Abs((t1 * r01) - (t0 * r11)), (ae.x * ar11) + (ae.y * ar01), (be.x * ar22) + (be.z * ar20)) &&
                TestAxis(Mathf.Abs((t1 * r02) - (t0 * r12)), (ae.x * ar12) + (ae.y * ar02), (be.x * ar21) + (be.y * ar20));
        }

        private static bool TestAxis(float distance, float radiusA, float radiusB)
        {
            return distance <= radiusA + radiusB;
        }
    }
}
