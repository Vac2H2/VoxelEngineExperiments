using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Serialization;
using VoxelEngine.Data.Voxel;
using VoxelEngine.Debugging;
using VoxelEngine.LifeCycle.Manager;
using VoxelEngine.Render.Cores;
using VoxelEngine.Render.RenderBackend;
using VoxelEngine.Render.RenderPipeline;

namespace VoxelEngine.Render.Debugging
{
    public enum VoxelGbufferPreviewTarget
    {
        Albedo = 0,
        Normal = 1,
        Depth = 2,
        Motion = 3,
        HitDist = 4,
        RawAo = 5,
        RawLight = 6,
        DenoisedLight = 7,
        RawReflection = 8,
        Reflection = 9,
        LitColor = 10,
        Smoothness = 11,
    }

    public static class VoxelGbufferDebugView
    {
        private static VoxelGbufferPreviewTarget _previewTarget = VoxelGbufferPreviewTarget.Albedo;

        public static VoxelGbufferPreviewTarget PreviewTarget
        {
            get => _previewTarget;
            set => _previewTarget = value;
        }

        public static void Reset()
        {
            _previewTarget = VoxelGbufferPreviewTarget.Albedo;
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Render/Debug/Voxel Gbuffer Debug Overlay")]
    public sealed class VoxelGbufferDebugOverlay : MonoBehaviour
    {
        private const float SettingsHintHeight = 24.0f;
        private const float SettingsHintSpacing = 6.0f;

        [SerializeField] private Vector2 _panelMargin = new Vector2(16.0f, 16.0f);
        [SerializeField] private float _panelWidth = 360.0f;
        [SerializeField] private int _fontSize = 15;
        [SerializeField] private float _buttonHeight = 28.0f;
        [SerializeField] private float _maxPanelHeight = 320.0f;

        private GUIStyle _hintStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private Vector2 _scrollPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeInstance()
        {
            if (!Application.isPlaying || !UsesVoxelEngineRenderPipeline())
            {
                return;
            }

            if (UnityEngine.Object.FindFirstObjectByType<VoxelGbufferDebugOverlay>() != null)
            {
                return;
            }

            GameObject root = new GameObject(nameof(VoxelGbufferDebugOverlay));
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<VoxelGbufferDebugOverlay>();
        }

        private void OnValidate()
        {
            _panelWidth = Mathf.Max(_panelWidth, 180.0f);
            _fontSize = Mathf.Max(_fontSize, 10);
            _buttonHeight = Mathf.Max(_buttonHeight, 20.0f);
            _maxPanelHeight = Mathf.Max(_maxPanelHeight, 120.0f);
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !UsesVoxelEngineRenderPipeline())
            {
                return;
            }

            EnsureStyles();
            DrawSettingsHint();

            if (!VoxelDebugSettingsUiState.IsVisible)
            {
                return;
            }

            TryGetActiveRenderPipelineAsset(out VoxelEngineRenderPipelineAsset renderPipelineAsset);
            TryGetActiveGbufferCore(out GbufferCore gbufferCore);
            TryGetActiveSunLightCore(out SunLightCore sunLightCore);
            TryGetActiveReflectionCore(out ReflectionCore reflectionCore);
            TryGetActiveRtaoCore(out RtaoCore rtaoCore);
            TryGetActiveRtaoFrameAverageCore(out RtaoFrameAverageCore frameAverageCore);
            TryGetActiveTaaCore(out TaaCore taaCore);
            float contentHeight = CalculatePanelContentHeight(
                renderPipelineAsset != null,
                gbufferCore != null,
                sunLightCore != null,
                reflectionCore != null,
                rtaoCore != null,
                frameAverageCore != null,
                taaCore != null);
            float panelHeight = Mathf.Min(
                contentHeight,
                Mathf.Max(Screen.height - (_panelMargin.y * 2.0f) - SettingsHintHeight - SettingsHintSpacing, 120.0f),
                _maxPanelHeight);
            Rect panelRect = new Rect(
                _panelMargin.x,
                _panelMargin.y + SettingsHintHeight + SettingsHintSpacing,
                _panelWidth,
                panelHeight);

            GUILayout.BeginArea(panelRect, GUIContent.none, _panelStyle);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, contentHeight > panelHeight);
            GUILayout.Label("Voxel G-buffer", _headerStyle);
            GUILayout.Label($"Showing: {VoxelGbufferDebugView.PreviewTarget}", _labelStyle);

            GUILayout.Space(6.0f);
            GUILayout.BeginHorizontal();
            DrawPreviewButton("Albedo", VoxelGbufferPreviewTarget.Albedo);
            DrawPreviewButton("Normal", VoxelGbufferPreviewTarget.Normal);
            DrawPreviewButton("Depth", VoxelGbufferPreviewTarget.Depth);
            DrawPreviewButton("Motion", VoxelGbufferPreviewTarget.Motion);
            DrawPreviewButton("HitDist", VoxelGbufferPreviewTarget.HitDist);
            GUILayout.EndHorizontal();

            GUILayout.Space(4.0f);
            GUILayout.BeginHorizontal();
            DrawPreviewButton("RawAO", VoxelGbufferPreviewTarget.RawAo);
            DrawPreviewButton("RawLight", VoxelGbufferPreviewTarget.RawLight);
            GUILayout.EndHorizontal();

            GUILayout.Space(4.0f);
            GUILayout.BeginHorizontal();
            DrawPreviewButton("DenoisedLight", VoxelGbufferPreviewTarget.DenoisedLight);
            DrawPreviewButton("RawRefl", VoxelGbufferPreviewTarget.RawReflection);
            DrawPreviewButton("Reflection", VoxelGbufferPreviewTarget.Reflection);
            GUILayout.EndHorizontal();

            GUILayout.Space(4.0f);
            GUILayout.BeginHorizontal();
            DrawPreviewButton("LitColor", VoxelGbufferPreviewTarget.LitColor);
            DrawPreviewButton("Smooth", VoxelGbufferPreviewTarget.Smoothness);
            GUILayout.EndHorizontal();

            GUILayout.Space(8.0f);
            if (GUILayout.Button("Reset Preview", _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                VoxelGbufferDebugView.Reset();
            }

            if (gbufferCore != null)
            {
                GUILayout.Space(8.0f);
                GUILayout.Label($"Global Roughness: {gbufferCore.GlobalRoughness:0.##}", _labelStyle);
                GUILayout.BeginHorizontal();
                DrawGlobalRoughnessButton(gbufferCore, 0.15f, "0.15");
                DrawGlobalRoughnessButton(gbufferCore, 0.35f, "0.35");
                DrawGlobalRoughnessButton(gbufferCore, 0.55f, "0.55");
                DrawGlobalRoughnessButton(gbufferCore, 0.8f, "0.80");
                GUILayout.EndHorizontal();

                GUILayout.Space(6.0f);
                GUILayout.Label(
                    $"Puddles: {(gbufferCore.PuddlesEnabled ? "On" : "Off")} | Coverage: {gbufferCore.PuddleCoverage:0.##}",
                    _labelStyle);
                GUILayout.BeginHorizontal();
                DrawPuddleToggleButton(gbufferCore, false, "Off");
                DrawPuddleToggleButton(gbufferCore, true, "On");
                GUILayout.EndHorizontal();

                GUILayout.Space(4.0f);
                GUILayout.BeginHorizontal();
                DrawPuddleCoverageButton(gbufferCore, 0.2f, "Low");
                DrawPuddleCoverageButton(gbufferCore, 0.45f, "Med");
                DrawPuddleCoverageButton(gbufferCore, 0.7f, "High");
                GUILayout.EndHorizontal();
            }

            if (renderPipelineAsset != null)
            {
                GUILayout.Space(6.0f);
                GUILayout.Label(
                    $"Sky: {(renderPipelineAsset.SkyTexture != null ? "On" : "Off")} | Exposure: {renderPipelineAsset.SkyExposure:0.##} | Rot: {renderPipelineAsset.SkyRotation:0}",
                    _labelStyle);
                GUILayout.BeginHorizontal();
                DrawSkyExposureButton(renderPipelineAsset, 0.5f, "0.5x");
                DrawSkyExposureButton(renderPipelineAsset, 1.0f, "1x");
                DrawSkyExposureButton(renderPipelineAsset, 2.0f, "2x");
                DrawSkyExposureButton(renderPipelineAsset, 4.0f, "4x");
                GUILayout.EndHorizontal();

                GUILayout.Space(4.0f);
                GUILayout.BeginHorizontal();
                DrawSkyRotationButton(renderPipelineAsset, 0.0f, "0");
                DrawSkyRotationButton(renderPipelineAsset, 90.0f, "90");
                DrawSkyRotationButton(renderPipelineAsset, 180.0f, "180");
                DrawSkyRotationButton(renderPipelineAsset, 270.0f, "270");
                GUILayout.EndHorizontal();
            }

            if (taaCore != null)
            {
                GUILayout.Space(6.0f);
                GUILayout.Label(
                    $"TAA: {(taaCore.Enabled ? "On" : "Off")} | Weight: {taaCore.HistoryWeight:0.##} | Jitter: {taaCore.JitterSpread:0.##}",
                    _labelStyle);
                GUILayout.BeginHorizontal();
                DrawTaaToggleButton(taaCore, false, "Off");
                DrawTaaToggleButton(taaCore, true, "On");
                GUILayout.EndHorizontal();

                GUILayout.Space(4.0f);
                GUILayout.BeginHorizontal();
                DrawTaaHistoryWeightButton(taaCore, 0.85f, "W 0.85");
                DrawTaaHistoryWeightButton(taaCore, 0.92f, "W 0.92");
                DrawTaaHistoryWeightButton(taaCore, 0.97f, "W 0.97");
                GUILayout.EndHorizontal();

                GUILayout.Space(4.0f);
                GUILayout.BeginHorizontal();
                DrawTaaJitterSpreadButton(taaCore, 0.0f, "J 0");
                DrawTaaJitterSpreadButton(taaCore, 0.25f, "J 0.25");
                DrawTaaJitterSpreadButton(taaCore, 0.5f, "J 0.5");
                DrawTaaJitterSpreadButton(taaCore, 1.0f, "J 1");
                GUILayout.EndHorizontal();
            }

            if (sunLightCore != null)
            {
                GUILayout.Space(6.0f);
                GUILayout.Label(
                    $"Sun Disk: {(sunLightCore.VisibleSunEnabled ? "On" : "Off")} | Intensity: {sunLightCore.VisibleSunIntensity:0.#}",
                    _labelStyle);
                GUILayout.BeginHorizontal();
                DrawSunDiskToggleButton(sunLightCore, false, "Disk Off");
                DrawSunDiskToggleButton(sunLightCore, true, "Disk On");
                GUILayout.EndHorizontal();

                GUILayout.Space(4.0f);
                GUILayout.BeginHorizontal();
                DrawVisibleSunIntensityButton(sunLightCore, 8.0f, "8x");
                DrawVisibleSunIntensityButton(sunLightCore, 18.0f, "18x");
                DrawVisibleSunIntensityButton(sunLightCore, 32.0f, "32x");
                GUILayout.EndHorizontal();

                GUILayout.Space(4.0f);
                GUILayout.Label("Sunlight Color", _labelStyle);
                GUILayout.BeginHorizontal();
                DrawSunLightColorButton(sunLightCore, Color.white, "White");
                DrawSunLightColorButton(sunLightCore, new Color(1.0f, 0.86f, 0.62f, 1.0f), "Warm");
                DrawSunLightColorButton(sunLightCore, new Color(1.0f, 0.62f, 0.32f, 1.0f), "Amber");
                DrawSunLightColorButton(sunLightCore, new Color(0.72f, 0.84f, 1.0f, 1.0f), "Cool");
                GUILayout.EndHorizontal();
            }

            if (reflectionCore != null)
            {
                GUILayout.Space(6.0f);
                GUILayout.Label(
                    $"Reflection: {(reflectionCore.Enabled ? "On" : "Off")} | Spatial: {(reflectionCore.SpatialDenoiseEnabled ? "On" : "Off")}",
                    _labelStyle);
                GUILayout.BeginHorizontal();
                DrawReflectionToggleButton(reflectionCore, false, "Off");
                DrawReflectionToggleButton(reflectionCore, true, "On");
                GUILayout.EndHorizontal();

                GUILayout.Space(4.0f);
                GUILayout.BeginHorizontal();
                DrawReflectionSpatialToggleButton(reflectionCore, false, "Spatial Off");
                DrawReflectionSpatialToggleButton(reflectionCore, true, "Spatial On");
                GUILayout.EndHorizontal();
            }

            if (rtaoCore != null)
            {
                GUILayout.Space(8.0f);
                GUILayout.Label($"RTAO Resolution: {rtaoCore.ResolutionMode}", _labelStyle);
                GUILayout.BeginHorizontal();
                DrawResolutionButton(rtaoCore, RtaoResolutionMode.Full, "Full");
                DrawResolutionButton(rtaoCore, RtaoResolutionMode.Half, "Half");
                GUILayout.EndHorizontal();

                GUILayout.Space(6.0f);
                GUILayout.Label($"HitDist RPP: {rtaoCore.AmbientRaysPerPixel}", _labelStyle);
                GUILayout.BeginHorizontal();
                DrawAmbientRtaoRppButton(rtaoCore, 1);
                DrawAmbientRtaoRppButton(rtaoCore, 2);
                DrawAmbientRtaoRppButton(rtaoCore, 4);
                DrawAmbientRtaoRppButton(rtaoCore, 8);
                GUILayout.EndHorizontal();

                GUILayout.Space(6.0f);
                GUILayout.Label($"RTAO Noise: {rtaoCore.NoiseMode}", _labelStyle);
                GUILayout.BeginHorizontal();
                DrawRtaoNoiseModeButton(rtaoCore, RtaoNoiseMode.AnimatedStbn, "Animated");
                DrawRtaoNoiseModeButton(rtaoCore, RtaoNoiseMode.FixedScreenStbn, "Fixed");
                GUILayout.EndHorizontal();

                if (frameAverageCore != null)
                {
                    GUILayout.Space(6.0f);
                    GUILayout.Label(
                        $"Spatial: {(frameAverageCore.SpatialEnabled ? "On" : "Off")} | R {frameAverageCore.SpatialRadius:0.#} | S {frameAverageCore.SpatialSampleCount}",
                        _labelStyle);
                    GUILayout.BeginHorizontal();
                    DrawSpatialToggleButton(frameAverageCore, false, "Off");
                    DrawSpatialToggleButton(frameAverageCore, true, "On");
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4.0f);
                    GUILayout.BeginHorizontal();
                    DrawSpatialRadiusButton(frameAverageCore, 2.0f, "R 2");
                    DrawSpatialRadiusButton(frameAverageCore, 4.0f, "R 4");
                    DrawSpatialRadiusButton(frameAverageCore, 8.0f, "R 8");
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4.0f);
                    GUILayout.BeginHorizontal();
                    DrawSpatialSampleCountButton(frameAverageCore, 8, "S 8");
                    DrawSpatialSampleCountButton(frameAverageCore, 12, "S 12");
                    DrawSpatialSampleCountButton(frameAverageCore, 16, "S 16");
                    GUILayout.EndHorizontal();

                    GUILayout.Space(6.0f);
                    DrawAmbientVisibilitySlider(frameAverageCore);

                    GUILayout.Space(4.0f);
                    DrawAoStepSizeSlider(frameAverageCore);

                    GUILayout.Space(4.0f);
                    DrawAoMinVisibilitySlider(frameAverageCore);

                    GUILayout.Space(4.0f);
                    GUILayout.Label("Ambient Light Color", _labelStyle);
                    GUILayout.BeginHorizontal();
                    DrawAmbientLightColorButton(frameAverageCore, Color.white, "White");
                    DrawAmbientLightColorButton(frameAverageCore, new Color(0.68f, 0.78f, 1.0f, 1.0f), "Sky");
                    DrawAmbientLightColorButton(frameAverageCore, new Color(1.0f, 0.82f, 0.62f, 1.0f), "Warm");
                    DrawAmbientLightColorButton(frameAverageCore, new Color(0.56f, 0.64f, 0.78f, 1.0f), "Dusk");
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSettingsHint()
        {
            Rect hintRect = new Rect(
                _panelMargin.x,
                _panelMargin.y,
                Mathf.Min(_panelWidth, 220.0f),
                SettingsHintHeight);
            GUI.Label(hintRect, VoxelDebugSettingsUiState.ToggleHintLabel, _hintStyle);
        }

        private void DrawPreviewButton(string label, VoxelGbufferPreviewTarget target)
        {
            Color originalColor = GUI.backgroundColor;
            if (VoxelGbufferDebugView.PreviewTarget == target)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                VoxelGbufferDebugView.PreviewTarget = target;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawAmbientRtaoRppButton(RtaoCore rtaoCore, int raysPerPixel)
        {
            Color originalColor = GUI.backgroundColor;
            if (rtaoCore.AmbientRaysPerPixel == raysPerPixel)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button($"{raysPerPixel} RPP", _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                rtaoCore.AmbientRaysPerPixel = raysPerPixel;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawResolutionButton(RtaoCore rtaoCore, RtaoResolutionMode resolutionMode, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (rtaoCore.ResolutionMode == resolutionMode)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                rtaoCore.ResolutionMode = resolutionMode;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawGlobalRoughnessButton(GbufferCore gbufferCore, float roughness, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Approximately(gbufferCore.GlobalRoughness, roughness))
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                gbufferCore.GlobalRoughness = roughness;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawPuddleToggleButton(GbufferCore gbufferCore, bool enabled, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (gbufferCore.PuddlesEnabled == enabled)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                gbufferCore.PuddlesEnabled = enabled;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawPuddleCoverageButton(GbufferCore gbufferCore, float coverage, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Approximately(gbufferCore.PuddleCoverage, coverage))
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                gbufferCore.PuddleCoverage = coverage;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawSkyExposureButton(VoxelEngineRenderPipelineAsset asset, float exposure, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Approximately(asset.SkyExposure, exposure))
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                asset.SkyExposure = exposure;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawSkyRotationButton(VoxelEngineRenderPipelineAsset asset, float rotation, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Abs(Mathf.DeltaAngle(asset.SkyRotation, rotation)) < 1e-3f)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                asset.SkyRotation = rotation;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawSunDiskToggleButton(SunLightCore sunLightCore, bool enabled, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (sunLightCore.VisibleSunEnabled == enabled)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                sunLightCore.VisibleSunEnabled = enabled;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawVisibleSunIntensityButton(SunLightCore sunLightCore, float intensity, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Approximately(sunLightCore.VisibleSunIntensity, intensity))
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                sunLightCore.VisibleSunIntensity = intensity;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawSunLightColorButton(SunLightCore sunLightCore, Color color, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (ApproximatelyColor(sunLightCore.SunLightColor, color))
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                sunLightCore.SunLightColor = color;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawRtaoNoiseModeButton(
            RtaoCore rtaoCore,
            RtaoNoiseMode noiseMode,
            string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (rtaoCore.NoiseMode == noiseMode)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                rtaoCore.NoiseMode = noiseMode;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawReflectionToggleButton(ReflectionCore reflectionCore, bool enabled, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (reflectionCore.Enabled == enabled)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                reflectionCore.Enabled = enabled;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawReflectionSpatialToggleButton(ReflectionCore reflectionCore, bool enabled, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (reflectionCore.SpatialDenoiseEnabled == enabled)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                reflectionCore.SpatialDenoiseEnabled = enabled;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawSpatialToggleButton(RtaoFrameAverageCore frameAverageCore, bool enabled, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (frameAverageCore.SpatialEnabled == enabled)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                frameAverageCore.SpatialEnabled = enabled;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawSpatialRadiusButton(RtaoFrameAverageCore frameAverageCore, float radius, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Abs(frameAverageCore.SpatialRadius - radius) < 1e-3f)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                frameAverageCore.SpatialRadius = radius;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawSpatialSampleCountButton(RtaoFrameAverageCore frameAverageCore, int sampleCount, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (frameAverageCore.SpatialSampleCount == sampleCount)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                frameAverageCore.SpatialSampleCount = sampleCount;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawAmbientVisibilitySlider(RtaoFrameAverageCore frameAverageCore)
        {
            GUILayout.Label($"Ambient Visibility: {frameAverageCore.AmbientVisibility:0.##}", _labelStyle);
            frameAverageCore.AmbientVisibility = GUILayout.HorizontalSlider(
                frameAverageCore.AmbientVisibility,
                0.0f,
                1.0f,
                GUILayout.Height(_buttonHeight));
        }

        private void DrawAoStepSizeSlider(RtaoFrameAverageCore frameAverageCore)
        {
            GUILayout.Label($"AO Quantize Step: {frameAverageCore.AoStepSize:0.##}", _labelStyle);
            frameAverageCore.AoStepSize = GUILayout.HorizontalSlider(
                frameAverageCore.AoStepSize,
                0.0f,
                20.0f,
                GUILayout.Height(_buttonHeight));
        }

        private void DrawAoMinVisibilitySlider(RtaoFrameAverageCore frameAverageCore)
        {
            GUILayout.Label($"AO Min Visibility: {frameAverageCore.AoMinVisibility:0.##}", _labelStyle);
            frameAverageCore.AoMinVisibility = GUILayout.HorizontalSlider(
                frameAverageCore.AoMinVisibility,
                0.0f,
                1.0f,
                GUILayout.Height(_buttonHeight));
        }

        private void DrawAmbientLightColorButton(RtaoFrameAverageCore frameAverageCore, Color color, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (ApproximatelyColor(frameAverageCore.AmbientLightColor, color))
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                frameAverageCore.AmbientLightColor = color;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawTaaToggleButton(TaaCore taaCore, bool enabled, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (taaCore.Enabled == enabled)
            {
                GUI.backgroundColor = new Color(0.35f, 0.7f, 0.95f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                taaCore.Enabled = enabled;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawTaaHistoryWeightButton(TaaCore taaCore, float historyWeight, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Abs(taaCore.HistoryWeight - historyWeight) < 1e-3f)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                taaCore.HistoryWeight = historyWeight;
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawTaaJitterSpreadButton(TaaCore taaCore, float jitterSpread, string label)
        {
            Color originalColor = GUI.backgroundColor;
            if (Mathf.Abs(taaCore.JitterSpread - jitterSpread) < 1e-3f)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.45f, 1.0f);
            }

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(_buttonHeight)))
            {
                taaCore.JitterSpread = jitterSpread;
            }

            GUI.backgroundColor = originalColor;
        }

        private static bool ApproximatelyColor(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 1e-3f &&
                   Mathf.Abs(left.g - right.g) < 1e-3f &&
                   Mathf.Abs(left.b - right.b) < 1e-3f &&
                   Mathf.Abs(left.a - right.a) < 1e-3f;
        }

        private float CalculatePanelContentHeight(
            bool hasSkyControls,
            bool hasGbufferControls,
            bool hasSunLightControls,
            bool hasReflectionControls,
            bool hasRtaoControls,
            bool hasFrameAverageControls,
            bool hasTaaControls)
        {
            float contentHeight = 188.0f;
            if (hasGbufferControls)
            {
                contentHeight += 150.0f;
            }

            if (hasSkyControls)
            {
                contentHeight += 82.0f;
            }

            if (hasSunLightControls)
            {
                contentHeight += 146.0f;
            }

            if (hasTaaControls)
            {
                contentHeight += 124.0f;
            }

            if (hasReflectionControls)
            {
                contentHeight += 96.0f;
            }

            if (!hasRtaoControls)
            {
                return contentHeight;
            }

            return contentHeight + (hasFrameAverageControls ? 522.0f : 222.0f);
        }

        private void EnsureStyles()
        {
            int hintFontSize = Mathf.Max(_fontSize - 2, 10);
            if (_hintStyle == null || _hintStyle.fontSize != hintFontSize)
            {
                _hintStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = hintFontSize,
                    padding = new RectOffset(8, 8, 4, 4)
                };
                _hintStyle.normal.textColor = new Color(0.92f, 0.95f, 1.0f, 1.0f);
            }

            if (_panelStyle == null)
            {
                _panelStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(12, 12, 10, 10)
                };
            }

            if (_headerStyle == null || _headerStyle.fontSize != _fontSize + 1)
            {
                _headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = _fontSize + 1,
                    fontStyle = FontStyle.Bold
                };
                _headerStyle.normal.textColor = Color.white;
            }

            if (_labelStyle == null || _labelStyle.fontSize != _fontSize)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = _fontSize
                };
                _labelStyle.normal.textColor = Color.white;
            }

            if (_buttonStyle == null || _buttonStyle.fontSize != _fontSize)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = _fontSize
                };
            }
        }

        private static bool UsesVoxelEngineRenderPipeline()
        {
            return RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline ||
                   GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset;
        }

        private static bool TryGetActiveRenderPipelineAsset(out VoxelEngineRenderPipelineAsset asset)
        {
            asset = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null)
                {
                    asset = renderPipeline.Asset;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset graphicsAsset)
            {
                asset = graphicsAsset;
                return true;
            }

            return false;
        }

        private static bool TryGetActiveRtaoCore(out RtaoCore rtaoCore)
        {
            rtaoCore = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null &&
                    renderPipeline.Asset.RtaoCore != null)
                {
                    rtaoCore = renderPipeline.Asset.RtaoCore;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset asset && asset.RtaoCore != null)
            {
                rtaoCore = asset.RtaoCore;
                return true;
            }

            return false;
        }

        private static bool TryGetActiveGbufferCore(out GbufferCore gbufferCore)
        {
            gbufferCore = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null &&
                    renderPipeline.Asset.GbufferCore != null)
                {
                    gbufferCore = renderPipeline.Asset.GbufferCore;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset asset && asset.GbufferCore != null)
            {
                gbufferCore = asset.GbufferCore;
                return true;
            }

            return false;
        }

        private static bool TryGetActiveSunLightCore(out SunLightCore sunLightCore)
        {
            sunLightCore = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null &&
                    renderPipeline.Asset.SunLightCore != null)
                {
                    sunLightCore = renderPipeline.Asset.SunLightCore;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset asset && asset.SunLightCore != null)
            {
                sunLightCore = asset.SunLightCore;
                return true;
            }

            return false;
        }

        private static bool TryGetActiveReflectionCore(out ReflectionCore reflectionCore)
        {
            reflectionCore = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null &&
                    renderPipeline.Asset.ReflectionCore != null)
                {
                    reflectionCore = renderPipeline.Asset.ReflectionCore;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset asset && asset.ReflectionCore != null)
            {
                reflectionCore = asset.ReflectionCore;
                return true;
            }

            return false;
        }

        private static bool TryGetActiveRtaoFrameAverageCore(out RtaoFrameAverageCore frameAverageCore)
        {
            frameAverageCore = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null &&
                    renderPipeline.Asset.RtaoFrameAverageCore != null)
                {
                    frameAverageCore = renderPipeline.Asset.RtaoFrameAverageCore;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset asset && asset.RtaoFrameAverageCore != null)
            {
                frameAverageCore = asset.RtaoFrameAverageCore;
                return true;
            }

            return false;
        }

        private static bool TryGetActiveTaaCore(out TaaCore taaCore)
        {
            taaCore = null;

            try
            {
                if (RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline &&
                    renderPipeline.Asset != null &&
                    renderPipeline.Asset.TaaCore != null)
                {
                    taaCore = renderPipeline.Asset.TaaCore;
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (GraphicsSettings.currentRenderPipeline is VoxelEngineRenderPipelineAsset asset && asset.TaaCore != null)
            {
                taaCore = asset.TaaCore;
                return true;
            }

            return false;
        }

    }

    [DisallowMultipleComponent]
    [AddComponentMenu("VoxelEngine/Render/Debug/Voxel Chunk Debugger")]
    public sealed class VoxelChunkDebugger : MonoBehaviour
    {
        private const byte OpaqueDebugVoxelId = 1;
        private const byte TransparentDebugVoxelId = 2;
        private const byte AabbLineVoxelId = 1;

        [SerializeField] private AssetReferenceVoxelModel _modelAsset;
        [SerializeField] private AssetReferenceVoxelPalette _paletteAsset;
        [SerializeField] private Vector3Int _chunkCoordinate = Vector3Int.zero;
        [SerializeField] private bool _includeOpaque = true;
        [SerializeField] private bool _includeTransparent = true;
        [SerializeField] private bool _renderVoxels = true;
        [FormerlySerializedAs("_renderAabbShells")]
        [SerializeField] private bool _renderAabbLines = true;
        [SerializeField, Range(0.01f, 0.5f)] private float _aabbLineWidth = 0.08f;
        [SerializeField] private bool _logAabbs = true;

        [NonSerialized] private VoxelEngineRenderBackend _registeredBackend;
        [NonSerialized] private VoxelEngineRenderInstanceHandle _voxelInstanceHandle;
        [NonSerialized] private readonly List<VoxelEngineRenderInstanceHandle> _aabbInstanceHandles = new List<VoxelEngineRenderInstanceHandle>();
        [NonSerialized] private string _registeredModelGuid = string.Empty;
        [NonSerialized] private string _registeredPaletteGuid = string.Empty;
        [NonSerialized] private Vector3Int _registeredChunkCoordinate;
        [NonSerialized] private bool _registeredIncludeOpaque;
        [NonSerialized] private bool _registeredIncludeTransparent;
        [NonSerialized] private bool _registeredRenderVoxels;
        [NonSerialized] private bool _registeredRenderAabbLines;
        [NonSerialized] private float _registeredAabbLineWidth;
        [NonSerialized] private bool _registeredLogAabbs;
        [NonSerialized] private VoxelEngineRenderBackend _lastFailedBackend;
        [NonSerialized] private string _lastFailedModelGuid = string.Empty;
        [NonSerialized] private string _lastFailedPaletteGuid = string.Empty;
        [NonSerialized] private string _lastLoggedState = string.Empty;
        [NonSerialized] private bool _hasRegistrationFailure;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            TryRefreshRegistration();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            TryRefreshRegistration();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ReleaseRegistration();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            TryRefreshRegistration();
        }

        private void TryRefreshRegistration()
        {
            string modelGuid = GetAssetGuid(_modelAsset);
            string paletteGuid = GetAssetGuid(_paletteAsset);

            if (!HasValidAssetReference())
            {
                LogStateOnce("missing-asset", "Waiting for a VoxelModel asset reference before registering chunk debug instances.");
                ClearRegistrationFailure();
                ReleaseRegistration(logRelease: false);
                return;
            }

            if (!_includeOpaque && !_includeTransparent)
            {
                LogStateOnce(
                    $"volumes-disabled:{modelGuid}:{_chunkCoordinate}",
                    "VoxelChunkDebugger has both opaque and transparent debug source volumes disabled.");
                ClearRegistrationFailure();
                ReleaseRegistration(logRelease: false);
                return;
            }

            if (!_renderVoxels && !_renderAabbLines)
            {
                LogStateOnce(
                    $"render-disabled:{modelGuid}:{_chunkCoordinate}",
                    "VoxelChunkDebugger has both voxel and AABB-line debug rendering disabled.");
                ClearRegistrationFailure();
                ReleaseRegistration(logRelease: false);
                return;
            }

            VoxelEngineRenderBackend currentBackend = TryGetCurrentBackend();
            if (currentBackend == null)
            {
                LogStateOnce("missing-backend", "Waiting for an active VoxelEngineRenderPipeline before registering chunk debug instances.");
                ReleaseRegistration(logRelease: false);
                return;
            }

            bool backendChanged = _registeredBackend != null && !ReferenceEquals(_registeredBackend, currentBackend);
            bool configurationChanged = HasRegisteredInstances() && !MatchesRegistration(modelGuid, paletteGuid);
            if (backendChanged || configurationChanged)
            {
                ReleaseRegistration(logRelease: false);
            }

            if (HasRegisteredInstances())
            {
                ClearRegistrationFailure();
                LogStateOnce(
                    $"registered:{_registeredModelGuid}:{_registeredPaletteGuid}:{_registeredChunkCoordinate}:{_voxelInstanceHandle.Value}:{_aabbInstanceHandles.Count}",
                    $"Registered chunk debugger for model '{_registeredModelGuid}' with palette '{_registeredPaletteGuid}' at chunk {_registeredChunkCoordinate}: voxel instance {_voxelInstanceHandle.Value}, all-chunk AABB overlay instances {_aabbInstanceHandles.Count}.");
                return;
            }

            if (IsSameFailure(currentBackend, modelGuid, paletteGuid))
            {
                return;
            }

            try
            {
                BuildAndRegister(currentBackend, modelGuid, paletteGuid);
                ClearRegistrationFailure();
                LogStateOnce(
                    $"registered:{_registeredModelGuid}:{_registeredPaletteGuid}:{_registeredChunkCoordinate}:{_voxelInstanceHandle.Value}:{_aabbInstanceHandles.Count}",
                    $"Registered chunk debugger for model '{_registeredModelGuid}' with palette '{_registeredPaletteGuid}' at chunk {_registeredChunkCoordinate}: voxel instance {_voxelInstanceHandle.Value}, all-chunk AABB overlay instances {_aabbInstanceHandles.Count}.");
            }
            catch (Exception exception)
            {
                RecordRegistrationFailure(currentBackend, modelGuid, paletteGuid);
                ReleaseRegistration(logRelease: false);
                LogStateOnce(
                    $"registration-failed:{modelGuid}:{paletteGuid}:{_chunkCoordinate}",
                    $"Failed to register chunk debugger for model '{modelGuid}' with palette '{paletteGuid}' at chunk {_chunkCoordinate}.");
                Debug.LogException(exception, this);
            }
        }

        private void BuildAndRegister(VoxelEngineRenderBackend backend, string modelGuid, string paletteGuid)
        {
            if (backend == null)
            {
                throw new ArgumentNullException(nameof(backend));
            }

            AsyncOperationHandle<VoxelModelAsset> loadHandle = default;
            AsyncOperationHandle<VoxelPaletteAsset> paletteLoadHandle = default;
            List<VoxelEngineRenderInstanceHandle> addedAabbHandles = null;
            VoxelEngineRenderInstanceHandle addedVoxelHandle = default;
            bool useSourcePalette = HasValidPaletteReference();

            try
            {
                loadHandle = _modelAsset.LoadAssetAsync();
                VoxelModelAsset modelAsset = loadHandle.WaitForCompletion();
                if (loadHandle.Status != AsyncOperationStatus.Succeeded || modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load VoxelModelAsset from Addressables runtime key '{_modelAsset.RuntimeKey}'.");
                }

                using VoxelModel sourceModel = VoxelModelSerializer.Deserialize(modelAsset, Allocator.Temp);

                if (_logAabbs)
                {
                    LogChunkAabbs(sourceModel, modelGuid);
                }

                if (_renderVoxels)
                {
                    using VoxelModel voxelDebugModel = BuildVoxelDebugModel(sourceModel, useSourcePalette);
                    if (!voxelDebugModel.IsEmpty)
                    {
                        using VoxelPalette voxelDebugPalette = useSourcePalette
                            ? LoadPaletteFromSource(ref paletteLoadHandle)
                            : BuildVoxelDebugPalette();
                        VoxelModelKey voxelModelKey = new VoxelModelKey(BuildVoxelModelRuntimeKey(modelGuid));
                        VoxelPaletteKey voxelPaletteKey = new VoxelPaletteKey(BuildVoxelPaletteRuntimeKey(modelGuid, paletteGuid, useSourcePalette));
                        addedVoxelHandle = backend.AddInstance(
                            voxelModelKey,
                            voxelPaletteKey,
                            voxelDebugModel,
                            voxelDebugPalette,
                            transform.localToWorldMatrix);
                    }
                }

                if (_renderAabbLines)
                {
                    addedAabbHandles = new List<VoxelEngineRenderInstanceHandle>();
                    AppendAabbLineInstances(backend, modelGuid, sourceModel, addedAabbHandles);
                }

                if (!addedVoxelHandle.IsValid && (addedAabbHandles == null || addedAabbHandles.Count == 0))
                {
                    throw new InvalidOperationException(
                        $"Chunk {_chunkCoordinate} in model '{modelGuid}' produced no debug voxel or AABB-line instances.");
                }

                _registeredBackend = backend;
                _voxelInstanceHandle = addedVoxelHandle;
                _aabbInstanceHandles.Clear();
                if (addedAabbHandles != null)
                {
                    _aabbInstanceHandles.AddRange(addedAabbHandles);
                }

                _registeredModelGuid = modelGuid;
                _registeredPaletteGuid = _renderVoxels && useSourcePalette ? paletteGuid : string.Empty;
                _registeredChunkCoordinate = _chunkCoordinate;
                _registeredIncludeOpaque = _includeOpaque;
                _registeredIncludeTransparent = _includeTransparent;
                _registeredRenderVoxels = _renderVoxels;
                _registeredRenderAabbLines = _renderAabbLines;
                _registeredAabbLineWidth = _aabbLineWidth;
                _registeredLogAabbs = _logAabbs;
            }
            catch
            {
                if (addedAabbHandles != null)
                {
                    for (int index = 0; index < addedAabbHandles.Count; index++)
                    {
                        backend.RemoveInstance(addedAabbHandles[index]);
                    }
                }

                if (addedVoxelHandle.IsValid)
                {
                    backend.RemoveInstance(addedVoxelHandle);
                }

                throw;
            }
            finally
            {
                if (loadHandle.IsValid())
                {
                    Addressables.Release(loadHandle);
                }

                if (paletteLoadHandle.IsValid())
                {
                    Addressables.Release(paletteLoadHandle);
                }
            }
        }

        private VoxelPalette LoadPaletteFromSource(ref AsyncOperationHandle<VoxelPaletteAsset> loadHandle)
        {
            loadHandle = _paletteAsset.LoadAssetAsync();
            VoxelPaletteAsset paletteAsset = loadHandle.WaitForCompletion();
            if (loadHandle.Status != AsyncOperationStatus.Succeeded || paletteAsset == null)
            {
                throw new InvalidOperationException(
                    $"Failed to load VoxelPaletteAsset from Addressables runtime key '{_paletteAsset.RuntimeKey}'.");
            }

            return VoxelPaletteSerializer.Deserialize(paletteAsset, Allocator.Temp);
        }

        private void AppendAabbLineInstances(
            VoxelEngineRenderBackend backend,
            string modelGuid,
            VoxelModel sourceModel,
            List<VoxelEngineRenderInstanceHandle> destination)
        {
            if (_includeOpaque)
            {
                AppendAabbLineInstancesForVolume(
                    backend,
                    modelGuid,
                    sourceModel.OpaqueVolume,
                    volumeTag: "opaque",
                    destination);
            }

            if (_includeTransparent)
            {
                AppendAabbLineInstancesForVolume(
                    backend,
                    modelGuid,
                    sourceModel.TransparentVolume,
                    volumeTag: "transparent",
                    destination);
            }
        }

        private void AppendAabbLineInstancesForVolume(
            VoxelEngineRenderBackend backend,
            string modelGuid,
            VoxelVolume sourceVolume,
            string volumeTag,
            List<VoxelEngineRenderInstanceHandle> destination)
        {
            if (sourceVolume == null)
            {
                throw new ArgumentNullException(nameof(sourceVolume));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            bool sourceWasTransparent = StringComparer.Ordinal.Equals(volumeTag, "transparent");
            for (int sourceChunkIndex = 0; sourceChunkIndex < sourceVolume.ChunkCapacity; sourceChunkIndex++)
            {
                if (!sourceVolume.IsChunkAllocated(sourceChunkIndex))
                {
                    continue;
                }

                NativeSlice<VoxelChunkAabb> aabbSlice = sourceVolume.GetChunkAabbSlice(sourceChunkIndex);
                if (CountActiveAabbs(aabbSlice) == 0)
                {
                    continue;
                }

                int3 chunkCoordinate = sourceVolume.ChunkCoordinates[sourceChunkIndex];
                using VoxelModel lineModel = BuildAabbLineDebugModel(chunkCoordinate, aabbSlice);
                using VoxelPalette linePalette = BuildAabbLineDebugPalette(chunkCoordinate, sourceWasTransparent);
                VoxelModelKey lineModelKey = new VoxelModelKey(
                    BuildAabbLineModelRuntimeKey(modelGuid, volumeTag, chunkCoordinate));
                VoxelPaletteKey linePaletteKey = new VoxelPaletteKey(
                    BuildAabbLinePaletteRuntimeKey(modelGuid, volumeTag, chunkCoordinate));
                VoxelEngineRenderInstanceHandle lineHandle = backend.AddDebugAabbOverlayInstance(
                    lineModelKey,
                    linePaletteKey,
                    lineModel,
                    linePalette,
                    transform.localToWorldMatrix,
                    _aabbLineWidth);
                destination.Add(lineHandle);
            }
        }

        private static int CountActiveAabbs(NativeSlice<VoxelChunkAabb> aabbSlice)
        {
            int activeCount = 0;
            for (int localAabbIndex = 0; localAabbIndex < aabbSlice.Length; localAabbIndex++)
            {
                if (aabbSlice[localAabbIndex].IsActive != 0)
                {
                    activeCount++;
                }
            }

            return activeCount;
        }

        private VoxelModel BuildVoxelDebugModel(VoxelModel sourceModel, bool preserveSourceVoxelIds)
        {
            if (sourceModel == null)
            {
                throw new ArgumentNullException(nameof(sourceModel));
            }

            int3 targetChunkCoordinate = new int3(_chunkCoordinate.x, _chunkCoordinate.y, _chunkCoordinate.z);
            VoxelModel debugModel = VoxelModel.Create(1, 1, Allocator.Temp);

            try
            {
                if (_includeOpaque)
                {
                    CopySourceChunk(
                        sourceModel.OpaqueVolume,
                        targetChunkCoordinate,
                        debugModel.OpaqueVolume,
                        OpaqueDebugVoxelId,
                        preserveSourceVoxelIds);
                }

                if (_includeTransparent)
                {
                    CopySourceChunk(
                        sourceModel.TransparentVolume,
                        targetChunkCoordinate,
                        debugModel.TransparentVolume,
                        TransparentDebugVoxelId,
                        preserveSourceVoxelIds);
                }

                return debugModel;
            }
            catch
            {
                debugModel.Dispose();
                throw;
            }
        }

        private static void CopySourceChunk(
            VoxelVolume sourceVolume,
            int3 chunkCoordinate,
            VoxelVolume destinationVolume,
            byte debugVoxelId,
            bool preserveSourceVoxelIds)
        {
            if (sourceVolume == null)
            {
                throw new ArgumentNullException(nameof(sourceVolume));
            }

            if (destinationVolume == null)
            {
                throw new ArgumentNullException(nameof(destinationVolume));
            }

            if (!sourceVolume.TryGetChunkIndex(chunkCoordinate, out int sourceChunkIndex))
            {
                return;
            }

            if (!destinationVolume.TryAllocateChunk(chunkCoordinate, out int destinationChunkIndex))
            {
                throw new InvalidOperationException($"Chunk {chunkCoordinate} was allocated more than once in debug model.");
            }

            NativeSlice<byte> sourceVoxelSlice = sourceVolume.GetChunkVoxelDataSlice(sourceChunkIndex);
            for (int voxelIndex = 0; voxelIndex < VoxelVolume.VoxelsPerChunk; voxelIndex++)
            {
                byte sourceVoxelId = sourceVoxelSlice[voxelIndex];
                if (sourceVoxelId == 0)
                {
                    continue;
                }

                int z = voxelIndex / (VoxelVolume.ChunkDimension * VoxelVolume.ChunkDimension);
                int yzRemainder = voxelIndex % (VoxelVolume.ChunkDimension * VoxelVolume.ChunkDimension);
                int y = yzRemainder / VoxelVolume.ChunkDimension;
                int x = yzRemainder % VoxelVolume.ChunkDimension;
                destinationVolume.SetVoxel(
                    destinationChunkIndex,
                    x,
                    y,
                    z,
                    preserveSourceVoxelIds ? sourceVoxelId : debugVoxelId);
            }

            NativeSlice<VoxelChunkAabb> sourceAabbSlice = sourceVolume.GetChunkAabbSlice(sourceChunkIndex);
            for (int localAabbIndex = 0; localAabbIndex < VoxelVolume.MaxAabbsPerChunk; localAabbIndex++)
            {
                VoxelChunkAabb sourceAabb = sourceAabbSlice[localAabbIndex];
                if (sourceAabb.IsActive == 0)
                {
                    continue;
                }

                if (!destinationVolume.TryAllocateAabbSlot(destinationChunkIndex, out int destinationAabbIndex))
                {
                    throw new InvalidOperationException("Debug voxel chunk ran out of AABB slots while copying source chunk.");
                }

                destinationVolume.SetAabb(destinationChunkIndex, destinationAabbIndex, sourceAabb.Min, sourceAabb.Max);
            }
        }

        private static VoxelModel BuildAabbLineDebugModel(int3 chunkCoordinate, NativeSlice<VoxelChunkAabb> sourceAabbSlice)
        {
            VoxelModel debugModel = VoxelModel.Create(1, 1, Allocator.Temp);

            try
            {
                if (!debugModel.OpaqueVolume.TryAllocateChunk(chunkCoordinate, out int opaqueChunkIndex))
                {
                    throw new InvalidOperationException($"Chunk {chunkCoordinate} was allocated more than once in debug AABB line model.");
                }

                for (int localAabbIndex = 0; localAabbIndex < sourceAabbSlice.Length; localAabbIndex++)
                {
                    VoxelChunkAabb sourceAabb = sourceAabbSlice[localAabbIndex];
                    if (sourceAabb.IsActive == 0)
                    {
                        continue;
                    }

                    if (!debugModel.OpaqueVolume.TryAllocateAabbSlot(opaqueChunkIndex, out int aabbIndex))
                    {
                        throw new InvalidOperationException("Debug AABB line chunk has no free AABB slots.");
                    }

                    debugModel.OpaqueVolume.SetAabb(opaqueChunkIndex, aabbIndex, sourceAabb.Min, sourceAabb.Max);
                }

                return debugModel;
            }
            catch
            {
                debugModel.Dispose();
                throw;
            }
        }

        private static VoxelPalette BuildVoxelDebugPalette()
        {
            VoxelPalette palette = new VoxelPalette(Allocator.Temp);
            palette[OpaqueDebugVoxelId] = ToVoxelColor(new Color32(255, 176, 66, 255));
            palette[TransparentDebugVoxelId] = ToVoxelColor(new Color32(78, 220, 255, 255));
            return palette;
        }

        private static VoxelPalette BuildAabbLineDebugPalette(int3 chunkCoordinate, bool sourceWasTransparent)
        {
            VoxelPalette palette = new VoxelPalette(Allocator.Temp);
            palette[AabbLineVoxelId] = ToVoxelColor(ComputeChunkDebugColor(chunkCoordinate, sourceWasTransparent));
            return palette;
        }

        private static Color ComputeChunkDebugColor(int3 chunkCoordinate, bool sourceWasTransparent)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)chunkCoordinate.x) * 16777619u;
                hash = (hash ^ (uint)chunkCoordinate.y) * 16777619u;
                hash = (hash ^ (uint)chunkCoordinate.z) * 16777619u;
                hash = (hash ^ (sourceWasTransparent ? 1u : 0u)) * 16777619u;

                float hue = (hash % 360u) / 360.0f;
                float saturation = sourceWasTransparent ? 0.45f : 0.35f;
                float value = 1.0f;
                return Color.HSVToRGB(hue, saturation, value);
            }
        }

        private static VoxelColor ToVoxelColor(Color color)
        {
            Color32 color32 = color;
            return new VoxelColor(color32.r, color32.g, color32.b, byte.MaxValue);
        }

        private void LogChunkAabbs(VoxelModel sourceModel, string modelGuid)
        {
            if (sourceModel == null)
            {
                throw new ArgumentNullException(nameof(sourceModel));
            }

            int3 targetChunkCoordinate = new int3(_chunkCoordinate.x, _chunkCoordinate.y, _chunkCoordinate.z);
            int totalAabbCount = 0;

            if (_includeOpaque)
            {
                totalAabbCount += LogVolumeAabbs(modelGuid, sourceModel.OpaqueVolume, targetChunkCoordinate, "opaque");
            }

            if (_includeTransparent)
            {
                totalAabbCount += LogVolumeAabbs(modelGuid, sourceModel.TransparentVolume, targetChunkCoordinate, "transparent");
            }

            Debug.Log(
                $"[{nameof(VoxelChunkDebugger)}] Found {totalAabbCount} active AABBs for model '{modelGuid}' at chunk {_chunkCoordinate}.",
                this);
        }

        private int LogVolumeAabbs(
            string modelGuid,
            VoxelVolume sourceVolume,
            int3 chunkCoordinate,
            string volumeTag)
        {
            if (sourceVolume == null)
            {
                throw new ArgumentNullException(nameof(sourceVolume));
            }

            int appendedCount = 0;
            if (!sourceVolume.TryGetChunkIndex(chunkCoordinate, out int sourceChunkIndex))
            {
                return appendedCount;
            }

            NativeSlice<VoxelChunkAabb> aabbSlice = sourceVolume.GetChunkAabbSlice(sourceChunkIndex);
            for (int localAabbIndex = 0; localAabbIndex < VoxelVolume.MaxAabbsPerChunk; localAabbIndex++)
            {
                VoxelChunkAabb sourceAabb = aabbSlice[localAabbIndex];
                if (sourceAabb.IsActive == 0)
                {
                    continue;
                }

                Vector3 objectSpaceMin = ToObjectSpacePosition(chunkCoordinate, sourceAabb.Min);
                Vector3 objectSpaceMax = ToObjectSpacePosition(chunkCoordinate, sourceAabb.Max);
                Bounds worldBounds = TransformAabb(transform.localToWorldMatrix, objectSpaceMin, objectSpaceMax);
                Debug.Log(
                    $"[{nameof(VoxelChunkDebugger)}] model='{modelGuid}' chunk={_chunkCoordinate} {volumeTag}[{localAabbIndex}] " +
                    $"sourceLocalMin={sourceAabb.Min} sourceLocalMax={sourceAabb.Max} " +
                    $"rtasObjectMin={objectSpaceMin} rtasObjectMax={objectSpaceMax} " +
                    $"rtasWorldMin={worldBounds.min} rtasWorldMax={worldBounds.max}",
                    this);
                appendedCount++;
            }

            return appendedCount;
        }

        private static Vector3 ToObjectSpacePosition(int3 chunkCoordinate, int3 localPosition)
        {
            int3 chunkOrigin = chunkCoordinate * VoxelVolume.ChunkDimension;
            return new Vector3(
                chunkOrigin.x + localPosition.x,
                chunkOrigin.y + localPosition.y,
                chunkOrigin.z + localPosition.z);
        }

        private static Bounds TransformAabb(Matrix4x4 localToWorld, Vector3 min, Vector3 max)
        {
            Vector3[] corners = new Vector3[8]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
            };

            Vector3 transformed = localToWorld.MultiplyPoint3x4(corners[0]);
            Vector3 worldMin = transformed;
            Vector3 worldMax = transformed;

            for (int index = 1; index < corners.Length; index++)
            {
                transformed = localToWorld.MultiplyPoint3x4(corners[index]);
                worldMin = Vector3.Min(worldMin, transformed);
                worldMax = Vector3.Max(worldMax, transformed);
            }

            Bounds bounds = new Bounds(worldMin, Vector3.zero);
            bounds.SetMinMax(worldMin, worldMax);
            return bounds;
        }

        private bool HasValidAssetReference()
        {
            return _modelAsset != null && _modelAsset.RuntimeKeyIsValid();
        }

        private bool HasValidPaletteReference()
        {
            return _paletteAsset != null && _paletteAsset.RuntimeKeyIsValid();
        }

        private bool HasRegisteredInstances()
        {
            return _voxelInstanceHandle.IsValid || _aabbInstanceHandles.Count > 0;
        }

        private bool MatchesRegistration(string modelGuid, string paletteGuid)
        {
            return HasRegisteredInstances() &&
                StringComparer.Ordinal.Equals(_registeredModelGuid, modelGuid) &&
                StringComparer.Ordinal.Equals(_registeredPaletteGuid, _renderVoxels && HasValidPaletteReference() ? paletteGuid : string.Empty) &&
                _registeredChunkCoordinate == _chunkCoordinate &&
                _registeredIncludeOpaque == _includeOpaque &&
                _registeredIncludeTransparent == _includeTransparent &&
                _registeredRenderVoxels == _renderVoxels &&
                _registeredRenderAabbLines == _renderAabbLines &&
                Mathf.Approximately(_registeredAabbLineWidth, _aabbLineWidth) &&
                _registeredLogAabbs == _logAabbs;
        }

        private bool IsSameFailure(VoxelEngineRenderBackend backend, string modelGuid, string paletteGuid)
        {
            return _hasRegistrationFailure &&
                ReferenceEquals(_lastFailedBackend, backend) &&
                StringComparer.Ordinal.Equals(_lastFailedModelGuid, modelGuid) &&
                StringComparer.Ordinal.Equals(_lastFailedPaletteGuid, paletteGuid);
        }

        private void RecordRegistrationFailure(VoxelEngineRenderBackend backend, string modelGuid, string paletteGuid)
        {
            _lastFailedBackend = backend;
            _lastFailedModelGuid = modelGuid;
            _lastFailedPaletteGuid = paletteGuid;
            _hasRegistrationFailure = true;
        }

        private void ClearRegistrationFailure()
        {
            _lastFailedBackend = null;
            _lastFailedModelGuid = string.Empty;
            _lastFailedPaletteGuid = string.Empty;
            _hasRegistrationFailure = false;
        }

        private void ReleaseRegistration(bool logRelease = true)
        {
            VoxelEngineRenderBackend releasedBackend = _registeredBackend;
            VoxelEngineRenderInstanceHandle releasedVoxelHandle = _voxelInstanceHandle;
            int releasedAabbLineCount = _aabbInstanceHandles.Count;
            string releasedModelGuid = _registeredModelGuid;
            string releasedPaletteGuid = _registeredPaletteGuid;
            Vector3Int releasedChunkCoordinate = _registeredChunkCoordinate;
            bool hadRegistration = releasedBackend != null && HasRegisteredInstances();

            if (releasedBackend != null)
            {
                for (int index = 0; index < _aabbInstanceHandles.Count; index++)
                {
                    try
                    {
                        releasedBackend.RemoveInstance(_aabbInstanceHandles[index]);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                }

                if (_voxelInstanceHandle.IsValid)
                {
                    try
                    {
                        releasedBackend.RemoveInstance(_voxelInstanceHandle);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                }
            }

            _registeredBackend = null;
            _voxelInstanceHandle = default;
            _aabbInstanceHandles.Clear();
            _registeredModelGuid = string.Empty;
            _registeredPaletteGuid = string.Empty;
            _registeredChunkCoordinate = default;
            _registeredIncludeOpaque = false;
            _registeredIncludeTransparent = false;
            _registeredRenderVoxels = false;
            _registeredRenderAabbLines = false;
            _registeredAabbLineWidth = 0.0f;
            _registeredLogAabbs = false;

            if (logRelease && hadRegistration)
            {
                LogStateOnce(
                    $"released:{releasedModelGuid}:{releasedPaletteGuid}:{releasedChunkCoordinate}:{releasedVoxelHandle.Value}:{releasedAabbLineCount}",
                    $"Released chunk debugger instances for model '{releasedModelGuid}' with palette '{releasedPaletteGuid}' at chunk {releasedChunkCoordinate}: voxel instance {releasedVoxelHandle.Value}, all-chunk AABB overlay instances {releasedAabbLineCount}.");
            }
        }

        private void LogStateOnce(string stateKey, string message)
        {
            if (StringComparer.Ordinal.Equals(_lastLoggedState, stateKey))
            {
                return;
            }

            _lastLoggedState = stateKey;
            Debug.Log($"[{nameof(VoxelChunkDebugger)}] {message}", this);
        }

        private static VoxelEngineRenderBackend TryGetCurrentBackend()
        {
            try
            {
                if (!(RenderPipelineManager.currentPipeline is VoxelEngineRenderPipeline renderPipeline))
                {
                    return null;
                }

                return renderPipeline.RenderBackend;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        private static string GetAssetGuid(AssetReference assetReference)
        {
            return assetReference?.AssetGUID ?? string.Empty;
        }

        private string BuildVoxelModelRuntimeKey(string modelGuid)
        {
            return $"runtime:debug:chunk-voxels:{modelGuid}:{_chunkCoordinate.x}:{_chunkCoordinate.y}:{_chunkCoordinate.z}:{_includeOpaque}:{_includeTransparent}";
        }

        private string BuildVoxelPaletteRuntimeKey(string modelGuid, string paletteGuid, bool usesSourcePalette)
        {
            return usesSourcePalette
                ? $"runtime:debug:chunk-voxel-palette:{modelGuid}:{paletteGuid}:{_includeOpaque}:{_includeTransparent}"
                : $"runtime:debug:chunk-voxel-palette:{modelGuid}:debug:{_includeOpaque}:{_includeTransparent}";
        }

        private string BuildAabbLineModelRuntimeKey(string modelGuid, string volumeTag, int3 chunkCoordinate)
        {
            return $"runtime:debug:chunk-aabb-line:{modelGuid}:{volumeTag}:{chunkCoordinate.x}:{chunkCoordinate.y}:{chunkCoordinate.z}";
        }

        private string BuildAabbLinePaletteRuntimeKey(string modelGuid, string volumeTag, int3 chunkCoordinate)
        {
            return $"runtime:debug:chunk-aabb-line-palette:{modelGuid}:{volumeTag}:{chunkCoordinate.x}:{chunkCoordinate.y}:{chunkCoordinate.z}";
        }
    }
}
