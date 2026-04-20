using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Tools
{
    public sealed class MeshToVoxelModelAssetWindow : EditorWindow
    {
        [SerializeField] private Mesh _sourceMesh;
        [SerializeField] private VoxelModelAsset _targetAsset;
        [SerializeField] private string _targetAssetPath;
        [SerializeField] private float _voxelSize = 0.1f;
        [SerializeField] private int _solidVoxelValue = 1;
        [SerializeField] private int _maxAabbsPerChunk = 1;

        [MenuItem("VoxelEngine/Tools/Mesh To VoxelModel Asset")]
        public static void Open()
        {
            MeshToVoxelModelAssetWindow window = GetWindow<MeshToVoxelModelAssetWindow>();
            window.titleContent = new GUIContent("Mesh To Voxel");
            window.minSize = new Vector2(440.0f, 320.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Mesh To VoxelModel Asset", EditorStyles.boldLabel);

            _sourceMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", _sourceMesh, typeof(Mesh), false);
            VoxelModelAsset previousTargetAsset = _targetAsset;
            _targetAsset = (VoxelModelAsset)EditorGUILayout.ObjectField("Target Asset", _targetAsset, typeof(VoxelModelAsset), false);
            if (_targetAsset != previousTargetAsset)
            {
                _targetAssetPath = _targetAsset != null
                    ? AssetDatabase.GetAssetPath(_targetAsset)
                    : null;
            }

            _voxelSize = EditorGUILayout.FloatField("Voxel Size", _voxelSize);
            _solidVoxelValue = EditorGUILayout.IntSlider("Solid Voxel Value", _solidVoxelValue, 1, 255);
            _maxAabbsPerChunk = EditorGUILayout.IntSlider("Max AABBs / Chunk", _maxAabbsPerChunk, 1, VoxelVolume.MaxAabbsPerChunk);

            EditorGUILayout.Space();

            if (_sourceMesh != null && _voxelSize > 0f)
            {
                DrawMeshSummary(_sourceMesh, _voxelSize);
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a source mesh and a positive voxel size.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The tool assumes the mesh is a closed solid. It voxelizes into the latest VoxelModelAsset format with linear chunk storage only.",
                MessageType.None);
            EditorGUILayout.HelpBox(
                "Chunk coordinates are rebased so the source mesh bounds minimum becomes local origin. Each occupied chunk builds up to the requested AABB budget.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(_sourceMesh == null || _voxelSize <= 0f))
            {
                if (GUILayout.Button(_targetAsset == null ? "Create Asset" : "Overwrite Asset", GUILayout.Height(32.0f)))
                {
                    Bake();
                }
            }
        }

        private static void DrawMeshSummary(Mesh mesh, float voxelSize)
        {
            Bounds bounds = mesh.bounds;
            Vector3Int gridDimensions = CalculateGridDimensions(bounds, voxelSize);
            Vector3Int chunkDimensions = CalculateChunkDimensions(gridDimensions);
            long voxelCount = (long)gridDimensions.x * gridDimensions.y * gridDimensions.z;
            long chunkCapacity = (long)chunkDimensions.x * chunkDimensions.y * chunkDimensions.z;

            EditorGUILayout.LabelField("Bounds Min", bounds.min.ToString("F4"));
            EditorGUILayout.LabelField("Bounds Max", bounds.max.ToString("F4"));
            EditorGUILayout.LabelField("Grid", $"{gridDimensions.x} x {gridDimensions.y} x {gridDimensions.z}");
            EditorGUILayout.LabelField("Chunk Grid", $"{chunkDimensions.x} x {chunkDimensions.y} x {chunkDimensions.z}");
            EditorGUILayout.LabelField("Estimated Voxels", voxelCount.ToString("N0"));
            EditorGUILayout.LabelField("Max Chunk Capacity", chunkCapacity.ToString("N0"));

            if (voxelCount > 2_000_000L)
            {
                EditorGUILayout.HelpBox("The estimated voxel count is large. Expect a slower bake and a larger asset.", MessageType.Warning);
            }
        }

        private void Bake()
        {
            string assetPath = ResolveTargetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            try
            {
                var options = new MeshToVoxelModelAssetBuildOptions(
                    _voxelSize,
                    checked((byte)_solidVoxelValue),
                    _maxAabbsPerChunk);
                MeshToVoxelModelAssetBuildResult buildResult = MeshToVoxelModelAssetBuilder.Build(_sourceMesh, options);

                _targetAsset = WriteAsset(assetPath, buildResult.SerializedBytes);
                _targetAssetPath = assetPath;

                Selection.activeObject = _targetAsset;
                EditorGUIUtility.PingObject(_targetAsset);

                if (buildResult.FallbackChunkCount > 0 && _maxAabbsPerChunk > 1)
                {
                    Debug.LogWarning(
                        $"Mesh To VoxelModel Asset: generated {buildResult.ChunkCount} chunk(s) and {buildResult.OccupiedVoxelCount:N0} occupied voxel(s). " +
                        $"{buildResult.FallbackChunkCount} chunk(s) exceeded the AABB budget {_maxAabbsPerChunk} and used a fallback bounds AABB for the remaining voxels.");
                }
                else
                {
                    Debug.Log(
                        $"Mesh To VoxelModel Asset: generated {buildResult.ChunkCount} chunk(s) and {buildResult.OccupiedVoxelCount:N0} occupied voxel(s).");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Mesh To VoxelModel Asset", exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private string ResolveTargetPath()
        {
            if (_targetAsset != null)
            {
                if (!string.IsNullOrEmpty(_targetAssetPath))
                {
                    VoxelModelAsset loadedAsset = AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(_targetAssetPath);
                    if (loadedAsset == _targetAsset)
                    {
                        return _targetAssetPath;
                    }
                }

                _targetAssetPath = AssetDatabase.GetAssetPath(_targetAsset);
                if (string.IsNullOrWhiteSpace(_targetAssetPath))
                {
                    EditorUtility.DisplayDialog(
                        "Mesh To VoxelModel Asset",
                        "Target asset must be a persistent VoxelModelAsset.",
                        "OK");
                    return null;
                }

                return _targetAssetPath;
            }

            string defaultName = _sourceMesh != null ? $"{_sourceMesh.name}.asset" : "VoxelModel.asset";
            string defaultDirectory = "Assets";

            if (_sourceMesh != null)
            {
                string meshPath = AssetDatabase.GetAssetPath(_sourceMesh);
                if (!string.IsNullOrEmpty(meshPath))
                {
                    string meshDirectory = System.IO.Path.GetDirectoryName(meshPath);
                    if (!string.IsNullOrEmpty(meshDirectory))
                    {
                        defaultDirectory = meshDirectory.Replace('\\', '/');
                    }
                }
            }

            return EditorUtility.SaveFilePanelInProject(
                "Create Mesh VoxelModel Asset",
                defaultName,
                "asset",
                "Choose where to save the generated VoxelModel asset.",
                defaultDirectory);
        }

        private static VoxelModelAsset WriteAsset(string assetPath, byte[] serializedBytes)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("Asset path must be a non-empty string.", nameof(assetPath));
            }

            if (serializedBytes == null)
            {
                throw new ArgumentNullException(nameof(serializedBytes));
            }

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

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(assetPath);
        }

        private static Vector3Int CalculateGridDimensions(Bounds bounds, float voxelSize)
        {
            Vector3 size = bounds.size;
            return new Vector3Int(
                Mathf.Max(1, Mathf.CeilToInt(size.x / voxelSize)),
                Mathf.Max(1, Mathf.CeilToInt(size.y / voxelSize)),
                Mathf.Max(1, Mathf.CeilToInt(size.z / voxelSize)));
        }

        private static Vector3Int CalculateChunkDimensions(Vector3Int gridDimensions)
        {
            return new Vector3Int(
                DivideRoundUp(gridDimensions.x, VoxelVolume.ChunkDimension),
                DivideRoundUp(gridDimensions.y, VoxelVolume.ChunkDimension),
                DivideRoundUp(gridDimensions.z, VoxelVolume.ChunkDimension));
        }

        private static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
