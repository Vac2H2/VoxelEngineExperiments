using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoxelEngineModules.Shape.Debugger.Editor
{
    public static class ShapePhysicsDebugSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Shape Physics Debug.unity";

        [MenuItem("VoxelEngine/Shape/Rebuild Shape Physics Debug Scene")]
        public static void RebuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Shape Physics Debug";

            Camera camera = CreateCamera();
            CreateSun();
            GameObject runnerObject = CreateMiniPhysicsRunner();
            CreateShapePhysicsDebugger(camera, runnerObject);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.ImportAsset(ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        [Shortcut("VoxelEngine/Shape/Rebuild Shape Physics Debug Scene")]
        private static void RebuildSceneShortcut()
        {
            RebuildScene();
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            TryAddComponent(cameraObject, "VoxelEngine.Debugging.CameraPlanarMovementController");
            cameraObject.tag = "MainCamera";

            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500.0f;
            camera.fieldOfView = 55.0f;

            cameraObject.transform.position = new Vector3(34.0f, 30.0f, -54.0f);
            LookAt(cameraObject.transform, new Vector3(0.0f, 6.0f, 0.0f));
            return camera;
        }

        private static GameObject CreateMiniPhysicsRunner()
        {
            GameObject runnerObject = new GameObject("Mini Physics Runner");
            TryAddComponent(runnerObject, "VoxelEngine.Physics.Simulation.MiniPhysicsBroadphaseDebugRunner");
            return runnerObject;
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

        private static void CreateShapePhysicsDebugger(
            Camera camera,
            GameObject runnerObject)
        {
            GameObject debuggerObject = new GameObject("Shape Physics Debugger");
            debuggerObject.transform.position = Vector3.zero;
            debuggerObject.transform.rotation = Quaternion.identity;
            debuggerObject.transform.localScale = Vector3.one;

            ShapeDebugRenderer renderer = debuggerObject.AddComponent<ShapeDebugRenderer>();
            renderer.VoxelSize = 1.0f;

            ShapePhysicsDebugger debugger = debuggerObject.AddComponent<ShapePhysicsDebugger>();
            debugger.Configure(
                camera,
                runnerObject,
                shapeCapacity: 64,
                destructionRadiusInVoxels: 2.0f,
                minimalVoxelNumber: 2,
                voxelSize: 1.0f);
        }

        private static void TryAddComponent(GameObject gameObject, string typeName)
        {
            Type componentType = FindType(typeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                Debug.LogWarning($"Could not find component type '{typeName}' while building the Shape Physics Debug scene.");
                return;
            }

            gameObject.AddComponent(componentType);
        }

        private static Type FindType(string typeName)
        {
            Type type = Type.GetType($"{typeName}, Assembly-CSharp");
            if (type != null)
            {
                return type;
            }

            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
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
