using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Data.Voxel;
using VoxelEngine.Data.VoxelWorldHierarchy;

namespace VoxelEngine.Editor.Importer
{
    public sealed class VoxImportWindow : EditorWindow
    {
        [SerializeField] private string _sourceFilePath;
        [SerializeField] private DefaultAsset _targetFolder;
        [SerializeField] private int _maxAabbsPerChunk = 1;

        [MenuItem("VoxelEngine/Importer/Vox Importer")]
        public static void Open()
        {
            VoxImportWindow window = GetWindow<VoxImportWindow>();
            window.titleContent = new GUIContent("Vox Importer");
            window.minSize = new Vector2(540f, 280f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            DrawSourceFileField();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            DrawTargetFolderField();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Chunk AABB", EditorStyles.boldLabel);
            DrawChunkAabbOptions();

            EditorGUILayout.Space();
            DrawSummary();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!CanImport(out string validationMessage)))
            {
                if (GUILayout.Button("Import .vox", GUILayout.Height(32f)))
                {
                    Import();
                }
            }

            if (!CanImport(out string message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }

        private void DrawSourceFileField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Source .vox");
                _sourceFilePath = EditorGUILayout.TextField(_sourceFilePath ?? string.Empty);
                if (GUILayout.Button("Browse", GUILayout.Width(84f)))
                {
                    string selectedPath = EditorUtility.OpenFilePanel(
                        "Select .vox File",
                        ResolveInitialSourceDirectory(),
                        "vox");
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        _sourceFilePath = selectedPath;
                    }
                }
            }
        }

        private void DrawTargetFolderField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    "Target Folder",
                    _targetFolder,
                    typeof(DefaultAsset),
                    false);

                if (GUILayout.Button("Browse", GUILayout.Width(84f)))
                {
                    string selectedFolder = EditorUtility.OpenFolderPanel(
                        "Select Target Folder",
                        ResolveInitialTargetDirectory(),
                        string.Empty);
                    if (TryConvertToProjectFolder(selectedFolder, out string projectFolderPath))
                    {
                        _targetFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(projectFolderPath);
                    }
                    else if (!string.IsNullOrWhiteSpace(selectedFolder))
                    {
                        EditorUtility.DisplayDialog(
                            "Vox Importer",
                            "The target folder must be inside this Unity project's Assets folder.",
                            "OK");
                    }
                }
            }
        }

        private void DrawChunkAabbOptions()
        {
            _maxAabbsPerChunk = EditorGUILayout.IntSlider(
                new GUIContent("Max AABBs / Chunk"),
                _maxAabbsPerChunk,
                1,
                VoxelVolume.MaxAabbsPerChunk);
        }

        private void DrawSummary()
        {
            if (!string.IsNullOrWhiteSpace(_sourceFilePath))
            {
                EditorGUILayout.LabelField("File", Path.GetFileName(_sourceFilePath));
            }

            if (TryGetTargetFolderPath(out string targetFolderPath))
            {
                string importName = Path.GetFileNameWithoutExtension(_sourceFilePath ?? string.Empty);
                importName = string.IsNullOrWhiteSpace(importName) ? "ImportedVox" : importName;
                EditorGUILayout.LabelField("Import Folder", $"{targetFolderPath}/{importName}");
            }

            EditorGUILayout.LabelField(
                "Chunk AABB Budget",
                _maxAabbsPerChunk == 1
                    ? "1 (single tight bounds per chunk)"
                    : $"{_maxAabbsPerChunk} (strict greedy cuboids, with one fallback slot if needed)");

            EditorGUILayout.HelpBox(
                "The importer creates one shared VoxelPaletteAsset, one VoxelModelAsset per VOX model, and one VoxelWorldHierarchy asset. Generated model and palette assets are registered into Addressables automatically.\n\n" +
                "When Max AABBs / Chunk is greater than 1, the importer tries to split each 8x8x8 chunk into strict non-overlapping solid cuboids. A value of 1 skips this optimization and keeps one tight bounds per chunk.",
                MessageType.None);
        }

        private bool CanImport(out string validationMessage)
        {
            if (string.IsNullOrWhiteSpace(_sourceFilePath))
            {
                validationMessage = "Select a source .vox file.";
                return false;
            }

            if (!File.Exists(_sourceFilePath))
            {
                validationMessage = "The selected .vox file does not exist.";
                return false;
            }

            if (!string.Equals(Path.GetExtension(_sourceFilePath), ".vox", StringComparison.OrdinalIgnoreCase))
            {
                validationMessage = "The source file must use the .vox extension.";
                return false;
            }

            if (!TryGetTargetFolderPath(out _))
            {
                validationMessage = "Select a valid target folder under Assets.";
                return false;
            }

            validationMessage = null;
            return true;
        }

        private void Import()
        {
            try
            {
                if (!TryGetTargetFolderPath(out string targetFolderPath))
                {
                    throw new InvalidOperationException("Target folder is invalid.");
                }

                VoxelWorldHierarchy hierarchy = VoxImporter.Import(
                    _sourceFilePath,
                    targetFolderPath,
                    new VoxImportOptions(_maxAabbsPerChunk));
                Selection.activeObject = hierarchy;
                EditorGUIUtility.PingObject(hierarchy);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Vox Importer", exception.Message, "OK");
            }
        }

        private bool TryGetTargetFolderPath(out string targetFolderPath)
        {
            targetFolderPath = null;
            if (_targetFolder == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(_targetFolder);
            if (string.IsNullOrWhiteSpace(assetPath) || !AssetDatabase.IsValidFolder(assetPath))
            {
                return false;
            }

            targetFolderPath = assetPath;
            return true;
        }

        private static bool TryConvertToProjectFolder(string absoluteFolderPath, out string projectFolderPath)
        {
            projectFolderPath = null;
            if (string.IsNullOrWhiteSpace(absoluteFolderPath))
            {
                return false;
            }

            string normalizedAbsolutePath = Path.GetFullPath(absoluteFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedAssetsPath = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!normalizedAbsolutePath.StartsWith(normalizedAssetsPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string relativeTail = normalizedAbsolutePath.Substring(normalizedAssetsPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            projectFolderPath = string.IsNullOrEmpty(relativeTail)
                ? "Assets"
                : $"Assets/{relativeTail.Replace('\\', '/')}";
            return AssetDatabase.IsValidFolder(projectFolderPath);
        }

        private string ResolveInitialSourceDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_sourceFilePath) && File.Exists(_sourceFilePath))
            {
                return Path.GetDirectoryName(_sourceFilePath);
            }

            return Application.dataPath;
        }

        private string ResolveInitialTargetDirectory()
        {
            if (TryGetTargetFolderPath(out string targetFolderPath))
            {
                string absolutePath = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, targetFolderPath));
                if (Directory.Exists(absolutePath))
                {
                    return absolutePath;
                }
            }

            return Application.dataPath;
        }
    }
}
