using System;
using UnityEditor;
using UnityEngine;
using VoxelRT.Runtime.Data;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    public sealed class MeshToVoxelModelWindow : EditorWindow
    {
        [SerializeField] private Mesh _sourceMesh;
        [SerializeField] private VoxelModel _targetModel;
        [SerializeField] private string _targetModelPath;
        [SerializeField] private float _voxelSize = 0.1f;
        [SerializeField] private int _solidVoxelValue = 1;
        [SerializeField] private VoxelMemoryLayout _newModelMemoryLayout = VoxelMemoryLayout.Linear;

        [MenuItem("VoxelRT/Tools/Mesh To Voxel Model")]
        public static void Open()
        {
            MeshToVoxelModelWindow window = GetWindow<MeshToVoxelModelWindow>();
            window.titleContent = new GUIContent("Mesh To Voxel");
            window.minSize = new Vector2(420f, 280f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _sourceMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", _sourceMesh, typeof(Mesh), false);
            VoxelModel previousTargetModel = _targetModel;
            _targetModel = (VoxelModel)EditorGUILayout.ObjectField("Target Model", _targetModel, typeof(VoxelModel), false);
            if (_targetModel != previousTargetModel)
            {
                _targetModelPath = _targetModel != null
                    ? AssetDatabase.GetAssetPath(_targetModel)
                    : null;
            }

            VoxelMemoryLayout resolvedMemoryLayout = ResolveRequestedMemoryLayout();
            using (new EditorGUI.DisabledScope(_targetModel != null))
            {
                VoxelMemoryLayout selectedLayout = (VoxelMemoryLayout)EditorGUILayout.EnumPopup("Memory Layout", resolvedMemoryLayout);
                if (_targetModel == null)
                {
                    _newModelMemoryLayout = selectedLayout;
                }
            }

            if (_targetModel != null)
            {
                EditorGUILayout.HelpBox(
                    $"Target model layout is fixed to {_targetModel.MemoryLayout}. Create a new asset to switch between Linear and Octant layouts.",
                    MessageType.Info);
            }

            _voxelSize = EditorGUILayout.FloatField("Voxel Size", _voxelSize);
            _solidVoxelValue = EditorGUILayout.IntSlider("Solid Voxel Value", _solidVoxelValue, 1, 255);

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
                "The tool assumes the mesh describes a closed solid. It runs a GPU conservative surface pass, classifies solid interior from a padded dense volume, then compacts the result into the VoxelModel chunk format.",
                MessageType.None);
            EditorGUILayout.HelpBox(
                "VoxelModel local space is rebased to the mesh bounds minimum corner, so chunk (0,0,0) starts at (0,0,0).",
                MessageType.None);

            using (new EditorGUI.DisabledScope(_sourceMesh == null || _voxelSize <= 0f))
            {
                if (GUILayout.Button(_targetModel == null ? "Create VoxelModel" : "Overwrite Target Model", GUILayout.Height(32f)))
                {
                    Bake();
                }
            }
        }

        private static void DrawMeshSummary(Mesh mesh, float voxelSize)
        {
            Bounds bounds = mesh.bounds;
            Vector3Int gridDimensions = MeshVoxelizer.CalculateGridDimensions(bounds, voxelSize);
            long voxelCount = (long)gridDimensions.x * gridDimensions.y * gridDimensions.z;

            EditorGUILayout.LabelField("Bounds Min", bounds.min.ToString("F4"));
            EditorGUILayout.LabelField("Bounds Max", bounds.max.ToString("F4"));
            EditorGUILayout.LabelField("Grid", $"{gridDimensions.x} x {gridDimensions.y} x {gridDimensions.z}");
            EditorGUILayout.LabelField("Estimated Voxels", voxelCount.ToString("N0"));

            if (voxelCount > 2_000_000L)
            {
                EditorGUILayout.HelpBox("The estimated voxel count is large. Expect a slow bake and a large asset.", MessageType.Warning);
            }
        }

        private void Bake()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            MeshVoxelizerGpu.AppendTraceLine("Bake begin");
            MeshVoxelizerGpu.EmitDebugLog("MeshVoxelizer bake | t=0 ms | Bake begin");

            MeshVoxelizerGpu.AppendTraceLine("ResolveTargetPath begin");
            MeshVoxelizerGpu.EmitDebugLog("MeshVoxelizer bake | ResolveTargetPath begin");
            string assetPath = ResolveTargetPath();
            MeshVoxelizerGpu.AppendTraceLine($"ResolveTargetPath end | t={stopwatch.ElapsedMilliseconds} ms | path={assetPath}");
            MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer bake | t={stopwatch.ElapsedMilliseconds} ms | ResolveTargetPath end | path={assetPath}");
            if (string.IsNullOrEmpty(assetPath))
            {
                MeshVoxelizerGpu.AppendTraceLine($"Bake aborted | t={stopwatch.ElapsedMilliseconds} ms | empty path");
                MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer bake | t={stopwatch.ElapsedMilliseconds} ms | Bake aborted | empty path");
                return;
            }

            try
            {
                MeshVoxelizerGpu.AppendTraceLine($"Create settings begin | t={stopwatch.ElapsedMilliseconds} ms");
                MeshVoxelizationSettings settings = new MeshVoxelizationSettings(
                    _voxelSize,
                    checked((byte)_solidVoxelValue),
                    ResolveRequestedMemoryLayout());
                MeshVoxelizerGpu.AppendTraceLine($"Create settings end | t={stopwatch.ElapsedMilliseconds} ms");

                MeshVoxelizerGpu.AppendTraceLine($"Voxelize begin | t={stopwatch.ElapsedMilliseconds} ms");
                MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer bake | t={stopwatch.ElapsedMilliseconds} ms | Voxelize begin");
                MeshVoxelizationResult result = MeshVoxelizerGpu.Voxelize(_sourceMesh, settings);
                MeshVoxelizerGpu.AppendTraceLine($"Voxelize end | t={stopwatch.ElapsedMilliseconds} ms | chunks={result.ChunkCount}");
                MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer bake | t={stopwatch.ElapsedMilliseconds} ms | Voxelize end | chunks={result.ChunkCount}");

                MeshVoxelizerGpu.AppendTraceLine($"WriteAsset begin | t={stopwatch.ElapsedMilliseconds} ms | path={assetPath}");
                MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer bake | t={stopwatch.ElapsedMilliseconds} ms | WriteAsset begin");
                VoxelModel asset = VoxelModelAssetWriter.WriteAsset(_targetModel, assetPath, result);
                MeshVoxelizerGpu.AppendTraceLine($"WriteAsset end | t={stopwatch.ElapsedMilliseconds} ms");
                MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer bake | t={stopwatch.ElapsedMilliseconds} ms | WriteAsset end");

                _targetModel = asset;
                _targetModelPath = assetPath;
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                MeshVoxelizerGpu.AppendTraceLine($"Bake end | t={stopwatch.ElapsedMilliseconds} ms");
                MeshVoxelizerGpu.EmitDebugLog($"MeshVoxelizer bake | t={stopwatch.ElapsedMilliseconds} ms | Bake end");
            }
            catch (OperationCanceledException)
            {
                MeshVoxelizerGpu.AppendTraceLine($"Bake canceled | t={stopwatch.ElapsedMilliseconds} ms");
            }
            catch (Exception exception)
            {
                MeshVoxelizerGpu.AppendTraceLine($"Bake exception | t={stopwatch.ElapsedMilliseconds} ms | {exception.GetType().Name}: {exception.Message}");
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Mesh To Voxel Model", exception.Message, "OK");
            }
        }

        private string ResolveTargetPath()
        {
            if (_targetModel != null)
            {
                if (!string.IsNullOrEmpty(_targetModelPath))
                {
                    VoxelModel loadedAsset = AssetDatabase.LoadAssetAtPath<VoxelModel>(_targetModelPath);
                    if (loadedAsset == _targetModel)
                    {
                        return _targetModelPath;
                    }
                }

                _targetModelPath = AssetDatabase.GetAssetPath(_targetModel);
                return _targetModelPath;
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
                "Create VoxelModel",
                defaultName,
                "asset",
                "Choose where to save the generated VoxelModel.",
                defaultDirectory);
        }

        private VoxelMemoryLayout ResolveRequestedMemoryLayout()
        {
            return _targetModel != null
                ? _targetModel.MemoryLayout
                : _newModelMemoryLayout;
        }
    }
}
