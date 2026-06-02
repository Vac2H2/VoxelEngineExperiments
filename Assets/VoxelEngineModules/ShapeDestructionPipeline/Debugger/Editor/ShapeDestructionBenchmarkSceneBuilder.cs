using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoxelEngineModules.Shape.Debugger.Editor
{
    public static class ShapeDestructionBenchmarkSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Shape Destruction Benchmark.unity";

        [MenuItem("VoxelEngine/Shape/Rebuild Shape Destruction Benchmark Scene")]
        public static void RebuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Shape Destruction Benchmark";

            CreateCamera();
            CreateSun();
            CreateBenchmark();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.ImportAsset(ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        [Shortcut("VoxelEngine/Shape/Rebuild Shape Destruction Benchmark Scene")]
        private static void RebuildSceneShortcut()
        {
            RebuildScene();
        }

        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";

            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500.0f;
            camera.fieldOfView = 55.0f;

            cameraObject.transform.position = new Vector3(0.0f, 12.0f, -24.0f);
            LookAt(cameraObject.transform, Vector3.zero);
        }

        private static void CreateSun()
        {
            GameObject sunObject = new GameObject("Directional Light");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.92f, 0.78f);
            sun.intensity = 1.2f;
            sunObject.transform.rotation = NormalizeRotation(Quaternion.Euler(48.0f, -30.0f, 0.0f));
            RenderSettings.sun = sun;
        }

        private static void CreateBenchmark()
        {
            GameObject benchmarkObject = new GameObject("Shape Destruction Pipeline Benchmark");
            ShapeDestructionPipelineBenchmark benchmark =
                benchmarkObject.AddComponent<ShapeDestructionPipelineBenchmark>();
            benchmark.Configure(
                new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1000 },
                warmupIterations: 2,
                measurementIterations: 8,
                destructionRadiusInVoxels: 4.0f,
                minimalVoxelNumber: 2,
                logStageBreakdown: true,
                logEachIteration: false,
                runOnStart: true);
        }

        private static void LookAt(Transform transform, Vector3 target)
        {
            Vector3 direction = target - transform.position;
            if (direction.sqrMagnitude <= 0.0f)
            {
                return;
            }

            transform.rotation = NormalizeRotation(Quaternion.LookRotation(direction.normalized, Vector3.up));
        }

        private static Quaternion NormalizeRotation(Quaternion rotation)
        {
            float lengthSquared =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            if (lengthSquared <= 0.0f || !float.IsFinite(lengthSquared))
            {
                return Quaternion.identity;
            }

            float inverseLength = 1.0f / Mathf.Sqrt(lengthSquared);
            return new Quaternion(
                rotation.x * inverseLength,
                rotation.y * inverseLength,
                rotation.z * inverseLength,
                rotation.w * inverseLength);
        }
    }
}
