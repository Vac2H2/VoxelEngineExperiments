using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VoxelEngineDOTS.BVH
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TlasBvhDemoSettings))]
    public class TlasBvhDemoController : MonoBehaviour
    {
        [SerializeField]
        private TlasBvhDemoSettings settings;

        private NativeArray<BodyState> bodies;
        private NativeArray<BodyProxy> proxies;
        private NativeArray<BvhNode> nodes;
        private NativeArray<int> levelA;
        private NativeArray<int> levelB;
        private NativeArray<QueryStats> perSeedStats;
        private NativeArray<NodePair> stackBuffer;
        private NativeList<BodyPair> queryPairs;
        private NativeList<BodyPair> naivePairs;
        private NativeList<NodePair> nodePairStack;
        private NativeList<int> intStack;
        private NativeList<NodePair> seedPairs;

        private readonly Stopwatch stepWatch = new Stopwatch();
        private readonly Stopwatch totalWatch = new Stopwatch();
        private readonly List<GameObject> bodyRenderers = new List<GameObject>();
        private readonly List<double> benchmarkBuildSamples = new List<double>();
        private readonly List<double> benchmarkQuerySamples = new List<double>();
        private readonly List<double> benchmarkTotalSamples = new List<double>();

        private Material bodyMaterial;
        private Material collidingBodyMaterial;
        private bool[] collidingBodies;
        private FrameStats frameStats;
        private PairValidationResult validationResult;
        private QueryStats queryStats;
        private int rootNodeIndex = -1;
        private int leafCount;
        private int nodeCount;
        private int allocatedBodyCount = -1;
        private int allocatedLeafCapacity = -1;
        private int allocatedNodeCapacity = -1;
        private int allocatedSeedCapacity = -1;
        private int allocatedStackCapacity = -1;
        private int lastGenerationHash;
        private bool rebuildBodies = true;
        private bool benchmarkActive;
        private int benchmarkFrame;

        private void OnEnable()
        {
            settings = settings != null ? settings : GetComponent<TlasBvhDemoSettings>();
            EnsureMaterials();
            EnsureCapacity();
            RebuildBodies();
            if (settings.AutoRunBenchmark)
            {
                StartBenchmark();
            }
        }

        private void OnDisable()
        {
            DisposeNativeData();
            DisposeRenderObjects();
            DisposeMaterials();
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            settings.Sanitize();
            BurstCompiler.Options.EnableBurstCompilation = settings.UseBurst;

            EnsureCapacity();
            int generationHash = ComputeGenerationHash();
            if (rebuildBodies || generationHash != lastGenerationHash)
            {
                RebuildBodies();
            }

            RunFrame();
            UpdateBenchmark();
        }

        private void RunFrame()
        {
            frameStats = default;
            queryStats = default;
            validationResult = default;
            frameStats.BodyCount = settings.BodyCount;
            frameStats.Fps = Time.unscaledDeltaTime > 0.0f ? 1.0f / Time.unscaledDeltaTime : 0.0f;

            totalWatch.Restart();

            stepWatch.Restart();
            SimulateBodies();
            frameStats.SimulateMs = RestartStep();

            stepWatch.Restart();
            ComputeProxies();
            frameStats.ProxyMs = RestartStep();

            stepWatch.Restart();
            SortProxies();
            frameStats.SortMs = RestartStep();

            stepWatch.Restart();
            BuildTlas();
            frameStats.BuildMs = RestartStep();

            QueryBodyPairs();

            stepWatch.Restart();
            ValidateIfRequested();
            frameStats.ValidateMs = RestartStep();

            stepWatch.Restart();
            UpdateRendering();
            frameStats.RenderMs = RestartStep();

            totalWatch.Stop();
            frameStats.TotalMs = totalWatch.Elapsed.TotalMilliseconds;
            frameStats.LeafCount = leafCount;
            frameStats.NodeCount = nodeCount;
            frameStats.BodyPairCount = queryPairs.IsCreated ? queryPairs.Length : 0;
            frameStats.NodePairTests = queryStats.NodePairTests;
            frameStats.AabbPruned = queryStats.AabbPruned;
            frameStats.LeafInternalChecks = queryStats.LeafInternalChecks;
            frameStats.LeafLeafChecks = queryStats.LeafLeafChecks;
            frameStats.BodyAabbTests = queryStats.BodyAabbTests;
            frameStats.StackOverflowCount = queryStats.StackOverflow;
            frameStats.OutputOverflowCount = queryStats.OutputOverflow;
            frameStats.BuildAndFindPairsMs =
                frameStats.BuildMs +
                frameStats.SeedMs +
                frameStats.QueryMs +
                frameStats.ReadbackMs;
        }

        private void SimulateBodies()
        {
            if (!bodies.IsCreated || bodies.Length == 0)
            {
                return;
            }

            if (settings.UseJobs)
            {
                new BodySimulationJob
                {
                    Bodies = bodies,
                    DeltaTime = Time.deltaTime,
                    WorldSize = settings.WorldSize
                }.Schedule(bodies.Length, settings.InnerLoopBatchCount).Complete();
                return;
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                BodySimulationUtility.SimulateBody(bodies, i, Time.deltaTime, settings.WorldSize);
            }
        }

        private void ComputeProxies()
        {
            if (!bodies.IsCreated || bodies.Length == 0)
            {
                return;
            }

            float halfWorld = settings.WorldSize * 0.5f;
            float3 worldMin = new float3(-halfWorld);
            float3 worldMax = new float3(halfWorld);

            if (settings.UseJobs)
            {
                new ComputeBodyProxiesJob
                {
                    Bodies = bodies,
                    Proxies = proxies,
                    WorldMin = worldMin,
                    WorldMax = worldMax,
                    MortonBitsPerAxis = settings.MortonBitsPerAxis
                }.Schedule(bodies.Length, settings.InnerLoopBatchCount).Complete();
                return;
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                BvhBuildUtility.ComputeProxy(bodies, proxies, i, worldMin, worldMax, settings.MortonBitsPerAxis);
            }
        }

        private void SortProxies()
        {
            if (!proxies.IsCreated || proxies.Length <= 1)
            {
                return;
            }

            if (settings.UseJobs)
            {
                proxies.SortJob().Schedule().Complete();
            }
            else
            {
                proxies.Sort();
            }
        }

        private void BuildTlas()
        {
            TlasBuilder.Build(
                proxies,
                settings.BodyCount,
                settings.LeafSize,
                nodes,
                levelA,
                levelB,
                settings.UseJobs,
                settings.InnerLoopBatchCount,
                out rootNodeIndex,
                out leafCount,
                out nodeCount);
        }

        private void QueryBodyPairs()
        {
            queryPairs.Clear();
            queryStats = default;
            frameStats.SeedCount = 0;
            frameStats.SeedMs = 0.0;
            frameStats.QueryMs = 0.0;
            frameStats.ReadbackMs = 0.0;

            if (settings.BodyCount <= 1 || rootNodeIndex < 0)
            {
                return;
            }

            switch (settings.QueryMode)
            {
                case BvhQueryMode.NaiveAllPairs:
                    stepWatch.Restart();
                    NaiveBroadphase.Query(proxies, settings.BodyCount, queryPairs, settings.MaxOutputPairs, out queryStats);
                    frameStats.QueryMs = RestartStep();
                    break;
                case BvhQueryMode.BodyVsTreeReference:
                    stepWatch.Restart();
                    BvhSelfQuery.BodyVsTreeReferenceQuery(
                        nodes,
                        rootNodeIndex,
                        proxies,
                        settings.BodyCount,
                        queryPairs,
                        intStack,
                        settings.MaxOutputPairs,
                        out queryStats);
                    frameStats.QueryMs = RestartStep();
                    break;
                case BvhQueryMode.SingleThreadNodePairSelfQuery:
                    stepWatch.Restart();
                    BvhSelfQuery.SingleThreadQuery(
                        nodes,
                        rootNodeIndex,
                        proxies,
                        queryPairs,
                        nodePairStack,
                        settings.MaxOutputPairs,
                        out queryStats);
                    frameStats.QueryMs = RestartStep();
                    break;
                case BvhQueryMode.ParallelNodePairSelfQuery:
                    if (settings.UseJobs)
                    {
                        RunParallelQuery();
                    }
                    else
                    {
                        stepWatch.Restart();
                        BvhSelfQuery.SingleThreadQuery(
                            nodes,
                            rootNodeIndex,
                            proxies,
                            queryPairs,
                            nodePairStack,
                            settings.MaxOutputPairs,
                            out queryStats);
                        frameStats.QueryMs = RestartStep();
                    }
                    break;
            }
        }

        private void RunParallelQuery()
        {
            stepWatch.Restart();
            int workerCount = math.max(1, SystemInfo.processorCount - 1);
            int targetSeedCount = math.max(1, workerCount * settings.SeedMultiplier);
            BvhSelfQuery.GenerateSeedPairs(nodes, rootNodeIndex, targetSeedCount, seedPairs, nodePairStack);
            frameStats.SeedMs = RestartStep();
            frameStats.SeedCount = seedPairs.Length;

            if (seedPairs.Length == 0)
            {
                return;
            }

            EnsureParallelBuffers(seedPairs.Length, settings.LocalStackCapacity);

            using NativeStream pairStream = new NativeStream(seedPairs.Length, Allocator.TempJob);

            stepWatch.Restart();
            new TraverseSeedPairsJob
            {
                Nodes = nodes,
                SortedProxies = proxies,
                SeedPairs = seedPairs.AsArray(),
                PairWriter = pairStream.AsWriter(),
                PerSeedStats = perSeedStats,
                StackBuffer = stackBuffer,
                LocalStackCapacity = settings.LocalStackCapacity,
                MaxPairsPerSeed = math.max(1, (settings.MaxOutputPairs + seedPairs.Length - 1) / seedPairs.Length)
            }.Schedule(seedPairs.Length, settings.InnerLoopBatchCount).Complete();
            frameStats.QueryMs = RestartStep();

            stepWatch.Restart();
            int readbackOverflow = ReadPairStream(pairStream, seedPairs.Length);
            ReduceSeedStats(seedPairs.Length);
            queryStats.OutputOverflow += readbackOverflow;
            frameStats.ReadbackMs = RestartStep();
        }

        private int ReadPairStream(NativeStream pairStream, int seedCount)
        {
            int overflow = 0;
            NativeStream.Reader reader = pairStream.AsReader();
            for (int i = 0; i < seedCount; i++)
            {
                reader.BeginForEachIndex(i);
                while (reader.RemainingItemCount > 0)
                {
                    BodyPair pair = reader.Read<BodyPair>();
                    if (queryPairs.Length < settings.MaxOutputPairs)
                    {
                        queryPairs.Add(pair);
                    }
                    else
                    {
                        overflow++;
                    }
                }

                reader.EndForEachIndex();
            }

            return overflow;
        }

        private void ReduceSeedStats(int seedCount)
        {
            queryStats = default;
            for (int i = 0; i < seedCount; i++)
            {
                queryStats.Add(perSeedStats[i]);
            }
        }

        private void ValidateIfRequested()
        {
            validationResult = default;
            if (!settings.ValidateAgainstNaive || settings.BodyCount > settings.MaxNaiveValidationBodies)
            {
                return;
            }

            QueryStats naiveStats;
            NaiveBroadphase.Query(proxies, settings.BodyCount, naivePairs, settings.MaxOutputPairs, out naiveStats);
            validationResult = PairValidation.Validate(queryPairs, naivePairs);
        }

        private void UpdateRendering()
        {
            if (!settings.RenderBodies || settings.MaxRenderedBodies <= 0)
            {
                SetRenderObjectsActive(0);
                return;
            }

            int renderCount = math.min(settings.BodyCount, settings.MaxRenderedBodies);
            EnsureRenderObjects(renderCount);
            UpdateCollidingFlags();

            for (int i = 0; i < bodyRenderers.Count; i++)
            {
                GameObject bodyObject = bodyRenderers[i];
                bool active = i < renderCount;
                if (bodyObject.activeSelf != active)
                {
                    bodyObject.SetActive(active);
                }

                if (!active)
                {
                    continue;
                }

                BodyState body = bodies[i];
                bodyObject.transform.localPosition = new Vector3(body.Position.x, body.Position.y, body.Position.z);
                bodyObject.transform.localScale = new Vector3(body.HalfSize.x * 2.0f, body.HalfSize.y * 2.0f, body.HalfSize.z * 2.0f);

                MeshRenderer renderer = bodyObject.GetComponent<MeshRenderer>();
                bool colliding = settings.MarkCollidingBodies &&
                                 collidingBodies != null &&
                                 body.BodyId < collidingBodies.Length &&
                                 collidingBodies[body.BodyId];
                renderer.sharedMaterial = colliding ? collidingBodyMaterial : bodyMaterial;
            }
        }

        private void UpdateCollidingFlags()
        {
            if (!settings.MarkCollidingBodies)
            {
                return;
            }

            if (collidingBodies == null || collidingBodies.Length != settings.BodyCount)
            {
                collidingBodies = new bool[settings.BodyCount];
            }
            else
            {
                Array.Clear(collidingBodies, 0, collidingBodies.Length);
            }

            for (int i = 0; i < queryPairs.Length; i++)
            {
                BodyPair pair = queryPairs[i];
                if (pair.A >= 0 && pair.A < collidingBodies.Length)
                {
                    collidingBodies[pair.A] = true;
                }

                if (pair.B >= 0 && pair.B < collidingBodies.Length)
                {
                    collidingBodies[pair.B] = true;
                }
            }
        }

        private void OnGUI()
        {
            const int width = 430;
            GUILayout.BeginArea(new Rect(12, 12, width, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("TLAS BVH Self-Collision Demo");
            GUILayout.Label($"Bodies: {frameStats.BodyCount:N0}  Leaves: {frameStats.LeafCount:N0}  Nodes: {frameStats.NodeCount:N0}");
            GUILayout.Label($"Mode: {settings.QueryMode}  Leaf: {settings.LeafSize}  Seeds: {frameStats.SeedCount:N0}");
            GUILayout.Label($"Pairs: {frameStats.BodyPairCount:N0}  FPS: {frameStats.Fps:0.0}");
            GUILayout.Space(4);
            GUILayout.Label($"Sim {frameStats.SimulateMs:0.000} ms | Proxy {frameStats.ProxyMs:0.000} ms | Sort {frameStats.SortMs:0.000} ms");
            GUILayout.Label($"Build {frameStats.BuildMs:0.000} ms | Seed {frameStats.SeedMs:0.000} ms | Query {frameStats.QueryMs:0.000} ms");
            GUILayout.Label($"Read {frameStats.ReadbackMs:0.000} ms | Validate {frameStats.ValidateMs:0.000} ms | Total {frameStats.TotalMs:0.000} ms");
            GUILayout.Label($"Build + Seed + Query + Read: {frameStats.BuildAndFindPairsMs:0.000} ms");
            GUILayout.Space(4);
            GUILayout.Label($"NodePairs {frameStats.NodePairTests:N0} | Pruned {frameStats.AabbPruned:N0} | BodyTests {frameStats.BodyAabbTests:N0}");
            GUILayout.Label($"LeafInternal {frameStats.LeafInternalChecks:N0} | LeafLeaf {frameStats.LeafLeafChecks:N0} | StackOverflow {frameStats.StackOverflowCount:N0}");
            GUILayout.Label($"OutputOverflow {frameStats.OutputOverflowCount:N0} | MaxPairs {settings.MaxOutputPairs:N0}");
            GUILayout.Space(4);
            DrawValidationGui();
            GUILayout.Space(6);
            DrawControlGui();
            GUILayout.EndArea();
        }

        private void DrawValidationGui()
        {
            if (!settings.ValidateAgainstNaive)
            {
                GUILayout.Label("Validation: disabled");
                return;
            }

            if (settings.BodyCount > settings.MaxNaiveValidationBodies)
            {
                GUILayout.Label($"Validation: skipped above {settings.MaxNaiveValidationBodies:N0} bodies");
                return;
            }

            string status = validationResult.Ran ? (validationResult.Passed ? "PASS" : "FAIL") : "pending";
            GUILayout.Label($"Validation: {status}  Naive {validationResult.NaivePairCount:N0}  Query {validationResult.QueryPairCount:N0}");
            GUILayout.Label($"Missing {validationResult.MissingCount:N0} | Extra {validationResult.ExtraCount:N0} | Duplicates {validationResult.DuplicateCount:N0} | Self {validationResult.SelfPairCount:N0}");
        }

        private void DrawControlGui()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild Bodies"))
            {
                rebuildBodies = true;
            }

            if (GUILayout.Button("Randomize Seed"))
            {
                settings.RandomSeed = UnityEngine.Random.Range(1, int.MaxValue);
                rebuildBodies = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(settings.RenderBodies ? "Rendering: On" : "Rendering: Off"))
            {
                settings.RenderBodies = !settings.RenderBodies;
            }

            if (GUILayout.Button(settings.ValidateAgainstNaive ? "Validation: On" : "Validation: Off"))
            {
                settings.ValidateAgainstNaive = !settings.ValidateAgainstNaive;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cycle Query"))
            {
                settings.QueryMode = (BvhQueryMode)(((int)settings.QueryMode + 1) % Enum.GetValues(typeof(BvhQueryMode)).Length);
            }

            if (GUILayout.Button(benchmarkActive ? "Benchmarking..." : "Run Benchmark"))
            {
                StartBenchmark();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("- Bodies"))
            {
                settings.BodyCount = math.max(0, settings.BodyCount / 2);
                rebuildBodies = true;
            }

            if (GUILayout.Button("+ Bodies"))
            {
                settings.BodyCount = math.max(1, settings.BodyCount * 2);
                rebuildBodies = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("- Leaf"))
            {
                settings.LeafSize = math.max(1, settings.LeafSize / 2);
            }

            if (GUILayout.Button("+ Leaf"))
            {
                settings.LeafSize = math.max(1, settings.LeafSize * 2);
            }
            GUILayout.EndHorizontal();
        }

        private void StartBenchmark()
        {
            benchmarkActive = true;
            benchmarkFrame = 0;
            benchmarkBuildSamples.Clear();
            benchmarkQuerySamples.Clear();
            benchmarkTotalSamples.Clear();
        }

        private void UpdateBenchmark()
        {
            if (!benchmarkActive)
            {
                return;
            }

            benchmarkFrame++;
            if (benchmarkFrame <= settings.WarmupFrames)
            {
                return;
            }

            benchmarkBuildSamples.Add(frameStats.BuildMs);
            benchmarkQuerySamples.Add(frameStats.QueryMs);
            benchmarkTotalSamples.Add(frameStats.TotalMs);

            if (benchmarkBuildSamples.Count >= settings.SampleFrames)
            {
                benchmarkActive = false;
                Debug.Log(
                    $"TLAS BVH Benchmark bodies={settings.BodyCount} leaf={settings.LeafSize} mode={settings.QueryMode} " +
                    $"buildAvg={Average(benchmarkBuildSamples):0.000}ms buildP95={Percentile(benchmarkBuildSamples, 0.95):0.000}ms " +
                    $"queryAvg={Average(benchmarkQuerySamples):0.000}ms queryP95={Percentile(benchmarkQuerySamples, 0.95):0.000}ms " +
                    $"totalAvg={Average(benchmarkTotalSamples):0.000}ms totalP95={Percentile(benchmarkTotalSamples, 0.95):0.000}ms " +
                    $"pairs={frameStats.BodyPairCount}");
            }
        }

        private static double Average(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0.0;
            }

            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }

            return sum / values.Count;
        }

        private static double Percentile(List<double> values, double percentile)
        {
            if (values.Count == 0)
            {
                return 0.0;
            }

            List<double> sorted = new List<double>(values);
            sorted.Sort();
            int index = (int)math.clamp(math.ceil((float)(percentile * sorted.Count)) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }

        private void EnsureCapacity()
        {
            int bodyCount = math.max(0, settings.BodyCount);
            int leafSize = math.max(1, settings.LeafSize);
            int leafCapacity = math.max(1, (bodyCount + leafSize - 1) / leafSize);
            int nodeCapacity = math.max(1, leafCapacity * 2 - 1);

            if (allocatedBodyCount == bodyCount &&
                allocatedLeafCapacity == leafCapacity &&
                allocatedNodeCapacity == nodeCapacity &&
                queryPairs.IsCreated)
            {
                return;
            }

            DisposeNativeData();

            bodies = new NativeArray<BodyState>(bodyCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            proxies = new NativeArray<BodyProxy>(bodyCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            nodes = new NativeArray<BvhNode>(nodeCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            levelA = new NativeArray<int>(leafCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            levelB = new NativeArray<int>(leafCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            queryPairs = new NativeList<BodyPair>(math.max(1, math.min(settings.MaxOutputPairs, 1024)), Allocator.Persistent);
            naivePairs = new NativeList<BodyPair>(math.max(1, math.min(settings.MaxOutputPairs, 1024)), Allocator.Persistent);
            nodePairStack = new NativeList<NodePair>(math.max(64, leafCapacity), Allocator.Persistent);
            intStack = new NativeList<int>(math.max(64, leafCapacity), Allocator.Persistent);
            seedPairs = new NativeList<NodePair>(math.max(64, leafCapacity), Allocator.Persistent);

            allocatedBodyCount = bodyCount;
            allocatedLeafCapacity = leafCapacity;
            allocatedNodeCapacity = nodeCapacity;
            allocatedSeedCapacity = -1;
            allocatedStackCapacity = -1;
            rebuildBodies = true;
        }

        private void EnsureParallelBuffers(int seedCount, int localStackCapacity)
        {
            int stackCapacity = math.max(1, seedCount * localStackCapacity);
            if (perSeedStats.IsCreated &&
                allocatedSeedCapacity == seedCount &&
                allocatedStackCapacity == stackCapacity)
            {
                return;
            }

            if (perSeedStats.IsCreated)
            {
                perSeedStats.Dispose();
            }

            if (stackBuffer.IsCreated)
            {
                stackBuffer.Dispose();
            }

            perSeedStats = new NativeArray<QueryStats>(seedCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            stackBuffer = new NativeArray<NodePair>(stackCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            allocatedSeedCapacity = seedCount;
            allocatedStackCapacity = stackCapacity;
        }

        private void RebuildBodies()
        {
            if (bodies.IsCreated && bodies.Length > 0)
            {
                BodyGenerator.Generate(bodies, settings);
            }

            lastGenerationHash = ComputeGenerationHash();
            rebuildBodies = false;
        }

        private int ComputeGenerationHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + settings.BodyCount;
                hash = hash * 31 + settings.WorldSize.GetHashCode();
                hash = hash * 31 + settings.MinBodySize.GetHashCode();
                hash = hash * 31 + settings.MaxBodySize.GetHashCode();
                hash = hash * 31 + settings.MaxSpeed.GetHashCode();
                hash = hash * 31 + settings.RandomSeed;
                hash = hash * 31 + (int)settings.Distribution;
                hash = hash * 31 + settings.ClusterCount;
                hash = hash * 31 + settings.ClusterRadius.GetHashCode();
                hash = hash * 31 + settings.LargeBodyRatio.GetHashCode();
                return hash;
            }
        }

        private void EnsureMaterials()
        {
            if (bodyMaterial != null && collidingBodyMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }

            bodyMaterial = new Material(shader)
            {
                color = new Color(0.16f, 0.55f, 0.85f, 0.8f)
            };
            collidingBodyMaterial = new Material(shader)
            {
                color = new Color(0.95f, 0.18f, 0.12f, 0.95f)
            };
        }

        private void EnsureRenderObjects(int renderCount)
        {
            EnsureMaterials();
            while (bodyRenderers.Count < renderCount)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"TLAS Body {bodyRenderers.Count}";
                cube.transform.SetParent(transform, false);
                Collider collider = cube.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = bodyMaterial;
                bodyRenderers.Add(cube);
            }

            SetRenderObjectsActive(renderCount);
        }

        private void SetRenderObjectsActive(int activeCount)
        {
            for (int i = 0; i < bodyRenderers.Count; i++)
            {
                bool active = i < activeCount;
                if (bodyRenderers[i] != null && bodyRenderers[i].activeSelf != active)
                {
                    bodyRenderers[i].SetActive(active);
                }
            }
        }

        private void DisposeNativeData()
        {
            if (bodies.IsCreated)
            {
                bodies.Dispose();
            }

            if (proxies.IsCreated)
            {
                proxies.Dispose();
            }

            if (nodes.IsCreated)
            {
                nodes.Dispose();
            }

            if (levelA.IsCreated)
            {
                levelA.Dispose();
            }

            if (levelB.IsCreated)
            {
                levelB.Dispose();
            }

            if (perSeedStats.IsCreated)
            {
                perSeedStats.Dispose();
            }

            if (stackBuffer.IsCreated)
            {
                stackBuffer.Dispose();
            }

            if (queryPairs.IsCreated)
            {
                queryPairs.Dispose();
            }

            if (naivePairs.IsCreated)
            {
                naivePairs.Dispose();
            }

            if (nodePairStack.IsCreated)
            {
                nodePairStack.Dispose();
            }

            if (intStack.IsCreated)
            {
                intStack.Dispose();
            }

            if (seedPairs.IsCreated)
            {
                seedPairs.Dispose();
            }
        }

        private void DisposeRenderObjects()
        {
            for (int i = 0; i < bodyRenderers.Count; i++)
            {
                if (bodyRenderers[i] != null)
                {
                    Destroy(bodyRenderers[i]);
                }
            }

            bodyRenderers.Clear();
        }

        private void DisposeMaterials()
        {
            if (bodyMaterial != null)
            {
                Destroy(bodyMaterial);
                bodyMaterial = null;
            }

            if (collidingBodyMaterial != null)
            {
                Destroy(collidingBodyMaterial);
                collidingBodyMaterial = null;
            }
        }

        private double RestartStep()
        {
            stepWatch.Stop();
            double elapsed = stepWatch.Elapsed.TotalMilliseconds;
            stepWatch.Restart();
            return elapsed;
        }
    }
}
