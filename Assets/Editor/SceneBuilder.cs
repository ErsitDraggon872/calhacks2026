using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CalHacks.EditorTools
{
    /// <summary>
    /// Generates Sandbox.unity (Wesley's prefab test bed) and the first greybox of Level.unity (Henry's) from prefabs.
    /// Level is only generated when missing unless forced, so hand edits to it are never clobbered by accident.
    /// </summary>
    public static class SceneBuilder
    {
        public const string SandboxPath = "Assets/Scenes/Sandbox.unity";
        public const string LevelPath = "Assets/Scenes/Level.unity";

        const float WallHeight = 1.2f;

        [MenuItem("Game/Rebuild Sandbox Scene")]
        public static void BuildSandbox()
        {
            var scene = NewScene();
            var geo = new GameObject("Geometry").transform;

            Box("Block", geo, new Vector3(-6f, -1f, -12f), new Vector3(6f, 0f, 12f));
            Box("Block", geo, new Vector3(10.6f, -1f, -12f), new Vector3(20f, 0f, 12f));
            Box("Block", geo, new Vector3(6f, -1f, -12f), new Vector3(10.6f, 0f, -1.6f));
            Box("Block", geo, new Vector3(6f, -1f, 1.6f), new Vector3(10.6f, 0f, 12f));

            var puzzles = new GameObject("Puzzles").transform;
            Place("GapSocket", puzzles, new Vector3(8.3f, 0f, 0f));
            Place("CaveSocket", puzzles, new Vector3(4f, 0f, 8f));
            Place("VillagerSocket", puzzles, new Vector3(4f, 0f, -8f));
            Place("Checkpoint", puzzles, new Vector3(14f, 0f, 0f));
            Place("Exit", puzzles, new Vector3(18f, 0f, -8f));

            Rig(new Vector3(0f, 0f, 0f));
            Save(scene, SandboxPath);
        }

        [MenuItem("Game/Rebuild Level Greybox (overwrites Level.unity)")]
        public static void BuildLevelForced() => BuildLevel(true);

        /// <summary>Start → gap → checkpoint → dark cave → checkpoint → villager → exit, along +X.</summary>
        public static void BuildLevel(bool overwrite)
        {
            if (!overwrite && File.Exists(LevelPath))
            {
                Debug.LogWarning("[Builder] Level.unity already exists; not overwriting.");
                return;
            }
            var scene = NewScene();
            var geo = new GameObject("Geometry").transform;

            // Ground, with the gap left open between x 10 and 14.6.
            Box("Block", geo, new Vector3(-6.5f, -1f, -2.6f), new Vector3(10f, 0f, 2.6f));
            Box("Block", geo, new Vector3(14.6f, -1f, -2.6f), new Vector3(55f, 0f, 2.6f));

            // Corridor walls, back wall and end wall.
            Walls(geo, "Wall", -6.5f, 10f, WallHeight);
            Walls(geo, "Wall", 14.6f, 22f, WallHeight);
            Walls(geo, "Rock", 22f, 34.4f, 2.6f);
            Walls(geo, "Wall", 34.4f, 55f, WallHeight);
            Box("Wall", geo, new Vector3(-7.5f, 0f, -2.6f), new Vector3(-6.5f, WallHeight, 2.6f));
            Box("Wall", geo, new Vector3(55f, 0f, -2.6f), new Vector3(56f, WallHeight, 2.6f));

            // Cave mouth pillars.
            Box("Rock", geo, new Vector3(21.6f, 0f, 1.4f), new Vector3(22.4f, 3f, 2.6f));
            Box("Rock", geo, new Vector3(21.6f, 0f, -2.6f), new Vector3(22.4f, 3f, -1.4f));

            // Narrow lane where the villager stands, so nobody squeezes past.
            Box("Wall", geo, new Vector3(39f, 0f, 1.2f), new Vector3(45f, WallHeight, 1.6f));
            Box("Wall", geo, new Vector3(39f, 0f, -1.6f), new Vector3(45f, WallHeight, -1.2f));

            var puzzles = new GameObject("Puzzles").transform;
            Place("GapSocket", puzzles, new Vector3(12.3f, 0f, 0f));
            Place("Checkpoint", puzzles, new Vector3(17f, 0f, 0f)).name = "Checkpoint 1";
            Place("CaveSocket", puzzles, new Vector3(22f, 0f, 0f));
            Place("Checkpoint", puzzles, new Vector3(36.5f, 0f, 0f)).name = "Checkpoint 2";
            Place("VillagerSocket", puzzles, new Vector3(44.2f, 0f, 0f));
            Place("Exit", puzzles, new Vector3(51f, 0f, 0f));

            Rig(new Vector3(-3f, 0f, 0f));
            Save(scene, LevelPath);
        }

        static Scene NewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Camera>() != null) Object.DestroyImmediate(root);
                else if (root.TryGetComponent<Light>(out var light))
                {
                    light.transform.rotation = Quaternion.Euler(55f, 15f, 0f);
                    light.shadows = LightShadows.Soft;
                }
            }
            return scene;
        }

        /// <summary>Start point, player, camera and the Game object, wired together.</summary>
        static void Rig(Vector3 start)
        {
            var startPoint = new GameObject("Start").transform;
            startPoint.SetPositionAndRotation(start, Quaternion.Euler(0f, 90f, 0f));

            var player = Place("Player", null, start);
            player.transform.rotation = startPoint.rotation;
            var cam = Place("CameraRig", null, start);
            var game = Place("Game", null, Vector3.zero);

            var follow = cam.GetComponent<IsoCameraFollow>();
            GameAssetBuilder.Fields(follow, ("target", player.transform));
            GameAssetBuilder.Fields(player.GetComponent<PlayerController>(), ("cameraTransform", cam.transform));
            GameAssetBuilder.Fields(game.GetComponent<RunManager>(),
                ("player", player.GetComponent<PlayerController>()),
                ("cameraFollow", follow),
                ("startPoint", startPoint));
            // Put the camera where it will be at runtime so the Scene/Game view previews match.
            cam.transform.rotation = Quaternion.Euler(30f, 45f, 0f);
            cam.transform.position = start + Vector3.up - cam.transform.forward * 30f;
        }

        static void Walls(Transform parent, string prefab, float x0, float x1, float height)
        {
            Box(prefab, parent, new Vector3(x0, 0f, 1.6f), new Vector3(x1, height, 2.6f));
            Box(prefab, parent, new Vector3(x0, 0f, -2.6f), new Vector3(x1, height, -1.6f));
        }

        static GameObject Box(string prefab, Transform parent, Vector3 min, Vector3 max)
        {
            var go = Place(prefab, parent, (min + max) * 0.5f);
            go.transform.localScale = max - min;
            return go;
        }

        static GameObject Place(string prefab, Transform parent, Vector3 position)
        {
            var asset = GameAssetBuilder.LoadPrefab(prefab);
            if (asset == null) throw new FileNotFoundException("Prefab missing; run Game/Build Assets first: " + prefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            return go;
        }

        static void Save(Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Builder] Saved " + path);
        }

        [MenuItem("Game/Set Build Scenes (Level)")]
        public static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(LevelPath, true),
                new EditorBuildSettingsScene(SandboxPath, false),
            };
        }
    }
}
