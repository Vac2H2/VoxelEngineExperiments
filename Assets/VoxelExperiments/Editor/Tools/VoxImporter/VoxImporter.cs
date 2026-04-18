using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelExperiments.Runtime.Data;
using VoxelExperiments.Runtime.Rendering.ModelProceduralAabb;
using VoxelExperiments.Runtime.Rendering.VoxelRenderer;

namespace VoxelExperiments.Editor.Tools.VoxImporter
{
    internal static class VoxImporter
    {
        private const string VoxExtension = ".vox";

        public static GameObject Import(string sourceFilePath, string targetFolderPath, Material material)
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

            if (material == null)
            {
                throw new ArgumentNullException(nameof(material), "A material is required for imported VoxelRenderer components.");
            }

            Stopwatch totalStopwatch = Stopwatch.StartNew();
            VoxScene scene = ParseScene(sourceFilePath);
            TimeSpan parseDuration = totalStopwatch.Elapsed;
            EmitWarnings(scene);

            string importName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourceFilePath));
            string importFolderPath = EnsureImportFolder(targetFolderPath, importName);
            string palettePath = $"{importFolderPath}/{importName}_palette.asset";

            HashSet<string> keepAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                palettePath,
                $"{importFolderPath}/{importName}.prefab",
            };

            Stopwatch assetStopwatch = Stopwatch.StartNew();
            VoxelPalette palette;
            Dictionary<int, ImportedModelAssets> modelAssetsByModelId;

            AssetDatabase.StartAssetEditing();
            try
            {
                palette = CreateOrUpdatePaletteAsset(palettePath, scene.Palette);
                modelAssetsByModelId = CreateOrUpdateModelAssets(
                    scene,
                    importFolderPath,
                    importName,
                    keepAssetPaths);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            TimeSpan assetDuration = assetStopwatch.Elapsed;

            Stopwatch cleanupStopwatch = Stopwatch.StartNew();
            CleanupStaleModelAssets(importFolderPath, importName, keepAssetPaths);
            TimeSpan cleanupDuration = cleanupStopwatch.Elapsed;

            Stopwatch prefabStopwatch = Stopwatch.StartNew();
            GameObject prefab = CreateOrUpdatePrefab(
                importFolderPath,
                importName,
                scene,
                palette,
                modelAssetsByModelId,
                material);
            TimeSpan prefabDuration = prefabStopwatch.Elapsed;

            Stopwatch saveStopwatch = Stopwatch.StartNew();
            AssetDatabase.SaveAssets();
            TimeSpan saveDuration = saveStopwatch.Elapsed;

            Debug.Log(
                $"VOX import '{scene.Name}' finished in {totalStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(parse {parseDuration.TotalSeconds:F2}s, assets {assetDuration.TotalSeconds:F2}s, "
                + $"cleanup {cleanupDuration.TotalSeconds:F2}s, prefab {prefabDuration.TotalSeconds:F2}s, "
                + $"save {saveDuration.TotalSeconds:F2}s) | models={scene.Models.Count}, roots={scene.RootNodeIds.Count}.");

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            return prefab;
        }

        private static VoxScene ParseScene(string sourceFilePath)
        {
            using FileStream stream = File.OpenRead(sourceFilePath);
            using BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, false);

            string fileId = ReadChunkId(reader);
            if (!string.Equals(fileId, "VOX ", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The selected file is not a valid VOX file.");
            }

            int version = reader.ReadInt32();
            if (version < 150)
            {
                throw new InvalidDataException($"Unsupported VOX version {version}. Expected version 150 or newer.");
            }

            VoxScene scene = new VoxScene(Path.GetFileNameWithoutExtension(sourceFilePath));
            ReadChunks(reader, reader.BaseStream.Length, scene);

            if (scene.Palette == null)
            {
                throw new InvalidDataException("The VOX file does not contain an RGBA palette chunk.");
            }

            scene.ResolveRoots();
            return scene;
        }

        private static void ReadChunks(BinaryReader reader, long endPosition, VoxScene scene)
        {
            while (reader.BaseStream.Position < endPosition)
            {
                string chunkId = ReadChunkId(reader);
                int contentByteCount = reader.ReadInt32();
                int childByteCount = reader.ReadInt32();
                long contentStart = reader.BaseStream.Position;
                long contentEnd = contentStart + contentByteCount;

                switch (chunkId)
                {
                    case "MAIN":
                        break;
                    case "PACK":
                        ReadPackChunk(reader, scene);
                        break;
                    case "SIZE":
                        ReadSizeChunk(reader, scene);
                        break;
                    case "XYZI":
                        ReadXyziChunk(reader, scene);
                        break;
                    case "RGBA":
                        ReadRgbaChunk(reader, scene);
                        break;
                    case "LAYR":
                        ReadLayerChunk(reader, scene);
                        break;
                    case "nTRN":
                        ReadTransformChunk(reader, scene);
                        break;
                    case "nGRP":
                        ReadGroupChunk(reader, scene);
                        break;
                    case "nSHP":
                        ReadShapeChunk(reader, scene);
                        break;
                    default:
                        scene.AddWarning($"Ignored unsupported VOX chunk '{chunkId}'.");
                        reader.BaseStream.Position = contentEnd;
                        break;
                }

                reader.BaseStream.Position = contentEnd;

                if (childByteCount <= 0)
                {
                    continue;
                }

                long childrenEnd = reader.BaseStream.Position + childByteCount;
                ReadChunks(reader, childrenEnd, scene);
                reader.BaseStream.Position = childrenEnd;
            }
        }

        private static void ReadPackChunk(BinaryReader reader, VoxScene scene)
        {
            scene.ExpectedModelCount = reader.ReadInt32();
        }

        private static void ReadSizeChunk(BinaryReader reader, VoxScene scene)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int z = reader.ReadInt32();
            scene.PendingModelSizes.Add(new Vector3Int(x, z, y));
        }

        private static void ReadXyziChunk(BinaryReader reader, VoxScene scene)
        {
            int modelId = scene.Models.Count;
            if (modelId >= scene.PendingModelSizes.Count)
            {
                throw new InvalidDataException("Encountered XYZI data before a matching SIZE chunk.");
            }

            Vector3Int size = scene.PendingModelSizes[modelId];
            int voxelCount = reader.ReadInt32();
            List<VoxVoxel> voxels = new List<VoxVoxel>(voxelCount);
            for (int i = 0; i < voxelCount; i++)
            {
                int x = reader.ReadByte();
                int y = reader.ReadByte();
                int z = reader.ReadByte();
                byte colorIndex = reader.ReadByte();
                voxels.Add(new VoxVoxel(new Vector3Int(x, z, y), colorIndex));
            }

            scene.Models.Add(new VoxModelDefinition(modelId, size, voxels));
        }

        private static void ReadRgbaChunk(BinaryReader reader, VoxScene scene)
        {
            Color32[] palette = new Color32[VoxelPalette.EntryCount];
            palette[0] = new Color32(0, 0, 0, 0);

            for (int i = 0; i < VoxelPalette.EntryCount; i++)
            {
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                byte a = reader.ReadByte();
                if (i < VoxelPalette.EntryCount - 1)
                {
                    palette[i + 1] = new Color32(r, g, b, a);
                }
            }

            scene.Palette = palette;
        }

        private static void ReadLayerChunk(BinaryReader reader, VoxScene scene)
        {
            int layerId = reader.ReadInt32();
            Dictionary<string, string> attributes = ReadDict(reader);
            int reservedId = reader.ReadInt32();
            if (reservedId != -1)
            {
                scene.AddWarning($"Layer {layerId} reserved id was {reservedId}, expected -1.");
            }

            scene.Layers[layerId] = new VoxLayer(
                ResolveNodeName(attributes, $"Layer_{layerId}"),
                IsHidden(attributes));
        }

        private static void ReadTransformChunk(BinaryReader reader, VoxScene scene)
        {
            int nodeId = reader.ReadInt32();
            Dictionary<string, string> attributes = ReadDict(reader);
            int childId = reader.ReadInt32();
            int reservedId = reader.ReadInt32();
            if (reservedId != -1)
            {
                scene.AddWarning($"Transform node {nodeId} reserved id was {reservedId}, expected -1.");
            }

            int layerId = reader.ReadInt32();
            int frameCount = reader.ReadInt32();
            if (frameCount <= 0)
            {
                throw new InvalidDataException($"Transform node {nodeId} has no frames.");
            }

            Vector3 translation = Vector3.zero;
            Quaternion rotation = Quaternion.identity;

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                Dictionary<string, string> frameAttributes = ReadDict(reader);
                if (frameIndex == 0)
                {
                    ParseFrameTransform(frameAttributes, scene, nodeId, out translation, out rotation);
                }
            }

            if (frameCount > 1)
            {
                scene.AddWarning($"Transform node {nodeId} contains {frameCount} frames. Only the first frame was imported.");
            }

            scene.Nodes[nodeId] = new VoxTransformNode(
                nodeId,
                ResolveNodeName(attributes, $"Transform_{nodeId}"),
                IsHidden(attributes),
                childId,
                layerId,
                translation,
                rotation);
        }

        private static void ReadGroupChunk(BinaryReader reader, VoxScene scene)
        {
            int nodeId = reader.ReadInt32();
            Dictionary<string, string> attributes = ReadDict(reader);
            int childCount = reader.ReadInt32();
            int[] childIds = new int[childCount];
            for (int i = 0; i < childCount; i++)
            {
                childIds[i] = reader.ReadInt32();
            }

            scene.Nodes[nodeId] = new VoxGroupNode(
                nodeId,
                ResolveNodeName(attributes, $"Group_{nodeId}"),
                IsHidden(attributes),
                childIds);
        }

        private static void ReadShapeChunk(BinaryReader reader, VoxScene scene)
        {
            int nodeId = reader.ReadInt32();
            Dictionary<string, string> attributes = ReadDict(reader);
            int modelCount = reader.ReadInt32();
            int primaryModelId = -1;

            for (int i = 0; i < modelCount; i++)
            {
                int modelId = reader.ReadInt32();
                ReadDict(reader);
                if (i == 0)
                {
                    primaryModelId = modelId;
                }
            }

            if (modelCount > 1)
            {
                scene.AddWarning($"Shape node {nodeId} references {modelCount} models. Only the first model was imported.");
            }

            if (primaryModelId < 0)
            {
                throw new InvalidDataException($"Shape node {nodeId} does not reference a valid model.");
            }

            scene.Nodes[nodeId] = new VoxShapeNode(
                nodeId,
                ResolveNodeName(attributes, $"Model_{primaryModelId}"),
                IsHidden(attributes),
                primaryModelId);
        }

        private static Dictionary<string, string> ReadDict(BinaryReader reader)
        {
            int pairCount = reader.ReadInt32();
            Dictionary<string, string> values = new Dictionary<string, string>(pairCount, StringComparer.Ordinal);
            for (int i = 0; i < pairCount; i++)
            {
                string key = ReadString(reader);
                string value = ReadString(reader);
                values[key] = value;
            }

            return values;
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string ReadChunkId(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
            {
                throw new EndOfStreamException("Unexpected end of VOX file while reading a chunk id.");
            }

            return Encoding.ASCII.GetString(bytes);
        }

        private static void ParseFrameTransform(
            IReadOnlyDictionary<string, string> frameAttributes,
            VoxScene scene,
            int nodeId,
            out Vector3 translation,
            out Quaternion rotation)
        {
            Matrix4x4 sourceTransform = Matrix4x4.identity;

            if (frameAttributes.TryGetValue("_t", out string translationText)
                && !string.IsNullOrWhiteSpace(translationText))
            {
                string[] parts = translationText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3)
                {
                    scene.AddWarning($"Transform node {nodeId} has an invalid translation '{translationText}'.");
                }
                else
                {
                    int x = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    int y = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    int z = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    sourceTransform.SetColumn(3, new Vector4(x, y, z, 1f));
                }
            }

            if (frameAttributes.TryGetValue("_r", out string rotationText)
                && !string.IsNullOrWhiteSpace(rotationText))
            {
                if (!byte.TryParse(rotationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte encodedRotation))
                {
                    scene.AddWarning($"Transform node {nodeId} has an invalid rotation '{rotationText}'.");
                }
                else
                {
                    Int3x3 magicaRotation = DecodeMagicaRotation(encodedRotation);
                    if (magicaRotation.Determinant != 1)
                    {
                        scene.AddWarning($"Transform node {nodeId} rotation '{rotationText}' is not a pure rotation. It was ignored.");
                    }
                    else
                    {
                        sourceTransform = BuildTransformMatrix(magicaRotation, sourceTransform.GetColumn(3));
                    }
                }
            }

            Matrix4x4 unityTransform = SwizzleMagicaToUnityTransform(sourceTransform);
            DecomposeTransform(unityTransform, out translation, out rotation, out _);
        }

        private static Int3x3 DecodeMagicaRotation(byte encodedRotation)
        {
            int row0Column = encodedRotation & 0x3;
            int row1Column = (encodedRotation >> 2) & 0x3;
            if (row0Column > 2 || row1Column > 2 || row0Column == row1Column)
            {
                return Int3x3.Identity;
            }

            int row2Column = 3 - row0Column - row1Column;
            Vector3Int row0 = CreateRotationRow(row0Column, ((encodedRotation >> 4) & 0x1) == 0);
            Vector3Int row1 = CreateRotationRow(row1Column, ((encodedRotation >> 5) & 0x1) == 0);
            Vector3Int row2 = CreateRotationRow(row2Column, ((encodedRotation >> 6) & 0x1) == 0);
            return new Int3x3(row0, row1, row2);
        }

        private static Vector3Int CreateRotationRow(int axisIndex, bool positive)
        {
            int value = positive ? 1 : -1;
            switch (axisIndex)
            {
                case 0:
                    return new Vector3Int(value, 0, 0);
                case 1:
                    return new Vector3Int(0, value, 0);
                case 2:
                    return new Vector3Int(0, 0, value);
                default:
                    return Vector3Int.zero;
            }
        }

        private static Matrix4x4 BuildTransformMatrix(Int3x3 rotation, Vector4 translationColumn)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m00 = rotation.Row0.x;
            matrix.m01 = rotation.Row0.y;
            matrix.m02 = rotation.Row0.z;
            matrix.m10 = rotation.Row1.x;
            matrix.m11 = rotation.Row1.y;
            matrix.m12 = rotation.Row1.z;
            matrix.m20 = rotation.Row2.x;
            matrix.m21 = rotation.Row2.y;
            matrix.m22 = rotation.Row2.z;
            matrix.SetColumn(3, translationColumn);
            return matrix;
        }

        private static Matrix4x4 SwizzleMagicaToUnityTransform(Matrix4x4 sourceTransform)
        {
            Matrix4x4 basis = Matrix4x4.identity;
            basis.SetColumn(0, new Vector4(1f, 0f, 0f, 0f));
            basis.SetColumn(1, new Vector4(0f, 0f, 1f, 0f));
            basis.SetColumn(2, new Vector4(0f, 1f, 0f, 0f));
            return basis * sourceTransform * basis;
        }

        private static void DecomposeTransform(
            Matrix4x4 matrix,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = matrix.GetColumn(3);

            Vector3 basisX = matrix.GetColumn(0);
            Vector3 basisY = matrix.GetColumn(1);
            Vector3 basisZ = matrix.GetColumn(2);

            float scaleX = basisX.magnitude;
            float scaleY = basisY.magnitude;
            float scaleZ = basisZ.magnitude;

            basisX = scaleX < 1e-5f ? Vector3.right : basisX / scaleX;
            basisY = scaleY < 1e-5f ? Vector3.up : basisY / scaleY;
            basisZ = scaleZ < 1e-5f ? Vector3.forward : basisZ / scaleZ;

            if (Vector3.Dot(Vector3.Cross(basisX, basisY), basisZ) < 0f)
            {
                scaleX = -scaleX;
                basisX = -basisX;
            }

            Matrix4x4 rotationMatrix = Matrix4x4.identity;
            rotationMatrix.SetColumn(0, new Vector4(basisX.x, basisX.y, basisX.z, 0f));
            rotationMatrix.SetColumn(1, new Vector4(basisY.x, basisY.y, basisY.z, 0f));
            rotationMatrix.SetColumn(2, new Vector4(basisZ.x, basisZ.y, basisZ.z, 0f));

            rotation = rotationMatrix.rotation;
            scale = new Vector3(
                Mathf.Abs(scaleX) < 1e-5f ? 1f : scaleX,
                Mathf.Abs(scaleY) < 1e-5f ? 1f : scaleY,
                Mathf.Abs(scaleZ) < 1e-5f ? 1f : scaleZ);
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

            AssetDatabase.CreateFolder(targetFolderPath, importName);
            return importFolderPath;
        }

        private static VoxelPalette CreateOrUpdatePaletteAsset(string assetPath, IReadOnlyList<Color32> paletteColors)
        {
            VoxelPalette palette = AssetDatabase.LoadAssetAtPath<VoxelPalette>(assetPath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<VoxelPalette>();
                AssetDatabase.CreateAsset(palette, assetPath);
            }

            SerializedObject serializedPalette = new SerializedObject(palette);
            SerializedProperty entriesProperty = serializedPalette.FindProperty("_entries");
            entriesProperty.arraySize = VoxelPalette.EntryCount;

            for (int i = 0; i < VoxelPalette.EntryCount; i++)
            {
                SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                entryProperty.FindPropertyRelative("Color").colorValue = paletteColors[i];
                entryProperty.FindPropertyRelative("SurfaceType").intValue = 0;
            }

            serializedPalette.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(palette);
            return palette;
        }

        private static Dictionary<int, ImportedModelAssets> CreateOrUpdateModelAssets(
            VoxScene scene,
            string importFolderPath,
            string importName,
            ISet<string> keepAssetPaths)
        {
            Dictionary<int, ImportedModelAssets> modelAssetsByModelId = new Dictionary<int, ImportedModelAssets>(scene.Models.Count);
            Dictionary<string, VoxelModel> deduplicatedModels = new Dictionary<string, VoxelModel>(StringComparer.Ordinal);

            foreach (VoxModelDefinition model in scene.Models)
            {
                SplitModelData splitData = SplitModel(model, scene.Palette);
                VoxelModel opaqueModel = CreateOrReuseModelAsset(
                    splitData.Opaque,
                    importFolderPath,
                    $"{importName}_model_{model.Id:D3}_opaque.asset",
                    deduplicatedModels,
                    keepAssetPaths);
                VoxelModel transparentModel = CreateOrReuseModelAsset(
                    splitData.Transparent,
                    importFolderPath,
                    $"{importName}_model_{model.Id:D3}_transparent.asset",
                    deduplicatedModels,
                    keepAssetPaths);

                modelAssetsByModelId[model.Id] = new ImportedModelAssets(
                    opaqueModel,
                    transparentModel,
                    ComputeScenePivotOffset(model.Size));
            }

            return modelAssetsByModelId;
        }

        private static Vector3 ComputeScenePivotOffset(Vector3Int modelSize)
        {
            return new Vector3(
                modelSize.x / 2,
                modelSize.y / 2,
                modelSize.z / 2);
        }

        private static SplitModelData SplitModel(VoxModelDefinition model, IReadOnlyList<Color32> palette)
        {
            Dictionary<Vector3Int, ChunkBuilder> opaqueChunkBuilders = null;
            Dictionary<Vector3Int, ChunkBuilder> transparentChunkBuilders = null;

            for (int i = 0; i < model.Voxels.Count; i++)
            {
                VoxVoxel voxel = model.Voxels[i];
                if (palette[voxel.ColorIndex].a < byte.MaxValue)
                {
                    transparentChunkBuilders ??= new Dictionary<Vector3Int, ChunkBuilder>();
                    AddVoxelToChunks(transparentChunkBuilders, voxel);
                }
                else
                {
                    opaqueChunkBuilders ??= new Dictionary<Vector3Int, ChunkBuilder>();
                    AddVoxelToChunks(opaqueChunkBuilders, voxel);
                }
            }

            return new SplitModelData(
                PackModelData(opaqueChunkBuilders),
                PackModelData(transparentChunkBuilders));
        }

        private static void AddVoxelToChunks(
            IDictionary<Vector3Int, ChunkBuilder> chunkBuilders,
            VoxVoxel voxel)
        {
            Vector3Int position = voxel.Position;
            Vector3Int chunkCoord = new Vector3Int(
                position.x / VoxelChunkLayout.Dimension,
                position.y / VoxelChunkLayout.Dimension,
                position.z / VoxelChunkLayout.Dimension);

            if (!chunkBuilders.TryGetValue(chunkCoord, out ChunkBuilder chunk))
            {
                chunk = new ChunkBuilder(chunkCoord);
                chunkBuilders.Add(chunkCoord, chunk);
            }

            chunk.SetVoxel(
                position.x % VoxelChunkLayout.Dimension,
                position.y % VoxelChunkLayout.Dimension,
                position.z % VoxelChunkLayout.Dimension,
                voxel.ColorIndex);
        }

        private static PackedVoxelModelData PackModelData(IReadOnlyDictionary<Vector3Int, ChunkBuilder> chunkBuilders)
        {
            if (chunkBuilders == null || chunkBuilders.Count == 0)
            {
                return null;
            }

            List<ChunkBuilder> orderedChunks = chunkBuilders.Values.ToList();
            orderedChunks.Sort(static (a, b) =>
            {
                int xCompare = a.ChunkCoord.x.CompareTo(b.ChunkCoord.x);
                if (xCompare != 0)
                {
                    return xCompare;
                }

                int yCompare = a.ChunkCoord.y.CompareTo(b.ChunkCoord.y);
                if (yCompare != 0)
                {
                    return yCompare;
                }

                return a.ChunkCoord.z.CompareTo(b.ChunkCoord.z);
            });

            byte[] occupancyBytes = new byte[orderedChunks.Count * VoxelChunkLayout.OccupancyByteCount];
            byte[] voxelBytes = new byte[orderedChunks.Count * VoxelChunkLayout.VoxelDataByteCount];
            ModelChunkAabb[] chunkAabbs = new ModelChunkAabb[orderedChunks.Count];

            for (int i = 0; i < orderedChunks.Count; i++)
            {
                ChunkBuilder chunk = orderedChunks[i];
                chunk.CopyTo(
                    occupancyBytes,
                    i * VoxelChunkLayout.OccupancyByteCount,
                    voxelBytes,
                    i * VoxelChunkLayout.VoxelDataByteCount);
                chunkAabbs[i] = chunk.BuildAabb();
            }

            return new PackedVoxelModelData(
                occupancyBytes,
                voxelBytes,
                chunkAabbs,
                ComputeDataHash(occupancyBytes, voxelBytes, chunkAabbs));
        }

        private static string ComputeDataHash(
            IReadOnlyList<byte> occupancyBytes,
            IReadOnlyList<byte> voxelBytes,
            IReadOnlyList<ModelChunkAabb> chunkAabbs)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);

            writer.Write((byte)VoxelMemoryLayout.Linear);
            writer.Write(occupancyBytes.Count);
            for (int i = 0; i < occupancyBytes.Count; i++)
            {
                writer.Write(occupancyBytes[i]);
            }

            writer.Write(voxelBytes.Count);
            for (int i = 0; i < voxelBytes.Count; i++)
            {
                writer.Write(voxelBytes[i]);
            }

            writer.Write(chunkAabbs.Count);
            for (int i = 0; i < chunkAabbs.Count; i++)
            {
                ModelChunkAabb aabb = chunkAabbs[i];
                writer.Write(aabb.Min.x);
                writer.Write(aabb.Min.y);
                writer.Write(aabb.Min.z);
                writer.Write(aabb.Max.x);
                writer.Write(aabb.Max.y);
                writer.Write(aabb.Max.z);
            }

            writer.Flush();
            stream.Position = 0L;

            using SHA256 sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty, StringComparison.Ordinal);
        }

        private static VoxelModel CreateOrReuseModelAsset(
            PackedVoxelModelData data,
            string importFolderPath,
            string assetFileName,
            IDictionary<string, VoxelModel> deduplicatedModels,
            ISet<string> keepAssetPaths)
        {
            if (data == null)
            {
                return null;
            }

            if (deduplicatedModels.TryGetValue(data.Hash, out VoxelModel existingModel))
            {
                keepAssetPaths.Add(AssetDatabase.GetAssetPath(existingModel));
                return existingModel;
            }

            string assetPath = $"{importFolderPath}/{assetFileName}";
            VoxelModel model = AssetDatabase.LoadAssetAtPath<VoxelModel>(assetPath);
            if (model == null)
            {
                model = ScriptableObject.CreateInstance<VoxelModel>();
                AssetDatabase.CreateAsset(model, assetPath);
            }

            if (!model.HasContentHash(data.Hash))
            {
                model.OverwriteData(
                    VoxelMemoryLayout.Linear,
                    data.ChunkAabbs.Length,
                    data.OccupancyBytes,
                    data.VoxelBytes,
                    data.ChunkAabbs,
                    data.Hash);
                EditorUtility.SetDirty(model);
            }

            keepAssetPaths.Add(assetPath);
            deduplicatedModels[data.Hash] = model;
            return model;
        }

        private static GameObject CreateOrUpdatePrefab(
            string importFolderPath,
            string importName,
            VoxScene scene,
            VoxelPalette palette,
            IReadOnlyDictionary<int, ImportedModelAssets> modelAssetsByModelId,
            Material material)
        {
            string prefabPath = $"{importFolderPath}/{importName}.prefab";
            GameObject root = new GameObject(importName);

            try
            {
                if (scene.RootNodeIds.Count > 0)
                {
                    for (int i = 0; i < scene.RootNodeIds.Count; i++)
                    {
                        CreateNodeHierarchy(scene, scene.RootNodeIds[i], root.transform, palette, modelAssetsByModelId, material);
                    }
                }
                else
                {
                    CreateFallbackHierarchy(scene, root.transform, palette, modelAssetsByModelId, material);
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Failed to create prefab asset at '{prefabPath}'.");
            }

            return prefab;
        }

        private static void CreateFallbackHierarchy(
            VoxScene scene,
            Transform parent,
            VoxelPalette palette,
            IReadOnlyDictionary<int, ImportedModelAssets> modelAssetsByModelId,
            Material material)
        {
            foreach (VoxModelDefinition model in scene.Models)
            {
                if (!modelAssetsByModelId.TryGetValue(model.Id, out ImportedModelAssets modelAssets))
                {
                    continue;
                }

                GameObject modelRoot = new GameObject(SanitizeNodeName($"Model_{model.Id:D3}"));
                modelRoot.transform.SetParent(parent, false);
                CreateModelChildren(modelRoot.transform, modelAssets, palette, material);
            }
        }

        private static void CreateNodeHierarchy(
            VoxScene scene,
            int nodeId,
            Transform parent,
            VoxelPalette palette,
            IReadOnlyDictionary<int, ImportedModelAssets> modelAssetsByModelId,
            Material material)
        {
            if (!scene.Nodes.TryGetValue(nodeId, out VoxNode node))
            {
                scene.AddWarning($"Scene graph references missing node {nodeId}.");
                return;
            }

            switch (node)
            {
                case VoxTransformNode transformNode:
                    if (scene.Nodes.TryGetValue(transformNode.ChildId, out VoxNode childNode)
                        && childNode is VoxShapeNode shapeNode)
                    {
                        CreateShapeInstance(
                            scene,
                            transformNode,
                            shapeNode,
                            parent,
                            palette,
                            modelAssetsByModelId,
                            material);
                    }
                    else
                    {
                        GameObject transformObject = new GameObject(SanitizeNodeName(transformNode.Name));
                        transformObject.transform.SetParent(parent, false);
                        ApplyNodeTransform(transformObject.transform, transformNode);
                        transformObject.SetActive(!IsHidden(scene, transformNode));
                        CreateNodeHierarchy(
                            scene,
                            transformNode.ChildId,
                            transformObject.transform,
                            palette,
                            modelAssetsByModelId,
                            material);
                    }

                    break;
                case VoxGroupNode groupNode:
                    GameObject groupObject = new GameObject(SanitizeNodeName(groupNode.Name));
                    groupObject.transform.SetParent(parent, false);
                    groupObject.SetActive(!groupNode.Hidden);
                    for (int i = 0; i < groupNode.ChildIds.Count; i++)
                    {
                        CreateNodeHierarchy(
                            scene,
                            groupNode.ChildIds[i],
                            groupObject.transform,
                            palette,
                            modelAssetsByModelId,
                            material);
                    }

                    break;
                case VoxShapeNode shapeOnlyNode:
                    CreateShapeInstance(scene, null, shapeOnlyNode, parent, palette, modelAssetsByModelId, material);
                    break;
            }
        }

        private static void CreateShapeInstance(
            VoxScene scene,
            VoxTransformNode transformNode,
            VoxShapeNode shapeNode,
            Transform parent,
            VoxelPalette palette,
            IReadOnlyDictionary<int, ImportedModelAssets> modelAssetsByModelId,
            Material material)
        {
            if (!modelAssetsByModelId.TryGetValue(shapeNode.ModelId, out ImportedModelAssets modelAssets))
            {
                scene.AddWarning($"Shape node {shapeNode.NodeId} references missing model {shapeNode.ModelId}.");
                return;
            }

            string instanceName = transformNode != null
                ? SanitizeNodeName(transformNode.Name)
                : SanitizeNodeName(shapeNode.Name);
            GameObject instanceRoot = new GameObject(instanceName);
            instanceRoot.transform.SetParent(parent, false);

            if (transformNode != null)
            {
                ApplyShapeTransform(instanceRoot.transform, transformNode, modelAssets.ScenePivotOffset);
                instanceRoot.SetActive(!IsHidden(scene, transformNode) && !shapeNode.Hidden);
            }
            else
            {
                instanceRoot.SetActive(!shapeNode.Hidden);
            }

            CreateModelChildren(instanceRoot.transform, modelAssets, palette, material);
        }

        private static void ApplyNodeTransform(Transform transform, VoxTransformNode node)
        {
            transform.localPosition = node.Translation;
            transform.localRotation = node.Rotation;
            transform.localScale = Vector3.one;
        }

        private static void ApplyShapeTransform(
            Transform transform,
            VoxTransformNode node,
            Vector3 scenePivotOffset)
        {
            // MagicaVoxel scene instances rotate around floor(size / 2). Keep the imported
            // VoxelModel origin at its own lower-left corner by shifting the instance instead.
            transform.localPosition = node.Translation - (node.Rotation * scenePivotOffset);
            transform.localRotation = node.Rotation;
            transform.localScale = Vector3.one;
        }

        private static bool IsHidden(VoxScene scene, VoxTransformNode transformNode)
        {
            if (transformNode.Hidden)
            {
                return true;
            }

            return scene.Layers.TryGetValue(transformNode.LayerId, out VoxLayer layer) && layer.Hidden;
        }

        private static void CreateModelChildren(
            Transform parent,
            ImportedModelAssets modelAssets,
            VoxelPalette palette,
            Material material)
        {
            if (modelAssets.Opaque != null)
            {
                CreateRendererObject(parent, "Opaque", modelAssets.Opaque, palette, material, true);
            }

            if (modelAssets.Transparent != null)
            {
                CreateRendererObject(parent, "Transparent", modelAssets.Transparent, palette, material, false);
            }
        }

        private static void CreateRendererObject(
            Transform parent,
            string objectName,
            VoxelModel model,
            VoxelPalette palette,
            Material material,
            bool opaqueMaterial)
        {
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(parent, false);

            VoxelFilter filter = child.AddComponent<VoxelFilter>();
            SerializedObject serializedFilter = new SerializedObject(filter);
            serializedFilter.FindProperty("_model").objectReferenceValue = model;
            serializedFilter.FindProperty("_palette").objectReferenceValue = palette;
            serializedFilter.ApplyModifiedPropertiesWithoutUndo();

            VoxelRenderer renderer = child.AddComponent<VoxelRenderer>();
            SerializedObject serializedRenderer = new SerializedObject(renderer);
            serializedRenderer.FindProperty("_voxelFilter").objectReferenceValue = filter;
            serializedRenderer.FindProperty("_material").objectReferenceValue = material;
            serializedRenderer.FindProperty("_opaqueMaterial").boolValue = opaqueMaterial;
            serializedRenderer.FindProperty("_dynamicGeometry").boolValue = false;
            serializedRenderer.FindProperty("_overrideBuildFlags").boolValue = false;
            serializedRenderer.FindProperty("_buildFlags").intValue = (int)RayTracingAccelerationStructureBuildFlags.PreferFastTrace;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CleanupStaleModelAssets(
            string importFolderPath,
            string importName,
            IReadOnlyCollection<string> keepAssetPaths)
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(VoxelModel)}", new[] { importFolderPath });
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

        private static string ResolveNodeName(IReadOnlyDictionary<string, string> attributes, string fallbackName)
        {
            if (attributes.TryGetValue("_name", out string nodeName) && !string.IsNullOrWhiteSpace(nodeName))
            {
                return nodeName;
            }

            return fallbackName;
        }

        private static bool IsHidden(IReadOnlyDictionary<string, string> attributes)
        {
            return attributes.TryGetValue("_hidden", out string hiddenValue)
                && string.Equals(hiddenValue, "1", StringComparison.Ordinal);
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
                if (invalidCharacters.Contains(character) || character == '/' || character == '\\')
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(character);
                }
            }

            string sanitized = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized)
                ? "ImportedVox"
                : sanitized;
        }

        private static string SanitizeNodeName(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return "Node";
            }

            return nodeName
                .Replace('/', '_')
                .Replace('\\', '_')
                .Trim();
        }

        private sealed class VoxScene
        {
            public VoxScene(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public int? ExpectedModelCount { get; set; }

            public List<string> Warnings { get; } = new List<string>();

            public List<Vector3Int> PendingModelSizes { get; } = new List<Vector3Int>();

            public List<VoxModelDefinition> Models { get; } = new List<VoxModelDefinition>();

            public Dictionary<int, VoxNode> Nodes { get; } = new Dictionary<int, VoxNode>();

            public Dictionary<int, VoxLayer> Layers { get; } = new Dictionary<int, VoxLayer>();

            public List<int> RootNodeIds { get; } = new List<int>();

            public Color32[] Palette { get; set; }

            public void AddWarning(string warning)
            {
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    Warnings.Add(warning);
                }
            }

            public void ResolveRoots()
            {
                RootNodeIds.Clear();
                if (Nodes.Count == 0)
                {
                    return;
                }

                HashSet<int> referencedNodeIds = new HashSet<int>();
                foreach (VoxNode node in Nodes.Values)
                {
                    switch (node)
                    {
                        case VoxTransformNode transformNode:
                            referencedNodeIds.Add(transformNode.ChildId);
                            break;
                        case VoxGroupNode groupNode:
                            for (int i = 0; i < groupNode.ChildIds.Count; i++)
                            {
                                referencedNodeIds.Add(groupNode.ChildIds[i]);
                            }

                            break;
                    }
                }

                List<int> sortedNodeIds = Nodes.Keys.ToList();
                sortedNodeIds.Sort();
                for (int i = 0; i < sortedNodeIds.Count; i++)
                {
                    int nodeId = sortedNodeIds[i];
                    if (!referencedNodeIds.Contains(nodeId))
                    {
                        RootNodeIds.Add(nodeId);
                    }
                }

                if (RootNodeIds.Count == 0)
                {
                    RootNodeIds.AddRange(sortedNodeIds);
                }
            }
        }

        private abstract class VoxNode
        {
            protected VoxNode(int nodeId, string name, bool hidden)
            {
                NodeId = nodeId;
                Name = name;
                Hidden = hidden;
            }

            public int NodeId { get; }

            public string Name { get; }

            public bool Hidden { get; }
        }

        private sealed class VoxTransformNode : VoxNode
        {
            public VoxTransformNode(
                int nodeId,
                string name,
                bool hidden,
                int childId,
                int layerId,
                Vector3 translation,
                Quaternion rotation)
                : base(nodeId, name, hidden)
            {
                ChildId = childId;
                LayerId = layerId;
                Translation = translation;
                Rotation = rotation;
            }

            public int ChildId { get; }

            public int LayerId { get; }

            public Vector3 Translation { get; }

            public Quaternion Rotation { get; }
        }

        private sealed class VoxGroupNode : VoxNode
        {
            public VoxGroupNode(int nodeId, string name, bool hidden, IReadOnlyList<int> childIds)
                : base(nodeId, name, hidden)
            {
                ChildIds = childIds;
            }

            public IReadOnlyList<int> ChildIds { get; }
        }

        private sealed class VoxShapeNode : VoxNode
        {
            public VoxShapeNode(int nodeId, string name, bool hidden, int modelId)
                : base(nodeId, name, hidden)
            {
                ModelId = modelId;
            }

            public int ModelId { get; }
        }

        private readonly struct VoxLayer
        {
            public VoxLayer(string name, bool hidden)
            {
                Name = name;
                Hidden = hidden;
            }

            public string Name { get; }

            public bool Hidden { get; }
        }

        private sealed class VoxModelDefinition
        {
            public VoxModelDefinition(int id, Vector3Int size, IReadOnlyList<VoxVoxel> voxels)
            {
                Id = id;
                Size = size;
                Voxels = voxels;
            }

            public int Id { get; }

            public Vector3Int Size { get; }

            public IReadOnlyList<VoxVoxel> Voxels { get; }
        }

        private readonly struct VoxVoxel
        {
            public VoxVoxel(Vector3Int position, byte colorIndex)
            {
                Position = position;
                ColorIndex = colorIndex;
            }

            public Vector3Int Position { get; }

            public byte ColorIndex { get; }
        }

        private sealed class PackedVoxelModelData
        {
            public PackedVoxelModelData(
                byte[] occupancyBytes,
                byte[] voxelBytes,
                ModelChunkAabb[] chunkAabbs,
                string hash)
            {
                OccupancyBytes = occupancyBytes;
                VoxelBytes = voxelBytes;
                ChunkAabbs = chunkAabbs;
                Hash = hash;
            }

            public byte[] OccupancyBytes { get; }

            public byte[] VoxelBytes { get; }

            public ModelChunkAabb[] ChunkAabbs { get; }

            public string Hash { get; }
        }

        private sealed class SplitModelData
        {
            public SplitModelData(PackedVoxelModelData opaque, PackedVoxelModelData transparent)
            {
                Opaque = opaque;
                Transparent = transparent;
            }

            public PackedVoxelModelData Opaque { get; }

            public PackedVoxelModelData Transparent { get; }
        }

        private readonly struct ImportedModelAssets
        {
            public ImportedModelAssets(
                VoxelModel opaque,
                VoxelModel transparent,
                Vector3 scenePivotOffset)
            {
                Opaque = opaque;
                Transparent = transparent;
                ScenePivotOffset = scenePivotOffset;
            }

            public VoxelModel Opaque { get; }

            public VoxelModel Transparent { get; }

            public Vector3 ScenePivotOffset { get; }
        }

        private sealed class ChunkBuilder
        {
            private readonly byte[] _occupancyBytes = new byte[VoxelChunkLayout.OccupancyByteCount];
            private readonly byte[] _voxelBytes = new byte[VoxelChunkLayout.VoxelDataByteCount];

            public ChunkBuilder(Vector3Int chunkCoord)
            {
                ChunkCoord = chunkCoord;
            }

            public Vector3Int ChunkCoord { get; }

            public void SetVoxel(int x, int y, int z, byte voxelValue)
            {
                int voxelIndex = VoxelChunkLayout.FlattenVoxelDataIndex(VoxelMemoryLayout.Linear, x, y, z);
                int occupancyByteIndex = VoxelChunkLayout.ComputeOccupancyByteIndex(VoxelMemoryLayout.Linear, x, y, z);
                byte occupancyBit = VoxelChunkLayout.ComputeOccupancyBitMask(VoxelMemoryLayout.Linear, x, y, z);

                _occupancyBytes[occupancyByteIndex] |= occupancyBit;
                _voxelBytes[voxelIndex] = voxelValue;
            }

            public void CopyTo(byte[] occupancyDestination, int occupancyOffset, byte[] voxelDestination, int voxelOffset)
            {
                Buffer.BlockCopy(_occupancyBytes, 0, occupancyDestination, occupancyOffset, _occupancyBytes.Length);
                Buffer.BlockCopy(_voxelBytes, 0, voxelDestination, voxelOffset, _voxelBytes.Length);
            }

            public ModelChunkAabb BuildAabb()
            {
                Vector3 min = new Vector3(
                    ChunkCoord.x * VoxelChunkLayout.Dimension,
                    ChunkCoord.y * VoxelChunkLayout.Dimension,
                    ChunkCoord.z * VoxelChunkLayout.Dimension);
                Vector3 max = min + (Vector3.one * VoxelChunkLayout.Dimension);
                return new ModelChunkAabb(min, max);
            }
        }

        private readonly struct Int3x3
        {
            public static readonly Int3x3 Identity = new Int3x3(
                new Vector3Int(1, 0, 0),
                new Vector3Int(0, 1, 0),
                new Vector3Int(0, 0, 1));

            public Int3x3(Vector3Int row0, Vector3Int row1, Vector3Int row2)
            {
                Row0 = row0;
                Row1 = row1;
                Row2 = row2;
            }

            public Vector3Int Row0 { get; }

            public Vector3Int Row1 { get; }

            public Vector3Int Row2 { get; }

            public int Determinant =>
                (Row0.x * ((Row1.y * Row2.z) - (Row1.z * Row2.y)))
                - (Row0.y * ((Row1.x * Row2.z) - (Row1.z * Row2.x)))
                + (Row0.z * ((Row1.x * Row2.y) - (Row1.y * Row2.x)));

            public Vector3Int Multiply(Vector3Int vector)
            {
                return new Vector3Int(
                    Dot(Row0, vector),
                    Dot(Row1, vector),
                    Dot(Row2, vector));
            }

            private static int Dot(Vector3Int left, Vector3Int right)
            {
                return (left.x * right.x)
                    + (left.y * right.y)
                    + (left.z * right.z);
            }
        }
    }
}
