using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelEngine.Debugging;
using VoxelEngine.Physics.Body;
using VoxelEngine.Physics.Collider;
using VoxelEngine.Physics.DebugRenderer;
using VoxelEngine.Physics.Simulation;

namespace VoxelEngine.Physics.Editor
{
    public static class MiniPhysicsBroadphaseTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Mini Physics Broadphase.unity";
        private const string DebugPalettePath = "Assets/VoxelEngine/Physics/DebugRenderer/MiniPhysicsDebugPalette.asset";

        [MenuItem("VoxelEngine/Physics/Rebuild Mini Physics Broadphase Scene")]
        public static void RebuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Mini Physics Solver";

            CreateCamera();
            CreateSun();

            GameObject runnerObject = new GameObject("Mini Physics Solver Debug Runner");
            runnerObject.AddComponent<MiniPhysicsBroadphaseDebugRunner>();
            MiniPhysicsDebugRenderer debugRenderer = runnerObject.AddComponent<MiniPhysicsDebugRenderer>();
            debugRenderer.Palette = EnsureDebugPaletteAsset();

            CreateVoxelPhysicsObject(
                "Static Arena Floor",
                new Vector3(-40.0f, -8.0f, -40.0f),
                Quaternion.identity,
                new int3(10, 1, 10),
                configureBody: null);

            CreateVoxelPhysicsObject(
                "Static Arena Wall West",
                new Vector3(-48.0f, 0.0f, -40.0f),
                Quaternion.identity,
                new int3(1, 2, 10),
                configureBody: null);

            CreateVoxelPhysicsObject(
                "Static Arena Wall East",
                new Vector3(40.0f, 0.0f, -40.0f),
                Quaternion.identity,
                new int3(1, 2, 10),
                configureBody: null);

            CreateVoxelPhysicsObject(
                "Static Arena Wall South",
                new Vector3(-40.0f, 0.0f, -48.0f),
                Quaternion.identity,
                new int3(10, 2, 1),
                configureBody: null);

            CreateVoxelPhysicsObject(
                "Static Arena Wall North",
                new Vector3(-40.0f, 0.0f, 40.0f),
                Quaternion.identity,
                new int3(10, 2, 1),
                configureBody: null);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.ImportAsset(ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        private static GameObject CreateVoxelPhysicsObject(
            string name,
            Vector3 worldPositionAtVoxelSizeOne,
            Quaternion rotation,
            int3 chunkDimensions,
            Action<MiniRigidBody> configureBody)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.rotation = rotation;
            gameObject.transform.localScale = Vector3.one;
            ApplyGlobalVoxelSizePosition(gameObject, worldPositionAtVoxelSizeOne);

            if (configureBody != null)
            {
                MiniRigidBody body = gameObject.AddComponent<MiniRigidBody>();
                body.BodyType = MiniRigidBodyType.Dynamic;
                configureBody(body);
            }

            MiniCollider collider = gameObject.AddComponent<MiniCollider>();
            collider.Data.SetChunks(CreateChunkBox(chunkDimensions));
            return gameObject;
        }

        private static MiniPhysicsDebugPaletteAsset EnsureDebugPaletteAsset()
        {
            MiniPhysicsDebugPaletteAsset palette = AssetDatabase.LoadAssetAtPath<MiniPhysicsDebugPaletteAsset>(DebugPalettePath);
            if (palette != null)
            {
                return palette;
            }

            palette = ScriptableObject.CreateInstance<MiniPhysicsDebugPaletteAsset>();
            AssetDatabase.CreateAsset(palette, DebugPalettePath);
            AssetDatabase.ImportAsset(DebugPalettePath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<MiniPhysicsDebugPaletteAsset>(DebugPalettePath);
        }

        private static MiniColliderChunkData[] CreateChunkBox(int3 dimensions)
        {
            int width = dimensions.x;
            int height = dimensions.y;
            int depth = dimensions.z;
            if (width <= 0 || height <= 0 || depth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Chunk cube dimensions must be greater than zero.");
            }

            MiniColliderChunkData[] chunks = new MiniColliderChunkData[checked(width * height * depth)];
            int index = 0;
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        chunks[index++] = new MiniColliderChunkData(new int3(x, y, z));
                    }
                }
            }

            return chunks;
        }

        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<CameraPlanarMovementController>();
            cameraObject.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500.0f;
            camera.fieldOfView = 55.0f;
            cameraObject.AddComponent<MiniPhysicsVoxelThrower>();
            ApplyGlobalVoxelSizePosition(cameraObject, new Vector3(28.0f, 24.0f, -58.0f));
            LookAt(cameraObject.transform, ScaleByGlobalVoxelSize(new Vector3(0.0f, 5.0f, 0.0f)));
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
