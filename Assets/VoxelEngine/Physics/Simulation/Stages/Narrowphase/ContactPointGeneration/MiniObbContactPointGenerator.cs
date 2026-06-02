using System;
using UnityEngine;

namespace VoxelEngine.Physics.Simulation.Stages.Narrowphase.ContactPointGeneration
{
    internal sealed class MiniObbContactPointGenerator
    {
        public const int MaxCandidateCount = 32;

        private const float Epsilon = 0.000001f;
        private const float DuplicatePointDistanceSq = 0.000001f;
        private const int ClipBufferCapacity = 16;

        private readonly Vector3[] _clipBufferA = new Vector3[ClipBufferCapacity];
        private readonly Vector3[] _clipBufferB = new Vector3[ClipBufferCapacity];

        public bool TryGenerate(
            MiniObbFrame a,
            MiniObbFrame b,
            MiniContactCandidate[] candidates,
            out int candidateCount,
            out Vector3 normal,
            out float penetration)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            candidateCount = 0;
            normal = Vector3.up;
            penetration = 0.0f;

            if (!TryComputeSat(a, b, out MiniObbSatContact satContact))
            {
                return false;
            }

            normal = satContact.Normal;
            penetration = satContact.Penetration;

            if (satContact.Feature == MiniObbSatFeature.EdgeEdge)
            {
                AddEdgeEdgeCandidate(a, b, satContact, candidates, ref candidateCount);
            }
            else
            {
                AddFaceCandidates(a, b, satContact, candidates, ref candidateCount);
            }

            if (candidateCount == 0)
            {
                AddContainedVertexCandidates(a, b, satContact, candidates, ref candidateCount);
            }

            if (candidateCount == 0)
            {
                AddSupportFallbackCandidate(a, b, satContact, candidates, ref candidateCount);
            }

            return candidateCount > 0;
        }

        private void AddFaceCandidates(
            MiniObbFrame a,
            MiniObbFrame b,
            MiniObbSatContact satContact,
            MiniContactCandidate[] candidates,
            ref int candidateCount)
        {
            MiniObbFrame reference = satContact.Feature == MiniObbSatFeature.FaceA ? a : b;
            MiniObbFrame incident = satContact.Feature == MiniObbSatFeature.FaceA ? b : a;
            Vector3 referenceOutNormal = satContact.Feature == MiniObbSatFeature.FaceA
                ? satContact.Normal
                : -satContact.Normal;

            int referenceAxisIndex = satContact.AxisAIndex >= 0 ? satContact.AxisAIndex : satContact.AxisBIndex;
            int referenceNormalSign = Vector3.Dot(GetAxis(reference, referenceAxisIndex), referenceOutNormal) >= 0.0f ? 1 : -1;

            GetFaceBasis(
                referenceAxisIndex,
                out int referenceUAxisIndex,
                out int referenceVAxisIndex);

            Vector3 referenceFaceCenter =
                reference.Center +
                (GetAxis(reference, referenceAxisIndex) *
                 (GetExtent(reference, referenceAxisIndex) * referenceNormalSign));

            Vector3 referenceUAxis = GetAxis(reference, referenceUAxisIndex);
            Vector3 referenceVAxis = GetAxis(reference, referenceVAxisIndex);
            float referenceUExtent = GetExtent(reference, referenceUAxisIndex);
            float referenceVExtent = GetExtent(reference, referenceVAxisIndex);

            int incidentAxisIndex;
            int incidentFaceSign;
            FindIncidentFace(incident, referenceOutNormal, out incidentAxisIndex, out incidentFaceSign);
            int currentCount = WriteFaceVertices(incident, incidentAxisIndex, incidentFaceSign, _clipBufferA);

            currentCount = ClipAgainstFaceSidePlane(
                _clipBufferA,
                currentCount,
                _clipBufferB,
                referenceFaceCenter,
                referenceUAxis,
                referenceUExtent);
            currentCount = ClipAgainstFaceSidePlane(
                _clipBufferB,
                currentCount,
                _clipBufferA,
                referenceFaceCenter,
                -referenceUAxis,
                referenceUExtent);
            currentCount = ClipAgainstFaceSidePlane(
                _clipBufferA,
                currentCount,
                _clipBufferB,
                referenceFaceCenter,
                referenceVAxis,
                referenceVExtent);
            currentCount = ClipAgainstFaceSidePlane(
                _clipBufferB,
                currentCount,
                _clipBufferA,
                referenceFaceCenter,
                -referenceVAxis,
                referenceVExtent);

            for (int pointIndex = 0; pointIndex < currentCount; pointIndex++)
            {
                Vector3 incidentPoint = _clipBufferA[pointIndex];
                float pointPenetration = Vector3.Dot(referenceFaceCenter - incidentPoint, referenceOutNormal);
                if (pointPenetration < -Epsilon)
                {
                    continue;
                }

                Vector3 contactPosition = incidentPoint + (referenceOutNormal * (Mathf.Max(0.0f, pointPenetration) * 0.5f));
                AddCandidate(
                    candidates,
                    ref candidateCount,
                    contactPosition,
                    pointPenetration);
            }
        }

        private static void AddEdgeEdgeCandidate(
            MiniObbFrame a,
            MiniObbFrame b,
            MiniObbSatContact satContact,
            MiniContactCandidate[] candidates,
            ref int candidateCount)
        {
            BuildContactEdge(
                a,
                satContact.AxisAIndex,
                satContact.Normal,
                out Vector3 a0,
                out Vector3 a1);
            BuildContactEdge(
                b,
                satContact.AxisBIndex,
                -satContact.Normal,
                out Vector3 b0,
                out Vector3 b1);

            ClosestPointsBetweenSegments(a0, a1, b0, b1, out Vector3 pointA, out Vector3 pointB);
            AddCandidate(
                candidates,
                ref candidateCount,
                (pointA + pointB) * 0.5f,
                satContact.Penetration);
        }

        private static void AddContainedVertexCandidates(
            MiniObbFrame a,
            MiniObbFrame b,
            MiniObbSatContact satContact,
            MiniContactCandidate[] candidates,
            ref int candidateCount)
        {
            AddContainedVertices(a, b, satContact, candidates, ref candidateCount);
            AddContainedVertices(b, a, satContact, candidates, ref candidateCount);
        }

        private static void AddContainedVertices(
            MiniObbFrame source,
            MiniObbFrame target,
            MiniObbSatContact satContact,
            MiniContactCandidate[] candidates,
            ref int candidateCount)
        {
            for (int vertexIndex = 0; vertexIndex < 8; vertexIndex++)
            {
                Vector3 vertex = GetVertex(source, vertexIndex);
                if (!ContainsPoint(target, vertex))
                {
                    continue;
                }

                AddCandidate(candidates, ref candidateCount, vertex, satContact.Penetration);
            }
        }

        private static void AddSupportFallbackCandidate(
            MiniObbFrame a,
            MiniObbFrame b,
            MiniObbSatContact satContact,
            MiniContactCandidate[] candidates,
            ref int candidateCount)
        {
            Vector3 pointA = GetSupportPoint(a, satContact.Normal);
            Vector3 pointB = GetSupportPoint(b, -satContact.Normal);
            AddCandidate(candidates, ref candidateCount, (pointA + pointB) * 0.5f, satContact.Penetration);
        }

        private static bool TryComputeSat(MiniObbFrame a, MiniObbFrame b, out MiniObbSatContact contact)
        {
            contact = default;
            float bestPenetration = float.PositiveInfinity;
            Vector3 centerDelta = b.Center - a.Center;

            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
                Vector3 axis = GetAxis(a, axisIndex);
                if (!TryTestAxis(
                    a,
                    b,
                    centerDelta,
                    axis,
                    MiniObbSatFeature.FaceA,
                    axisIndex,
                    -1,
                    ref bestPenetration,
                    ref contact))
                {
                    return false;
                }
            }

            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
                Vector3 axis = GetAxis(b, axisIndex);
                if (!TryTestAxis(
                    a,
                    b,
                    centerDelta,
                    axis,
                    MiniObbSatFeature.FaceB,
                    -1,
                    axisIndex,
                    ref bestPenetration,
                    ref contact))
                {
                    return false;
                }
            }

            for (int axisAIndex = 0; axisAIndex < 3; axisAIndex++)
            {
                Vector3 axisA = GetAxis(a, axisAIndex);
                for (int axisBIndex = 0; axisBIndex < 3; axisBIndex++)
                {
                    Vector3 axis = Vector3.Cross(axisA, GetAxis(b, axisBIndex));
                    float lengthSquared = axis.sqrMagnitude;
                    if (lengthSquared <= Epsilon)
                    {
                        continue;
                    }

                    axis /= Mathf.Sqrt(lengthSquared);
                    if (!TryTestAxis(
                        a,
                        b,
                        centerDelta,
                        axis,
                        MiniObbSatFeature.EdgeEdge,
                        axisAIndex,
                        axisBIndex,
                        ref bestPenetration,
                        ref contact))
                    {
                        return false;
                    }
                }
            }

            return bestPenetration < float.PositiveInfinity;
        }

        private static bool TryTestAxis(
            MiniObbFrame a,
            MiniObbFrame b,
            Vector3 centerDelta,
            Vector3 axis,
            MiniObbSatFeature feature,
            int axisAIndex,
            int axisBIndex,
            ref float bestPenetration,
            ref MiniObbSatContact contact)
        {
            axis = NormalizeOrFallback(axis, Vector3.up);
            float centerDistance = Vector3.Dot(centerDelta, axis);
            float radiusA = ProjectRadius(a, axis);
            float radiusB = ProjectRadius(b, axis);
            float penetration = radiusA + radiusB - Mathf.Abs(centerDistance);

            if (penetration < -Epsilon)
            {
                return false;
            }

            if (penetration >= bestPenetration)
            {
                return true;
            }

            Vector3 normal = centerDistance >= 0.0f ? axis : -axis;
            bestPenetration = penetration;
            contact = new MiniObbSatContact(feature, axisAIndex, axisBIndex, normal, Mathf.Max(0.0f, penetration));
            return true;
        }

        private static float ProjectRadius(MiniObbFrame obb, Vector3 axis)
        {
            return
                (obb.Extents.x * Mathf.Abs(Vector3.Dot(obb.AxisX, axis))) +
                (obb.Extents.y * Mathf.Abs(Vector3.Dot(obb.AxisY, axis))) +
                (obb.Extents.z * Mathf.Abs(Vector3.Dot(obb.AxisZ, axis)));
        }

        private static int WriteFaceVertices(MiniObbFrame obb, int faceAxisIndex, int faceSign, Vector3[] destination)
        {
            GetFaceBasis(faceAxisIndex, out int uAxisIndex, out int vAxisIndex);

            Vector3 faceCenter =
                obb.Center +
                (GetAxis(obb, faceAxisIndex) * (GetExtent(obb, faceAxisIndex) * faceSign));
            Vector3 u = GetAxis(obb, uAxisIndex) * GetExtent(obb, uAxisIndex);
            Vector3 v = GetAxis(obb, vAxisIndex) * GetExtent(obb, vAxisIndex);

            destination[0] = faceCenter + u + v;
            destination[1] = faceCenter - u + v;
            destination[2] = faceCenter - u - v;
            destination[3] = faceCenter + u - v;
            return 4;
        }

        private static int ClipAgainstFaceSidePlane(
            Vector3[] input,
            int inputCount,
            Vector3[] output,
            Vector3 faceCenter,
            Vector3 sideNormal,
            float sideExtent)
        {
            if (inputCount <= 0)
            {
                return 0;
            }

            int outputCount = 0;
            Vector3 previous = input[inputCount - 1];
            float previousDistance = Vector3.Dot(previous - faceCenter, sideNormal) - sideExtent;
            bool previousInside = previousDistance <= Epsilon;

            for (int index = 0; index < inputCount; index++)
            {
                Vector3 current = input[index];
                float currentDistance = Vector3.Dot(current - faceCenter, sideNormal) - sideExtent;
                bool currentInside = currentDistance <= Epsilon;

                if (currentInside != previousInside)
                {
                    float denominator = previousDistance - currentDistance;
                    if (Mathf.Abs(denominator) > Epsilon)
                    {
                        float t = previousDistance / denominator;
                        AppendClipPoint(output, ref outputCount, previous + ((current - previous) * t));
                    }
                }

                if (currentInside)
                {
                    AppendClipPoint(output, ref outputCount, current);
                }

                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }

            return outputCount;
        }

        private static void AppendClipPoint(Vector3[] output, ref int outputCount, Vector3 point)
        {
            if (outputCount >= output.Length)
            {
                return;
            }

            output[outputCount++] = point;
        }

        private static void FindIncidentFace(
            MiniObbFrame obb,
            Vector3 referenceOutNormal,
            out int incidentAxisIndex,
            out int incidentFaceSign)
        {
            float bestDot = float.PositiveInfinity;
            incidentAxisIndex = 0;
            incidentFaceSign = 1;

            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
                Vector3 axis = GetAxis(obb, axisIndex);
                float positiveDot = Vector3.Dot(axis, referenceOutNormal);
                if (positiveDot < bestDot)
                {
                    bestDot = positiveDot;
                    incidentAxisIndex = axisIndex;
                    incidentFaceSign = 1;
                }

                float negativeDot = -positiveDot;
                if (negativeDot < bestDot)
                {
                    bestDot = negativeDot;
                    incidentAxisIndex = axisIndex;
                    incidentFaceSign = -1;
                }
            }
        }

        private static void BuildContactEdge(
            MiniObbFrame obb,
            int edgeAxisIndex,
            Vector3 normalTowardOther,
            out Vector3 edgeStart,
            out Vector3 edgeEnd)
        {
            Vector3 edgeCenter = obb.Center;
            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
                if (axisIndex == edgeAxisIndex)
                {
                    continue;
                }

                Vector3 axis = GetAxis(obb, axisIndex);
                float sign = Vector3.Dot(axis, normalTowardOther) >= 0.0f ? 1.0f : -1.0f;
                edgeCenter += axis * (GetExtent(obb, axisIndex) * sign);
            }

            Vector3 edgeHalfVector = GetAxis(obb, edgeAxisIndex) * GetExtent(obb, edgeAxisIndex);
            edgeStart = edgeCenter - edgeHalfVector;
            edgeEnd = edgeCenter + edgeHalfVector;
        }

        private static void ClosestPointsBetweenSegments(
            Vector3 p1,
            Vector3 q1,
            Vector3 p2,
            Vector3 q2,
            out Vector3 closest1,
            out Vector3 closest2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float s;
            float t;

            if (a <= Epsilon && e <= Epsilon)
            {
                closest1 = p1;
                closest2 = p2;
                return;
            }

            if (a <= Epsilon)
            {
                s = 0.0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= Epsilon)
                {
                    t = 0.0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denominator = (a * e) - (b * b);
                    s = denominator != 0.0f
                        ? Mathf.Clamp01(((b * f) - (c * e)) / denominator)
                        : 0.0f;

                    t = (b * s + f) / e;
                    if (t < 0.0f)
                    {
                        t = 0.0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1.0f)
                    {
                        t = 1.0f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            closest1 = p1 + (d1 * s);
            closest2 = p2 + (d2 * t);
        }

        private static bool ContainsPoint(MiniObbFrame obb, Vector3 point)
        {
            Vector3 local = point - obb.Center;
            return
                Mathf.Abs(Vector3.Dot(local, obb.AxisX)) <= obb.Extents.x + Epsilon &&
                Mathf.Abs(Vector3.Dot(local, obb.AxisY)) <= obb.Extents.y + Epsilon &&
                Mathf.Abs(Vector3.Dot(local, obb.AxisZ)) <= obb.Extents.z + Epsilon;
        }

        private static Vector3 GetVertex(MiniObbFrame obb, int vertexIndex)
        {
            return obb.Center +
                (obb.AxisX * (((vertexIndex & 1) == 0 ? -1.0f : 1.0f) * obb.Extents.x)) +
                (obb.AxisY * (((vertexIndex & 2) == 0 ? -1.0f : 1.0f) * obb.Extents.y)) +
                (obb.AxisZ * (((vertexIndex & 4) == 0 ? -1.0f : 1.0f) * obb.Extents.z));
        }

        private static Vector3 GetSupportPoint(MiniObbFrame obb, Vector3 direction)
        {
            return obb.Center +
                (obb.AxisX * ((Vector3.Dot(obb.AxisX, direction) >= 0.0f ? 1.0f : -1.0f) * obb.Extents.x)) +
                (obb.AxisY * ((Vector3.Dot(obb.AxisY, direction) >= 0.0f ? 1.0f : -1.0f) * obb.Extents.y)) +
                (obb.AxisZ * ((Vector3.Dot(obb.AxisZ, direction) >= 0.0f ? 1.0f : -1.0f) * obb.Extents.z));
        }

        private static void AddCandidate(
            MiniContactCandidate[] candidates,
            ref int candidateCount,
            Vector3 position,
            float penetration)
        {
            float sanitizedPenetration = Mathf.Max(0.0f, float.IsFinite(penetration) ? penetration : 0.0f);
            for (int index = 0; index < candidateCount; index++)
            {
                if ((candidates[index].Position - position).sqrMagnitude > DuplicatePointDistanceSq)
                {
                    continue;
                }

                if (sanitizedPenetration > candidates[index].Penetration)
                {
                    candidates[index] = new MiniContactCandidate(position, sanitizedPenetration);
                }

                return;
            }

            if (candidateCount >= candidates.Length)
            {
                return;
            }

            candidates[candidateCount++] = new MiniContactCandidate(position, sanitizedPenetration);
        }

        private static void GetFaceBasis(int normalAxisIndex, out int uAxisIndex, out int vAxisIndex)
        {
            switch (normalAxisIndex)
            {
                case 0:
                    uAxisIndex = 1;
                    vAxisIndex = 2;
                    break;
                case 1:
                    uAxisIndex = 0;
                    vAxisIndex = 2;
                    break;
                default:
                    uAxisIndex = 0;
                    vAxisIndex = 1;
                    break;
            }
        }

        private static Vector3 GetAxis(MiniObbFrame obb, int axisIndex)
        {
            return axisIndex switch
            {
                0 => obb.AxisX,
                1 => obb.AxisY,
                _ => obb.AxisZ,
            };
        }

        private static float GetExtent(MiniObbFrame obb, int axisIndex)
        {
            return axisIndex switch
            {
                0 => obb.Extents.x,
                1 => obb.Extents.y,
                _ => obb.Extents.z,
            };
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            float magnitude = value.magnitude;
            return magnitude > 0.0f ? value / magnitude : fallback;
        }

        private readonly struct MiniObbSatContact
        {
            public MiniObbSatContact(
                MiniObbSatFeature feature,
                int axisAIndex,
                int axisBIndex,
                Vector3 normal,
                float penetration)
            {
                Feature = feature;
                AxisAIndex = axisAIndex;
                AxisBIndex = axisBIndex;
                Normal = normal;
                Penetration = penetration;
            }

            public MiniObbSatFeature Feature { get; }

            public int AxisAIndex { get; }

            public int AxisBIndex { get; }

            public Vector3 Normal { get; }

            public float Penetration { get; }
        }

        private enum MiniObbSatFeature
        {
            FaceA,
            FaceB,
            EdgeEdge,
        }
    }
}
