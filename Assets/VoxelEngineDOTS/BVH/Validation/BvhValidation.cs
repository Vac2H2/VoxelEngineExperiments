using Unity.Collections;

namespace VoxelEngineDOTS.BVH
{
    public struct PairValidationResult
    {
        public bool Ran;
        public bool Passed;
        public int NaivePairCount;
        public int QueryPairCount;
        public int UniqueNaivePairCount;
        public int UniqueQueryPairCount;
        public int MissingCount;
        public int ExtraCount;
        public int DuplicateCount;
        public int SelfPairCount;
    }

    public static class NaiveBroadphase
    {
        public static void Query(
            NativeArray<BodyProxy> proxies,
            int bodyCount,
            NativeList<BodyPair> outputPairs,
            int maxOutputPairs,
            out QueryStats stats)
        {
            outputPairs.Clear();
            stats = default;
            int outputLimit = maxOutputPairs <= 0 ? int.MaxValue : maxOutputPairs;

            for (int i = 0; i < bodyCount; i++)
            {
                BodyProxy a = proxies[i];
                for (int j = i + 1; j < bodyCount; j++)
                {
                    BodyProxy b = proxies[j];
                    stats.BodyAabbTests++;
                    if (Aabb.Overlap(a.Bounds, b.Bounds))
                    {
                        if (outputPairs.Length < outputLimit)
                        {
                            outputPairs.Add(new BodyPair(a.BodyId, b.BodyId));
                            stats.EmittedPairs++;
                        }
                        else
                        {
                            stats.OutputOverflow++;
                        }
                    }
                }
            }
        }
    }

    public static class PairValidation
    {
        public static PairValidationResult Validate(
            NativeList<BodyPair> queryPairs,
            NativeList<BodyPair> naivePairs)
        {
            PairValidationResult result = new PairValidationResult
            {
                Ran = true,
                QueryPairCount = queryPairs.Length,
                NaivePairCount = naivePairs.Length
            };

            queryPairs.Sort();
            naivePairs.Sort();

            using NativeList<BodyPair> uniqueQuery = new NativeList<BodyPair>(queryPairs.Length, Allocator.Temp);
            using NativeList<BodyPair> uniqueNaive = new NativeList<BodyPair>(naivePairs.Length, Allocator.Temp);

            CopyUniqueValid(queryPairs, uniqueQuery, ref result);
            CopyUniqueValid(naivePairs, uniqueNaive, ref result);

            result.UniqueQueryPairCount = uniqueQuery.Length;
            result.UniqueNaivePairCount = uniqueNaive.Length;

            int queryIndex = 0;
            int naiveIndex = 0;
            while (queryIndex < uniqueQuery.Length || naiveIndex < uniqueNaive.Length)
            {
                if (queryIndex >= uniqueQuery.Length)
                {
                    result.MissingCount += uniqueNaive.Length - naiveIndex;
                    break;
                }

                if (naiveIndex >= uniqueNaive.Length)
                {
                    result.ExtraCount += uniqueQuery.Length - queryIndex;
                    break;
                }

                BodyPair query = uniqueQuery[queryIndex];
                BodyPair naive = uniqueNaive[naiveIndex];
                int compare = query.CompareTo(naive);

                if (compare == 0)
                {
                    queryIndex++;
                    naiveIndex++;
                }
                else if (compare < 0)
                {
                    result.ExtraCount++;
                    queryIndex++;
                }
                else
                {
                    result.MissingCount++;
                    naiveIndex++;
                }
            }

            result.Passed = result.MissingCount == 0 &&
                            result.ExtraCount == 0 &&
                            result.DuplicateCount == 0 &&
                            result.SelfPairCount == 0;

            return result;
        }

        private static void CopyUniqueValid(
            NativeList<BodyPair> input,
            NativeList<BodyPair> output,
            ref PairValidationResult result)
        {
            BodyPair previous = default;
            bool hasPrevious = false;

            for (int i = 0; i < input.Length; i++)
            {
                BodyPair pair = input[i];
                if (pair.A == pair.B || pair.A > pair.B)
                {
                    result.SelfPairCount++;
                    continue;
                }

                if (hasPrevious && pair.CompareTo(previous) == 0)
                {
                    result.DuplicateCount++;
                    continue;
                }

                output.Add(pair);
                previous = pair;
                hasPrevious = true;
            }
        }
    }
}
