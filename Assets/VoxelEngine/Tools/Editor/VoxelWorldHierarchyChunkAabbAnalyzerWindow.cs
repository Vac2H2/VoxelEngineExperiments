using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;
using VoxelEngine.Data.VoxelWorldHierarchy;

namespace VoxelEngine.Editor.Tools
{
    public sealed class VoxelWorldHierarchyChunkAabbAnalyzerWindow : EditorWindow
    {
        [SerializeField] private VoxelWorldHierarchy _hierarchy;

        private AnalysisReport _report;
        private Vector2 _scrollPosition;

        [MenuItem("VoxelEngine/Tools/Analyze Hierarchy Chunk AABBs")]
        public static void Open()
        {
            VoxelWorldHierarchyChunkAabbAnalyzerWindow window = GetWindow<VoxelWorldHierarchyChunkAabbAnalyzerWindow>();
            window.titleContent = new GUIContent("Hierarchy AABB Stats");
            window.minSize = new Vector2(640f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_hierarchy == null && Selection.activeObject is VoxelWorldHierarchy selectedHierarchy)
            {
                _hierarchy = selectedHierarchy;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            VoxelWorldHierarchy nextHierarchy = (VoxelWorldHierarchy)EditorGUILayout.ObjectField(
                "Hierarchy",
                _hierarchy,
                typeof(VoxelWorldHierarchy),
                false);
            if (!ReferenceEquals(nextHierarchy, _hierarchy))
            {
                _hierarchy = nextHierarchy;
                _report = null;
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_hierarchy == null))
            {
                if (GUILayout.Button("Analyze", GUILayout.Height(32f)))
                {
                    Analyze();
                }
            }

            if (_hierarchy == null)
            {
                EditorGUILayout.HelpBox("Select a VoxelWorldHierarchy asset to inspect its chunk AABB statistics.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            if (_report == null)
            {
                EditorGUILayout.HelpBox("Press Analyze to build a summary for the selected hierarchy.", MessageType.None);
                return;
            }

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scrollScope.scrollPosition;
                DrawSummary(_report);
                EditorGUILayout.Space();
                DrawPerModelTable(_report);
                DrawIssues(_report);
            }
        }

        private void Analyze()
        {
            try
            {
                _report = BuildReport(_hierarchy);
            }
            catch (Exception exception)
            {
                _report = null;
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Hierarchy Chunk AABB Analyzer", exception.Message, "OK");
            }
        }

        private static AnalysisReport BuildReport(VoxelWorldHierarchy hierarchy)
        {
            if (hierarchy == null)
            {
                throw new ArgumentNullException(nameof(hierarchy));
            }

            Dictionary<string, int> instanceCountByModelGuid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int skippedHiddenNodeCount = 0;
            int renderableNodeCount = 0;

            int[] rootNodeIndices = hierarchy.RootNodeIndices;
            for (int i = 0; i < rootNodeIndices.Length; i++)
            {
                AppendVisibleModelReferencesRecursive(
                    hierarchy,
                    rootNodeIndices[i],
                    ancestorHidden: false,
                    instanceCountByModelGuid,
                    ref renderableNodeCount,
                    ref skippedHiddenNodeCount);
            }

            List<ModelAnalysisEntry> entries = new List<ModelAnalysisEntry>(instanceCountByModelGuid.Count);
            List<string> issues = new List<string>();
            long totalUniqueChunkCount = 0L;
            long totalUniqueAabbCount = 0L;
            long totalInstancedChunkCount = 0L;
            long totalInstancedAabbCount = 0L;

            foreach ((string modelGuid, int instanceCount) in instanceCountByModelGuid)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(modelGuid);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    issues.Add($"Missing VoxelModelAsset for GUID '{modelGuid}'.");
                    continue;
                }

                VoxelModelAsset modelAsset = AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(assetPath);
                if (modelAsset == null)
                {
                    issues.Add($"Failed to load VoxelModelAsset at '{assetPath}'.");
                    continue;
                }

                ModelChunkStats stats;
                try
                {
                    stats = AnalyzeModel(modelAsset);
                }
                catch (Exception exception)
                {
                    issues.Add($"Failed to analyze '{assetPath}': {exception.Message}");
                    continue;
                }

                totalUniqueChunkCount += stats.TotalChunkCount;
                totalUniqueAabbCount += stats.TotalActiveAabbCount;
                totalInstancedChunkCount += (long)stats.TotalChunkCount * instanceCount;
                totalInstancedAabbCount += (long)stats.TotalActiveAabbCount * instanceCount;

                entries.Add(new ModelAnalysisEntry(modelAsset, assetPath, instanceCount, stats));
            }

            entries.Sort((left, right) =>
            {
                int instanceComparison = right.InstanceCount.CompareTo(left.InstanceCount);
                if (instanceComparison != 0)
                {
                    return instanceComparison;
                }

                int aabbComparison = right.Stats.AverageActiveAabbsPerChunk.CompareTo(left.Stats.AverageActiveAabbsPerChunk);
                if (aabbComparison != 0)
                {
                    return aabbComparison;
                }

                return string.Compare(left.AssetPath, right.AssetPath, StringComparison.OrdinalIgnoreCase);
            });

            return new AnalysisReport(
                hierarchy,
                renderableNodeCount,
                skippedHiddenNodeCount,
                entries,
                issues,
                totalUniqueChunkCount,
                totalUniqueAabbCount,
                totalInstancedChunkCount,
                totalInstancedAabbCount);
        }

        private static void AppendVisibleModelReferencesRecursive(
            VoxelWorldHierarchy hierarchy,
            int nodeIndex,
            bool ancestorHidden,
            IDictionary<string, int> instanceCountByModelGuid,
            ref int renderableNodeCount,
            ref int skippedHiddenNodeCount)
        {
            if (!hierarchy.TryGetNode(nodeIndex, out VoxelWorldHierarchyNode node))
            {
                return;
            }

            bool isHidden = ancestorHidden || node.Hidden;
            if (!isHidden && node.HasRenderableContent)
            {
                string modelGuid = node.ModelReference.AssetGUID;
                if (!string.IsNullOrWhiteSpace(modelGuid))
                {
                    renderableNodeCount++;
                    instanceCountByModelGuid.TryGetValue(modelGuid, out int currentCount);
                    instanceCountByModelGuid[modelGuid] = currentCount + 1;
                }
            }
            else if (isHidden && node.HasRenderableContent)
            {
                skippedHiddenNodeCount++;
            }

            int[] childIndices = node.ChildIndices;
            for (int i = 0; i < childIndices.Length; i++)
            {
                AppendVisibleModelReferencesRecursive(
                    hierarchy,
                    childIndices[i],
                    isHidden,
                    instanceCountByModelGuid,
                    ref renderableNodeCount,
                    ref skippedHiddenNodeCount);
            }
        }

        private static ModelChunkStats AnalyzeModel(VoxelModelAsset modelAsset)
        {
            using VoxelModel model = VoxelModelSerializer.Deserialize(modelAsset, Allocator.Temp);

            VolumeChunkStats opaqueStats = AnalyzeVolume(model.OpaqueVolume);
            VolumeChunkStats transparentStats = AnalyzeVolume(model.TransparentVolume);
            int totalChunkCount = opaqueStats.ChunkCount + transparentStats.ChunkCount;
            int totalActiveAabbCount = opaqueStats.ActiveAabbCount + transparentStats.ActiveAabbCount;

            return new ModelChunkStats(
                opaqueStats.ChunkCount,
                transparentStats.ChunkCount,
                totalChunkCount,
                opaqueStats.ActiveAabbCount,
                transparentStats.ActiveAabbCount,
                totalActiveAabbCount);
        }

        private static VolumeChunkStats AnalyzeVolume(VoxelVolume volume)
        {
            int chunkCount = 0;
            int activeAabbCount = 0;

            for (int chunkIndex = 0; chunkIndex < volume.ChunkCapacity; chunkIndex++)
            {
                if (!volume.IsChunkAllocated(chunkIndex))
                {
                    continue;
                }

                chunkCount++;
                activeAabbCount += CountActiveAabbs(volume, chunkIndex);
            }

            return new VolumeChunkStats(chunkCount, activeAabbCount);
        }

        private static int CountActiveAabbs(VoxelVolume volume, int chunkIndex)
        {
            int activeCount = 0;
            var aabbSlice = volume.GetChunkAabbSlice(chunkIndex);
            for (int i = 0; i < aabbSlice.Length; i++)
            {
                if (aabbSlice[i].IsActive != 0)
                {
                    activeCount++;
                }
            }

            return activeCount;
        }

        private static void DrawSummary(AnalysisReport report)
        {
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Hierarchy", report.Hierarchy.name);
            EditorGUILayout.LabelField("Visible Renderable Nodes", report.RenderableNodeCount.ToString());
            EditorGUILayout.LabelField("Hidden Renderable Nodes Skipped", report.HiddenRenderableNodeCount.ToString());
            EditorGUILayout.LabelField("Unique Models", report.ModelEntries.Count.ToString());

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Unique Model Chunks", report.TotalUniqueChunkCount.ToString());
            EditorGUILayout.LabelField("Unique Model Active AABBs", report.TotalUniqueActiveAabbCount.ToString());
            EditorGUILayout.LabelField("Unique Avg AABBs / Chunk", report.UniqueAverageActiveAabbsPerChunk.ToString("F3"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Instanced World Chunks", report.TotalInstancedChunkCount.ToString());
            EditorGUILayout.LabelField("Instanced World Active AABBs", report.TotalInstancedActiveAabbCount.ToString());
            EditorGUILayout.LabelField("Instanced Avg AABBs / Chunk", report.InstancedAverageActiveAabbsPerChunk.ToString("F3"));
        }

        private static void DrawPerModelTable(AnalysisReport report)
        {
            EditorGUILayout.LabelField("Per Model", EditorStyles.boldLabel);

            if (report.ModelEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("No visible renderable models were found in this hierarchy.", MessageType.None);
                return;
            }

            for (int i = 0; i < report.ModelEntries.Count; i++)
            {
                ModelAnalysisEntry entry = report.ModelEntries[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField("Model", entry.Asset, typeof(VoxelModelAsset), false);
                    EditorGUILayout.LabelField("Instances", entry.InstanceCount.ToString());
                    EditorGUILayout.LabelField("Opaque Chunks", entry.Stats.OpaqueChunkCount.ToString());
                    EditorGUILayout.LabelField("Transparent Chunks", entry.Stats.TransparentChunkCount.ToString());
                    EditorGUILayout.LabelField("Total Chunks", entry.Stats.TotalChunkCount.ToString());
                    EditorGUILayout.LabelField("Opaque Active AABBs", entry.Stats.OpaqueActiveAabbCount.ToString());
                    EditorGUILayout.LabelField("Transparent Active AABBs", entry.Stats.TransparentActiveAabbCount.ToString());
                    EditorGUILayout.LabelField("Total Active AABBs", entry.Stats.TotalActiveAabbCount.ToString());
                    EditorGUILayout.LabelField("Avg AABBs / Chunk", entry.Stats.AverageActiveAabbsPerChunk.ToString("F3"));
                }
            }
        }

        private static void DrawIssues(AnalysisReport report)
        {
            if (report.Issues.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Issues", EditorStyles.boldLabel);
            for (int i = 0; i < report.Issues.Count; i++)
            {
                EditorGUILayout.HelpBox(report.Issues[i], MessageType.Warning);
            }
        }

        private readonly struct VolumeChunkStats
        {
            public VolumeChunkStats(int chunkCount, int activeAabbCount)
            {
                ChunkCount = chunkCount;
                ActiveAabbCount = activeAabbCount;
            }

            public int ChunkCount { get; }

            public int ActiveAabbCount { get; }
        }

        private readonly struct ModelChunkStats
        {
            public ModelChunkStats(
                int opaqueChunkCount,
                int transparentChunkCount,
                int totalChunkCount,
                int opaqueActiveAabbCount,
                int transparentActiveAabbCount,
                int totalActiveAabbCount)
            {
                OpaqueChunkCount = opaqueChunkCount;
                TransparentChunkCount = transparentChunkCount;
                TotalChunkCount = totalChunkCount;
                OpaqueActiveAabbCount = opaqueActiveAabbCount;
                TransparentActiveAabbCount = transparentActiveAabbCount;
                TotalActiveAabbCount = totalActiveAabbCount;
            }

            public int OpaqueChunkCount { get; }

            public int TransparentChunkCount { get; }

            public int TotalChunkCount { get; }

            public int OpaqueActiveAabbCount { get; }

            public int TransparentActiveAabbCount { get; }

            public int TotalActiveAabbCount { get; }

            public float AverageActiveAabbsPerChunk => TotalChunkCount <= 0 ? 0f : (float)TotalActiveAabbCount / TotalChunkCount;
        }

        private sealed class ModelAnalysisEntry
        {
            public ModelAnalysisEntry(VoxelModelAsset asset, string assetPath, int instanceCount, ModelChunkStats stats)
            {
                Asset = asset;
                AssetPath = assetPath ?? string.Empty;
                InstanceCount = instanceCount;
                Stats = stats;
            }

            public VoxelModelAsset Asset { get; }

            public string AssetPath { get; }

            public int InstanceCount { get; }

            public ModelChunkStats Stats { get; }
        }

        private sealed class AnalysisReport
        {
            public AnalysisReport(
                VoxelWorldHierarchy hierarchy,
                int renderableNodeCount,
                int hiddenRenderableNodeCount,
                List<ModelAnalysisEntry> modelEntries,
                List<string> issues,
                long totalUniqueChunkCount,
                long totalUniqueActiveAabbCount,
                long totalInstancedChunkCount,
                long totalInstancedActiveAabbCount)
            {
                Hierarchy = hierarchy;
                RenderableNodeCount = renderableNodeCount;
                HiddenRenderableNodeCount = hiddenRenderableNodeCount;
                ModelEntries = modelEntries ?? new List<ModelAnalysisEntry>();
                Issues = issues ?? new List<string>();
                TotalUniqueChunkCount = totalUniqueChunkCount;
                TotalUniqueActiveAabbCount = totalUniqueActiveAabbCount;
                TotalInstancedChunkCount = totalInstancedChunkCount;
                TotalInstancedActiveAabbCount = totalInstancedActiveAabbCount;
            }

            public VoxelWorldHierarchy Hierarchy { get; }

            public int RenderableNodeCount { get; }

            public int HiddenRenderableNodeCount { get; }

            public List<ModelAnalysisEntry> ModelEntries { get; }

            public List<string> Issues { get; }

            public long TotalUniqueChunkCount { get; }

            public long TotalUniqueActiveAabbCount { get; }

            public long TotalInstancedChunkCount { get; }

            public long TotalInstancedActiveAabbCount { get; }

            public float UniqueAverageActiveAabbsPerChunk =>
                TotalUniqueChunkCount <= 0L ? 0f : (float)TotalUniqueActiveAabbCount / TotalUniqueChunkCount;

            public float InstancedAverageActiveAabbsPerChunk =>
                TotalInstancedChunkCount <= 0L ? 0f : (float)TotalInstancedActiveAabbCount / TotalInstancedChunkCount;
        }
    }
}
