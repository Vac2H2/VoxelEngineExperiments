using System;
using UnityEngine;
using VoxelEngine.Physics.Simulation.Stages.Narrowphase.ContactPointGeneration;

namespace VoxelEngine.Physics.Simulation.Stages.Narrowphase.Reduction
{
    internal static class MiniContactReducer
    {
        private const float DuplicatePointDistanceSq = 0.000001f;
        private const float Epsilon = 0.000001f;

        public static void Reduce(
            MiniContactCandidate[] candidates,
            int candidateCount,
            Vector3 normal,
            MiniContactPointFrame[] output,
            out int outputCount)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            outputCount = 0;
            int safeCandidateCount = Mathf.Clamp(candidateCount, 0, candidates.Length);
            if (safeCandidateCount == 0)
            {
                return;
            }

            if (safeCandidateCount <= MiniContactManifoldFrame.MaxPointCount)
            {
                for (int index = 0; index < safeCandidateCount; index++)
                {
                    AddOutputPoint(output, ref outputCount, candidates[index]);
                }

                return;
            }

            Vector3 contactNormal = NormalizeOrFallback(normal, Vector3.up);
            int selected0 = FindDeepestPoint(candidates, safeCandidateCount);
            int selected1 = FindFarthestPoint(candidates, safeCandidateCount, candidates[selected0].Position, contactNormal, selected0, -1, -1);
            int selected2 = selected1 >= 0
                ? FindLargestTrianglePoint(candidates, safeCandidateCount, candidates[selected0].Position, candidates[selected1].Position, contactNormal, selected0, selected1, -1)
                : -1;
            int selected3 = selected2 >= 0
                ? FindLargestCoveragePoint(candidates, safeCandidateCount, candidates[selected0].Position, candidates[selected1].Position, candidates[selected2].Position, contactNormal, selected0, selected1, selected2)
                : -1;

            AddSelectedPoint(output, ref outputCount, candidates, selected0);
            AddSelectedPoint(output, ref outputCount, candidates, selected1);
            AddSelectedPoint(output, ref outputCount, candidates, selected2);
            AddSelectedPoint(output, ref outputCount, candidates, selected3);
        }

        private static int FindDeepestPoint(MiniContactCandidate[] candidates, int candidateCount)
        {
            int bestIndex = 0;
            float bestPenetration = candidates[0].Penetration;
            for (int index = 1; index < candidateCount; index++)
            {
                if (candidates[index].Penetration <= bestPenetration)
                {
                    continue;
                }

                bestPenetration = candidates[index].Penetration;
                bestIndex = index;
            }

            return bestIndex;
        }

        private static int FindFarthestPoint(
            MiniContactCandidate[] candidates,
            int candidateCount,
            Vector3 anchor,
            Vector3 normal,
            int selected0,
            int selected1,
            int selected2)
        {
            int bestIndex = -1;
            float bestDistanceSq = Epsilon;
            for (int index = 0; index < candidateCount; index++)
            {
                if (IsSelected(index, selected0, selected1, selected2))
                {
                    continue;
                }

                Vector3 offset = candidates[index].Position - anchor;
                offset -= normal * Vector3.Dot(offset, normal);
                float distanceSq = offset.sqrMagnitude;
                if (distanceSq <= bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                bestIndex = index;
            }

            return bestIndex;
        }

        private static int FindLargestTrianglePoint(
            MiniContactCandidate[] candidates,
            int candidateCount,
            Vector3 point0,
            Vector3 point1,
            Vector3 normal,
            int selected0,
            int selected1,
            int selected2)
        {
            int bestIndex = -1;
            float bestArea = Epsilon;
            for (int index = 0; index < candidateCount; index++)
            {
                if (IsSelected(index, selected0, selected1, selected2))
                {
                    continue;
                }

                float area = TriangleArea2(point0, point1, candidates[index].Position, normal);
                if (area <= bestArea)
                {
                    continue;
                }

                bestArea = area;
                bestIndex = index;
            }

            return bestIndex;
        }

        private static int FindLargestCoveragePoint(
            MiniContactCandidate[] candidates,
            int candidateCount,
            Vector3 point0,
            Vector3 point1,
            Vector3 point2,
            Vector3 normal,
            int selected0,
            int selected1,
            int selected2)
        {
            int bestIndex = -1;
            float bestArea = Epsilon;
            for (int index = 0; index < candidateCount; index++)
            {
                if (IsSelected(index, selected0, selected1, selected2))
                {
                    continue;
                }

                Vector3 candidate = candidates[index].Position;
                float area =
                    TriangleArea2(point0, point1, candidate, normal) +
                    TriangleArea2(point1, point2, candidate, normal) +
                    TriangleArea2(point2, point0, candidate, normal);
                if (area <= bestArea)
                {
                    continue;
                }

                bestArea = area;
                bestIndex = index;
            }

            return bestIndex;
        }

        private static float TriangleArea2(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            return Mathf.Abs(Vector3.Dot(Vector3.Cross(b - a, c - a), normal));
        }

        private static bool IsSelected(int index, int selected0, int selected1, int selected2)
        {
            return index == selected0 || index == selected1 || index == selected2;
        }

        private static void AddSelectedPoint(
            MiniContactPointFrame[] output,
            ref int outputCount,
            MiniContactCandidate[] candidates,
            int candidateIndex)
        {
            if (candidateIndex < 0)
            {
                return;
            }

            AddOutputPoint(output, ref outputCount, candidates[candidateIndex]);
        }

        private static void AddOutputPoint(
            MiniContactPointFrame[] output,
            ref int outputCount,
            MiniContactCandidate candidate)
        {
            if (outputCount >= output.Length || outputCount >= MiniContactManifoldFrame.MaxPointCount)
            {
                return;
            }

            for (int index = 0; index < outputCount; index++)
            {
                if ((output[index].Position - candidate.Position).sqrMagnitude <= DuplicatePointDistanceSq)
                {
                    return;
                }
            }

            output[outputCount++] = new MiniContactPointFrame(candidate.Position, candidate.Penetration);
        }

        private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            float magnitude = value.magnitude;
            return magnitude > 0.0f ? value / magnitude : fallback;
        }
    }
}
