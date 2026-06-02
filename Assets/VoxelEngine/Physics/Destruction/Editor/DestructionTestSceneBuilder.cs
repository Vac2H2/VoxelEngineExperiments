using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelEngine.Debugging;
using VoxelEngine.Physics.DebugRenderer;

namespace VoxelEngine.Physics.Destruction.Editor
{
    public static class DestructionTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Destruction.unity";

        [MenuItem("VoxelEngine/Physics/Rebuild Destruction Scene")]
        public static void RebuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Destruction";

            CreateCamera();
            CreateSun();
            CreateDestructibleBlock();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.ImportAsset(ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<CameraPlanarMovementController>();
            cameraObject.AddComponent<MiniHammer>();
            cameraObject.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500.0f;
            camera.fieldOfView = 55.0f;

            ApplyGlobalVoxelSizePosition(cameraObject, new Vector3(42.0f, 28.0f, -52.0f));
            LookAt(cameraObject.transform, ScaleByGlobalVoxelSize(new Vector3(0.0f, 10.0f, 0.0f)));
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

        private static void CreateDestructibleBlock()
        {
            GameObject blockObject = new GameObject("Destructible Voxel Block");
            blockObject.transform.rotation = Quaternion.identity;
            blockObject.transform.localScale = Vector3.one;
            ApplyGlobalVoxelSizePosition(blockObject, Vector3.zero);

            DestructionDebugRenderer renderer = blockObject.AddComponent<DestructionDebugRenderer>();
            renderer.AutoSyncDirtyData = true;
            DestructionDebugObjectBinding binding = blockObject.AddComponent<DestructionDebugObjectBinding>();
            blockObject.AddComponent<MiniDestructionEngine>();
            DestructionDebugBlockBootstrap bootstrap = blockObject.AddComponent<DestructionDebugBlockBootstrap>();
            bootstrap.Bind(renderer, binding);
            bootstrap.MinVoxel = new Vector3Int(-16, 0, -16);
            bootstrap.SizeInVoxels = new Vector3Int(32, 24, 32);
            bootstrap.Color = new Color32(72, 196, 84, 255);
            bootstrap.VoxelState = 1;
            bootstrap.RefreshCollider();
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

        private static void ApplyGlobalVoxelSizePosition(GameObject gameObject, Vector3 worldPositionAtVoxelSizeOne)
        {
            MiniPhysicsGlobalVoxelSizePosition globalVoxelSizePosition = gameObject.AddComponent<MiniPhysicsGlobalVoxelSizePosition>();
            globalVoxelSizePosition.WorldPositionAtVoxelSizeOne = worldPositionAtVoxelSizeOne;
        }

        private static Vector3 ScaleByGlobalVoxelSize(Vector3 worldPositionAtVoxelSizeOne)
        {
            return worldPositionAtVoxelSizeOne * VoxelEngineSettings.SanitizeVoxelSize(VoxelEngineSettings.GlobalVoxelSize);
        }

        private static Quaternion NormalizeRotation(Quaternion rotation)
        {
            float lengthSquared =
                (rotation.x * rotation.x) +
                (rotation.y * rotation.y) +
                (rotation.z * rotation.z) +
                (rotation.w * rotation.w);
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
