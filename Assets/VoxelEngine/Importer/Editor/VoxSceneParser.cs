using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Importer
{
    internal static class VoxSceneParser
    {
        public static VoxScene Parse(string sourceFilePath)
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
                        scene.ExpectedModelCount = reader.ReadInt32();
                        break;
                    case "SIZE":
                        scene.PendingModelSizes.Add(new Vector3Int(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
                        break;
                    case "XYZI":
                        ReadModelChunk(reader, scene);
                        break;
                    case "RGBA":
                        scene.Palette = ReadPalette(reader);
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

        private static void ReadModelChunk(BinaryReader reader, VoxScene scene)
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

            scene.Models.Add(new VoxModelDefinition(modelId, new Vector3Int(size.x, size.z, size.y), voxels));
        }

        private static Color32[] ReadPalette(BinaryReader reader)
        {
            Color32[] palette = new Color32[VoxelPalette.ColorCount];
            palette[0] = new Color32(0, 0, 0, 0);

            for (int i = 0; i < VoxelPalette.ColorCount; i++)
            {
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                byte a = reader.ReadByte();
                if (i < VoxelPalette.ColorCount - 1)
                {
                    palette[i + 1] = new Color32(r, g, b, a);
                }
            }

            return palette;
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

            if (frameAttributes.TryGetValue("_t", out string translationText) && !string.IsNullOrWhiteSpace(translationText))
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

            if (frameAttributes.TryGetValue("_r", out string rotationText) && !string.IsNullOrWhiteSpace(rotationText))
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

        internal static string ResolveNodeName(IReadOnlyDictionary<string, string> attributes, string fallbackName)
        {
            if (attributes.TryGetValue("_name", out string nodeName) && !string.IsNullOrWhiteSpace(nodeName))
            {
                return nodeName;
            }

            return fallbackName;
        }

        internal static bool IsHidden(IReadOnlyDictionary<string, string> attributes)
        {
            return attributes.TryGetValue("_hidden", out string hiddenValue)
                && string.Equals(hiddenValue, "1", StringComparison.Ordinal);
        }

        internal static bool IsHidden(VoxScene scene, VoxTransformNode transformNode)
        {
            if (transformNode.Hidden)
            {
                return true;
            }

            return scene.Layers.TryGetValue(transformNode.LayerId, out VoxLayer layer) && layer.Hidden;
        }
    }
}
