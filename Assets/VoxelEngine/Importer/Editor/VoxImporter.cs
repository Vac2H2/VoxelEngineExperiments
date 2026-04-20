using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using VoxelEngine.Data.Voxel;
using VoxelEngine.Data.VoxelWorldHierarchy;

namespace VoxelEngine.Editor.Importer
{
    internal static class VoxImporter
    {
        private const string VoxExtension = ".vox";

        public static VoxelWorldHierarchy Import(string sourceFilePath, string targetFolderPath)
        {
            return Import(sourceFilePath, targetFolderPath, VoxImportOptions.Default);
        }

        public static VoxelWorldHierarchy Import(string sourceFilePath, string targetFolderPath, VoxImportOptions options)
        {
            ValidateImportArguments(sourceFilePath, targetFolderPath);

            VoxScene scene = VoxSceneParser.Parse(sourceFilePath);

            string importName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourceFilePath));
            string importFolderPath = EnsureImportFolder(targetFolderPath, importName);
            string palettePath = $"{importFolderPath}/{importName}_palette.asset";
            string hierarchyPath = $"{importFolderPath}/{importName}_hierarchy.asset";

            HashSet<string> keepAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                palettePath,
                hierarchyPath,
            };

            VoxelWorldHierarchy hierarchy = null;

            AssetDatabase.StartAssetEditing();
            try
            {
                ImportedPaletteAsset importedPalette = CreateOrUpdatePaletteAsset(palettePath, importName, scene.Palette);
                Dictionary<int, ImportedModelAsset> importedModels =
                    CreateOrUpdateModelAssets(scene, importFolderPath, importName, keepAssetPaths, options);
                hierarchy = CreateOrUpdateHierarchyAsset(
                    hierarchyPath,
                    sourceFilePath,
                    scene,
                    importedModels,
                    importedPalette.Reference);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            CleanupStaleModelAssets(importFolderPath, importName, keepAssetPaths);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(hierarchyPath, ImportAssetOptions.ForceUpdate);
            EmitWarnings(scene);

            hierarchy = AssetDatabase.LoadAssetAtPath<VoxelWorldHierarchy>(hierarchyPath);
            Selection.activeObject = hierarchy;
            EditorGUIUtility.PingObject(hierarchy);
            return hierarchy;
        }

        private static void ValidateImportArguments(string sourceFilePath, string targetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new ArgumentException("A source .vox file is required.", nameof(sourceFilePath));
            }

            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("The selected .vox file does not exist.", sourceFilePath);
            }

            if (!string.Equals(Path.GetExtension(sourceFilePath), VoxExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The selected source file must use the .vox extension.", nameof(sourceFilePath));
            }

            if (string.IsNullOrWhiteSpace(targetFolderPath) || !AssetDatabase.IsValidFolder(targetFolderPath))
            {
                throw new ArgumentException("A valid target folder inside the Unity project is required.", nameof(targetFolderPath));
            }
        }

        private static ImportedPaletteAsset CreateOrUpdatePaletteAsset(
            string assetPath,
            string importName,
            IReadOnlyList<Color32> paletteColors)
        {
            byte[] serializedBytes = BuildPaletteBytes(paletteColors);
            VoxelPaletteAsset asset = WritePaletteAsset(assetPath, serializedBytes);
            EnsureAddressableAsset(assetPath, $"{importName}_palette");
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            return new ImportedPaletteAsset(asset, new AssetReferenceVoxelPalette(guid));
        }

        private static Dictionary<int, ImportedModelAsset> CreateOrUpdateModelAssets(
            VoxScene scene,
            string importFolderPath,
            string importName,
            ISet<string> keepAssetPaths,
            VoxImportOptions options)
        {
            Dictionary<int, ImportedModelAsset> models = new Dictionary<int, ImportedModelAsset>(scene.Models.Count);

            foreach (VoxModelDefinition model in scene.Models)
            {
                if (!HasRenderableGeometry(model))
                {
                    scene.AddWarning($"Skipped model {model.Id} because it contains no voxels.");
                    continue;
                }

                string assetPath = $"{importFolderPath}/{importName}_model_{model.Id:D3}.asset";
                byte[] serializedBytes = BuildModelBytes(scene, model, options);
                VoxelModelAsset asset = WriteModelAsset(assetPath, serializedBytes);
                EnsureAddressableAsset(assetPath, $"{importName}_model_{model.Id:D3}");
                keepAssetPaths.Add(assetPath);

                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                models[model.Id] = new ImportedModelAsset(
                    asset,
                    new AssetReferenceVoxelModel(guid),
                    ComputeScenePivotOffset(model.Size));
            }

            return models;
        }

        private static VoxelWorldHierarchy CreateOrUpdateHierarchyAsset(
            string assetPath,
            string sourceFilePath,
            VoxScene scene,
            IReadOnlyDictionary<int, ImportedModelAsset> importedModels,
            AssetReferenceVoxelPalette paletteReference)
        {
            BuildHierarchyData(scene, importedModels, paletteReference, out VoxelWorldHierarchyNode[] nodes, out int[] rootNodeIndices);

            VoxelWorldHierarchy hierarchy = AssetDatabase.LoadAssetAtPath<VoxelWorldHierarchy>(assetPath);
            if (hierarchy == null)
            {
                hierarchy = ScriptableObject.CreateInstance<VoxelWorldHierarchy>();
                hierarchy.SetImportedData(sourceFilePath, nodes, rootNodeIndices);
                AssetDatabase.CreateAsset(hierarchy, assetPath);
            }
            else
            {
                hierarchy.SetImportedData(sourceFilePath, nodes, rootNodeIndices);
                EditorUtility.SetDirty(hierarchy);
            }

            EnsureAddressableAsset(assetPath, $"{SanitizeFileName(Path.GetFileNameWithoutExtension(assetPath))}");
            return hierarchy;
        }

        private static void BuildHierarchyData(
            VoxScene scene,
            IReadOnlyDictionary<int, ImportedModelAsset> importedModels,
            AssetReferenceVoxelPalette paletteReference,
            out VoxelWorldHierarchyNode[] nodes,
            out int[] rootNodeIndices)
        {
            List<int> sortedNodeIds = scene.Nodes.Keys.ToList();
            sortedNodeIds.Sort();

            Dictionary<int, int> nodeIndexBySourceId = new Dictionary<int, int>(sortedNodeIds.Count);
            for (int i = 0; i < sortedNodeIds.Count; i++)
            {
                nodeIndexBySourceId[sortedNodeIds[i]] = i;
            }

            Dictionary<int, int> parentSourceIdByChild = BuildParentLookup(scene);
            nodes = new VoxelWorldHierarchyNode[sortedNodeIds.Count];

            for (int i = 0; i < sortedNodeIds.Count; i++)
            {
                int sourceNodeId = sortedNodeIds[i];
                VoxNode sourceNode = scene.Nodes[sourceNodeId];
                int parentIndex = parentSourceIdByChild.TryGetValue(sourceNodeId, out int parentSourceId)
                    && nodeIndexBySourceId.TryGetValue(parentSourceId, out int mappedParentIndex)
                    ? mappedParentIndex
                    : -1;

                int[] childIndices = ResolveChildIndices(sourceNode, nodeIndexBySourceId);
                Vector3 localPosition = Vector3.zero;
                Quaternion localRotation = Quaternion.identity;
                Vector3 localScale = Vector3.one;
                Vector3 renderLocalOffset = Vector3.zero;
                bool hidden = sourceNode.Hidden;
                AssetReferenceVoxelModel modelReference = null;
                AssetReferenceVoxelPalette nodePaletteReference = null;

                if (sourceNode is VoxTransformNode transformNode)
                {
                    localPosition = transformNode.Translation;
                    localRotation = transformNode.Rotation;
                    hidden = VoxSceneParser.IsHidden(scene, transformNode);
                }
                else if (sourceNode is VoxShapeNode shapeNode)
                {
                    if (importedModels.TryGetValue(shapeNode.ModelId, out ImportedModelAsset importedModel))
                    {
                        modelReference = new AssetReferenceVoxelModel(importedModel.ModelReference.AssetGUID);
                        nodePaletteReference = new AssetReferenceVoxelPalette(paletteReference.AssetGUID);

                        if (parentSourceIdByChild.TryGetValue(sourceNodeId, out int directParentId)
                            && scene.Nodes.TryGetValue(directParentId, out VoxNode parentNode)
                            && parentNode is VoxTransformNode)
                        {
                            renderLocalOffset = -importedModel.ScenePivotOffset;
                        }
                    }
                    else
                    {
                        scene.AddWarning($"Shape node {shapeNode.NodeId} references missing model {shapeNode.ModelId}.");
                    }
                }

                nodes[i] = new VoxelWorldHierarchyNode(
                    sourceNodeId,
                    sourceNode.Name,
                    parentIndex,
                    childIndices,
                    localPosition,
                    localRotation,
                    localScale,
                    renderLocalOffset,
                    hidden,
                    modelReference,
                    nodePaletteReference);
            }

            rootNodeIndices = new int[scene.RootNodeIds.Count];
            for (int i = 0; i < scene.RootNodeIds.Count; i++)
            {
                rootNodeIndices[i] = nodeIndexBySourceId[scene.RootNodeIds[i]];
            }
        }

        private static Dictionary<int, int> BuildParentLookup(VoxScene scene)
        {
            Dictionary<int, int> parentByChild = new Dictionary<int, int>();

            foreach (VoxNode node in scene.Nodes.Values)
            {
                switch (node)
                {
                    case VoxTransformNode transformNode:
                        parentByChild[transformNode.ChildId] = transformNode.NodeId;
                        break;
                    case VoxGroupNode groupNode:
                        for (int i = 0; i < groupNode.ChildIds.Count; i++)
                        {
                            parentByChild[groupNode.ChildIds[i]] = groupNode.NodeId;
                        }

                        break;
                }
            }

            return parentByChild;
        }

        private static int[] ResolveChildIndices(VoxNode sourceNode, IReadOnlyDictionary<int, int> nodeIndexBySourceId)
        {
            switch (sourceNode)
            {
                case VoxTransformNode transformNode when nodeIndexBySourceId.TryGetValue(transformNode.ChildId, out int childIndex):
                    return new[] { childIndex };
                case VoxGroupNode groupNode:
                    List<int> mappedChildren = new List<int>(groupNode.ChildIds.Count);
                    for (int i = 0; i < groupNode.ChildIds.Count; i++)
                    {
                        if (nodeIndexBySourceId.TryGetValue(groupNode.ChildIds[i], out int mappedChild))
                        {
                            mappedChildren.Add(mappedChild);
                        }
                    }

                    return mappedChildren.Count == 0 ? Array.Empty<int>() : mappedChildren.ToArray();
                default:
                    return Array.Empty<int>();
            }
        }

        private static byte[] BuildPaletteBytes(IReadOnlyList<Color32> paletteColors)
        {
            using VoxelPalette palette = new VoxelPalette(Allocator.Temp);
            for (int i = 0; i < VoxelPalette.ColorCount; i++)
            {
                Color32 color = i < paletteColors.Count ? paletteColors[i] : default;
                palette[i] = new VoxelColor(color.r, color.g, color.b, color.a);
            }

            return VoxelPaletteSerializer.SerializeToBytes(palette);
        }

        private static byte[] BuildModelBytes(VoxScene scene, VoxModelDefinition model, VoxImportOptions options)
        {
            int initialChunkCapacity = ComputeInitialChunkCapacity(model.Size);
            using VoxelModel result = VoxelModel.Create(initialChunkCapacity, initialChunkCapacity, Allocator.Temp);
            FillModel(result, scene, model, options);
            return VoxelModelSerializer.SerializeToBytes(result);
        }

        private static void FillModel(VoxelModel model, VoxScene scene, VoxModelDefinition modelDefinition, VoxImportOptions options)
        {
            var opaqueChunks = new Dictionary<int3, ChunkBuildState>();
            var transparentChunks = new Dictionary<int3, ChunkBuildState>();
            IReadOnlyList<Color32> palette = scene.Palette;

            for (int i = 0; i < modelDefinition.Voxels.Count; i++)
            {
                VoxVoxel voxel = modelDefinition.Voxels[i];
                Color32 color = palette[voxel.ColorIndex];
                bool isTransparent = color.a < byte.MaxValue;

                VoxelVolume targetVolume = isTransparent ? model.TransparentVolume : model.OpaqueVolume;
                Dictionary<int3, ChunkBuildState> targetChunks = isTransparent ? transparentChunks : opaqueChunks;

                Vector3Int position = voxel.Position;
                int3 chunkCoordinate = new int3(
                    position.x / VoxelVolume.ChunkDimension,
                    position.y / VoxelVolume.ChunkDimension,
                    position.z / VoxelVolume.ChunkDimension);
                int3 localVoxelCoordinate = new int3(
                    position.x % VoxelVolume.ChunkDimension,
                    position.y % VoxelVolume.ChunkDimension,
                    position.z % VoxelVolume.ChunkDimension);

                if (!targetChunks.TryGetValue(chunkCoordinate, out ChunkBuildState chunkState))
                {
                    if (!targetVolume.TryAllocateChunk(chunkCoordinate, out int chunkIndex))
                    {
                        throw new InvalidOperationException($"Chunk {chunkCoordinate} was allocated more than once.");
                    }

                    chunkState = new ChunkBuildState(chunkIndex);
                    targetChunks.Add(chunkCoordinate, chunkState);
                }

                targetVolume.SetVoxel(
                    chunkState.ChunkIndex,
                    localVoxelCoordinate.x,
                    localVoxelCoordinate.y,
                    localVoxelCoordinate.z,
                    voxel.ColorIndex);
                chunkState.IncludeVoxel(localVoxelCoordinate);
            }

            int opaqueFallbackChunkCount = AllocateChunkAabbs(model.OpaqueVolume, opaqueChunks, options.MaxAabbsPerChunk);
            int transparentFallbackChunkCount = AllocateChunkAabbs(model.TransparentVolume, transparentChunks, options.MaxAabbsPerChunk);

            if (options.MaxAabbsPerChunk > 1 && opaqueFallbackChunkCount > 0)
            {
                scene.AddWarning(
                    $"Model {modelDefinition.Id} exceeded the per-chunk AABB budget ({options.MaxAabbsPerChunk}) " +
                    $"for {opaqueFallbackChunkCount} opaque chunk(s); the remaining voxels in those chunks were collapsed into one fallback AABB.");
            }

            if (options.MaxAabbsPerChunk > 1 && transparentFallbackChunkCount > 0)
            {
                scene.AddWarning(
                    $"Model {modelDefinition.Id} exceeded the per-chunk AABB budget ({options.MaxAabbsPerChunk}) " +
                    $"for {transparentFallbackChunkCount} transparent chunk(s); the remaining voxels in those chunks were collapsed into one fallback AABB.");
            }
        }

        private static int AllocateChunkAabbs(
            VoxelVolume volume,
            IReadOnlyDictionary<int3, ChunkBuildState> chunks,
            int maxAabbsPerChunk)
        {
            int fallbackChunkCount = 0;
            List<VoxelChunkAabb> allocatedAabbs = new List<VoxelChunkAabb>(VoxelVolume.MaxAabbsPerChunk);

            foreach (ChunkBuildState chunk in chunks.Values)
            {
                if (!chunk.HasVoxels)
                {
                    continue;
                }

                VoxelChunkAabbOptimizer.BuildChunkAabbs(
                    chunk,
                    maxAabbsPerChunk,
                    allocatedAabbs,
                    out bool usedFallbackBounds);
                if (allocatedAabbs.Count == 0)
                {
                    throw new InvalidOperationException($"Chunk {chunk.ChunkIndex} produced no AABBs even though it contains voxels.");
                }

                for (int i = 0; i < allocatedAabbs.Count; i++)
                {
                    if (!volume.TryAllocateAabbSlot(chunk.ChunkIndex, out int aabbIndex))
                    {
                        throw new InvalidOperationException($"Chunk {chunk.ChunkIndex} has no free AABB slots.");
                    }

                    VoxelChunkAabb aabb = allocatedAabbs[i];
                    volume.SetAabb(chunk.ChunkIndex, aabbIndex, aabb.Min, aabb.Max);
                }

                if (usedFallbackBounds)
                {
                    fallbackChunkCount++;
                }
            }

            return fallbackChunkCount;
        }

        private static int ComputeInitialChunkCapacity(Vector3Int modelSize)
        {
            int chunkResolutionX = Mathf.Max(1, Mathf.CeilToInt((float)modelSize.x / VoxelVolume.ChunkDimension));
            int chunkResolutionY = Mathf.Max(1, Mathf.CeilToInt((float)modelSize.y / VoxelVolume.ChunkDimension));
            int chunkResolutionZ = Mathf.Max(1, Mathf.CeilToInt((float)modelSize.z / VoxelVolume.ChunkDimension));
            return Math.Max(1, checked(chunkResolutionX * chunkResolutionY * chunkResolutionZ));
        }

        private static bool HasRenderableGeometry(VoxModelDefinition model)
        {
            return model != null && model.Voxels != null && model.Voxels.Count > 0;
        }

        private static Vector3 ComputeScenePivotOffset(Vector3Int modelSize)
        {
            return new Vector3(modelSize.x / 2, modelSize.y / 2, modelSize.z / 2);
        }

        private static VoxelModelAsset WriteModelAsset(string assetPath, byte[] serializedBytes)
        {
            VoxelModelAsset asset = AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<VoxelModelAsset>();
                asset.SetSerializedData(serializedBytes);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.SetSerializedData(serializedBytes);
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static VoxelPaletteAsset WritePaletteAsset(string assetPath, byte[] serializedBytes)
        {
            VoxelPaletteAsset asset = AssetDatabase.LoadAssetAtPath<VoxelPaletteAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<VoxelPaletteAsset>();
                asset.SetSerializedData(serializedBytes);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.SetSerializedData(serializedBytes);
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static void EnsureAddressableAsset(string assetPath, string address)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                throw new InvalidOperationException("Addressables settings were not found. Create Addressables settings before importing VOX assets.");
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                AddressableAssetGroup group = settings.DefaultGroup;
                if (group == null)
                {
                    throw new InvalidOperationException("Addressables default group was not found.");
                }

                entry = settings.CreateOrMoveEntry(guid, group, false, false);
            }

            entry.address = string.IsNullOrWhiteSpace(address) ? guid : address;
            EditorUtility.SetDirty(settings);
        }

        private static void CleanupStaleModelAssets(string importFolderPath, string importName, IReadOnlyCollection<string> keepAssetPaths)
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(VoxelModelAsset)}", new[] { importFolderPath });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileName(assetPath);
                if (!fileName.StartsWith($"{importName}_model_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (keepAssetPaths.Contains(assetPath))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void EmitWarnings(VoxScene scene)
        {
            if (scene.Warnings.Count == 0)
            {
                return;
            }

            Debug.LogWarning(
                $"VOX import '{scene.Name}' completed with {scene.Warnings.Count} warning(s):\n- "
                + string.Join("\n- ", scene.Warnings));
        }

        private static string EnsureImportFolder(string targetFolderPath, string importName)
        {
            string importFolderPath = $"{targetFolderPath}/{importName}";
            if (AssetDatabase.IsValidFolder(importFolderPath))
            {
                return importFolderPath;
            }

            string[] segments = importFolderPath.Split('/');
            string currentPath = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string nextPath = $"{currentPath}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[i]);
                }

                currentPath = nextPath;
            }

            return importFolderPath;
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "ImportedVox";
            }

            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(fileName.Length);
            for (int i = 0; i < fileName.Length; i++)
            {
                char character = fileName[i];
                builder.Append(invalidCharacters.Contains(character) || character == '/' || character == '\\' ? '_' : character);
            }

            string sanitized = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "ImportedVox" : sanitized;
        }
    }
}
