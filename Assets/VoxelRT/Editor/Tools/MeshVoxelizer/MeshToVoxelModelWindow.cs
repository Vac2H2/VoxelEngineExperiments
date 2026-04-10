using System;
using UnityEditor;
using UnityEngine;
using VoxelRT.Runtime.Data;

namespace VoxelRT.Editor.Tools.MeshVoxelizer
{
    internal enum MeshVoxelizationBackend
    {
        Cpu = 0,
        Gpu = 1,
    }

    public sealed class MeshToVoxelModelWindow : EditorWindow
    {
        [SerializeField] private Mesh _sourceMesh;
        [SerializeField] private VoxelModel _targetModel;
        [SerializeField] private MeshVoxelizationBackend _backend = MeshVoxelizationBackend.Gpu;
        [SerializeField] private float _voxelSize = 0.1f;
        [SerializeField] private int _solidVoxelValue = 1;

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
            _targetModel = (VoxelModel)EditorGUILayout.ObjectField("Target Model", _targetModel, typeof(VoxelModel), false);
            _backend = (MeshVoxelizationBackend)EditorGUILayout.EnumPopup("Backend", _backend);
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
                "The tool assumes the mesh describes a closed solid. Open meshes will voxelize from overlap tests and may produce shell-like output.",
                MessageType.None);

            if (_backend == MeshVoxelizationBackend.Gpu)
            {
                EditorGUILayout.HelpBox(
                    "GPU mode uses compute-based point-in-mesh classification and then compacts non-empty chunks on the CPU when writing the asset.",
                    MessageType.None);
            }

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
            string assetPath = ResolveTargetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            try
            {
                MeshVoxelizationSettings settings = new MeshVoxelizationSettings(
                    _voxelSize,
                    checked((byte)_solidVoxelValue));
                MeshVoxelizationResult result = _backend == MeshVoxelizationBackend.Gpu
                    ? MeshVoxelizerGpu.Voxelize(_sourceMesh, settings)
                    : MeshVoxelizer.Voxelize(_sourceMesh, settings);
                VoxelModel asset = VoxelModelAssetWriter.WriteAsset(_targetModel, assetPath, result);

                _targetModel = asset;
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Mesh To Voxel Model", exception.Message, "OK");
            }
        }

        private string ResolveTargetPath()
        {
            if (_targetModel != null)
            {
                return AssetDatabase.GetAssetPath(_targetModel);
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
    }
}
