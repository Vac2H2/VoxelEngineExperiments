using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Random = System.Random;
using VoxelEngine.Data.Voxel;

namespace VoxelEngine.Editor.Tools
{
    public sealed class RandomVoxelPaletteAssetWindow : EditorWindow
    {
        [SerializeField] private VoxelPaletteAsset _targetAsset;
        [SerializeField] private int _seed = 12345;
        [SerializeField] private bool _keepIndexZeroTransparent = true;

        [MenuItem("VoxelEngine/Tools/Create Random VoxelPalette Asset")]
        public static void Open()
        {
            RandomVoxelPaletteAssetWindow window = GetWindow<RandomVoxelPaletteAssetWindow>();
            window.titleContent = new GUIContent("Random Palette");
            window.minSize = new Vector2(420.0f, 220.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Random VoxelPalette", EditorStyles.boldLabel);
            _seed = EditorGUILayout.IntField("Seed", _seed);
            _keepIndexZeroTransparent = EditorGUILayout.ToggleLeft("Keep Index 0 Transparent", _keepIndexZeroTransparent);
            _targetAsset = (VoxelPaletteAsset)EditorGUILayout.ObjectField("Target Asset", _targetAsset, typeof(VoxelPaletteAsset), false);

            if (GUILayout.Button("New Seed", GUILayout.Height(24.0f)))
            {
                _seed = Environment.TickCount;
                Repaint();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Color Count", VoxelPalette.ColorCount.ToString());
            EditorGUILayout.HelpBox(
                "Generates a strong-typed VoxelPaletteAsset with deterministic random colors from the current seed.",
                MessageType.None);

            if (GUILayout.Button(_targetAsset == null ? "Create Asset" : "Overwrite Asset", GUILayout.Height(32.0f)))
            {
                GenerateAsset();
            }
        }

        private void GenerateAsset()
        {
            string assetPath = ResolveTargetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            try
            {
                byte[] serializedBytes = BuildPaletteBytes(_seed, _keepIndexZeroTransparent);
                _targetAsset = WriteAsset(assetPath, serializedBytes);
                if (_targetAsset != null)
                {
                    Selection.activeObject = _targetAsset;
                    EditorGUIUtility.PingObject(_targetAsset);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Random VoxelPalette Asset", exception.Message, "OK");
            }
        }

        private string ResolveTargetPath()
        {
            if (_targetAsset != null)
            {
                string existingAssetPath = AssetDatabase.GetAssetPath(_targetAsset);
                if (string.IsNullOrWhiteSpace(existingAssetPath))
                {
                    EditorUtility.DisplayDialog(
                        "Random VoxelPalette Asset",
                        "Target asset must be a persistent VoxelPaletteAsset.",
                        "OK");
                    return null;
                }

                return existingAssetPath;
            }

            return EditorUtility.SaveFilePanelInProject(
                "Create Random VoxelPalette Asset",
                $"palette_random_{_seed}",
                "asset",
                "Choose where to save the generated VoxelPalette asset.",
                "Assets");
        }

        private static VoxelPaletteAsset WriteAsset(string assetPath, byte[] serializedBytes)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("Asset path must be a non-empty string.", nameof(assetPath));
            }

            if (serializedBytes == null)
            {
                throw new ArgumentNullException(nameof(serializedBytes));
            }

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

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<VoxelPaletteAsset>(assetPath);
        }

        private static byte[] BuildPaletteBytes(int seed, bool keepIndexZeroTransparent)
        {
            using VoxelPalette palette = new VoxelPalette(Allocator.Temp);
            FillPalette(palette, seed, keepIndexZeroTransparent);
            return VoxelPaletteSerializer.SerializeToBytes(palette);
        }

        private static void FillPalette(VoxelPalette palette, int seed, bool keepIndexZeroTransparent)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            Random random = new Random(seed);

            for (int index = 0; index < VoxelPalette.ColorCount; index++)
            {
                if (keepIndexZeroTransparent && index == 0)
                {
                    palette[index] = new VoxelColor(0, 0, 0, 0);
                    continue;
                }

                Color color = Color.HSVToRGB(
                    NextFloat(random),
                    Mathf.Lerp(0.55f, 0.95f, NextFloat(random)),
                    Mathf.Lerp(0.70f, 1.00f, NextFloat(random)));
                palette[index] = new VoxelColor(
                    ToByte(color.r),
                    ToByte(color.g),
                    ToByte(color.b),
                    byte.MaxValue);
            }
        }

        private static float NextFloat(Random random)
        {
            return (float)random.NextDouble();
        }

        private static byte ToByte(float value)
        {
            return checked((byte)Mathf.Clamp(Mathf.RoundToInt(value * byte.MaxValue), 0, byte.MaxValue));
        }
    }
}
