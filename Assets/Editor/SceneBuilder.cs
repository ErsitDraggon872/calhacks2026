using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CalHacks.EditorTools
{
    /// <summary>
    /// Generates Sandbox.unity (Wesley's prefab test bed) and the side-view greybox of Level.unity (Henry's) from prefabs.
    /// Level is only generated when missing unless forced, so hand edits to it are never clobbered by accident.
    /// </summary>
    public static class SceneBuilder
    {
        public const string SandboxPath = "Assets/Scenes/Sandbox.unity";
        public const string LevelPath = "Assets/Scenes/Level.unity";

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

        /// <summary>
        /// Side-view demo level along +X, with depth (Z) toward the back. Start ledge (front path + raised back terrace)
        /// → the gap (first build always works) → the hub: checkpoint, a high ledge and a floating island with the exit.
        /// The camera looks along +Z, so nothing solid stands in front (−Z) of the paths; invisible walls stop B
        /// walking off the front edge.
        /// </summary>
        public static void BuildLevel(bool overwrite)
        {
            if (!overwrite && File.Exists(LevelPath))
            {
                Debug.LogWarning("[Builder] Level.unity already exists; not overwriting.");
                return;
            }
            var scene = NewScene();
            var geo = new GameObject("Geometry").transform;

            // Start ledge, x -6..10: front path (z -1..1) and a raised back terrace (z 1..3, 0.8 m: jump up to it).
            Box("Block", geo, new Vector3(-6f, -1f, -1f), new Vector3(10f, 0f, 1f)).name = "Start Path";
            Box("Block", geo, new Vector3(-6f, -1f, 1f), new Vector3(10f, 0.8f, 3f)).name = "Start Terrace";

            // The gap is x 10..14.6 (GapSocket's deck). Hub floor, x 14.6..40, z -2..3.
            Box("Block", geo, new Vector3(14.6f, -1f, -2f), new Vector3(40f, 0f, 3f)).name = "Hub Floor";

            // High ledge on the back layer, 2.3 m: out of jump reach (1.2 m), one invention plus a jump gets up.
            Box("Rock", geo, new Vector3(24f, 0f, 0.5f), new Vector3(30f, 2.3f, 3f)).name = "High Ledge";

            // Floating island, top at 4 m, with the exit and the goal flag. Reached by stacking inventions
            // (or by building off the high ledge).
            Box("Rock", geo, new Vector3(34f, 3f, -1.5f), new Vector3(40f, 4f, 2.5f)).name = "Floating Island";

            // Backdrop wall behind everything and low end walls. Nothing in front, so the side camera sees in.
            Box("Wall", geo, new Vector3(-7f, -6f, 3f), new Vector3(41f, 6f, 3.6f)).name = "Back Wall";
            Box("Wall", geo, new Vector3(-7f, -1f, -2f), new Vector3(-6f, 2f, 3f)).name = "Left End";
            Box("Wall", geo, new Vector3(40f, -1f, -2f), new Vector3(41f, 6f, 3f)).name = "Right End";

            // Invisible front edges: B can't walk off toward the camera. The gap's own rails cover the bridge.
            Invisible("Front Edge Start", geo, new Vector3(-6f, 0f, -1.4f), new Vector3(10f, 4f, -1f));
            Invisible("Front Edge Hub", geo, new Vector3(14.6f, 0f, -2.4f), new Vector3(40f, 8f, -2f));
            Invisible("Front Edge Island", geo, new Vector3(34f, 4f, -1.9f), new Vector3(40f, 7f, -1.5f));

            var puzzles = new GameObject("Puzzles").transform;
            Place("GapSocket", puzzles, new Vector3(12.3f, 0f, 0f)); // its Hint says "Too far to jump!"
            Place("Checkpoint", puzzles, new Vector3(17f, 0f, 0.5f)).name = "Checkpoint 1";
            AddHint(puzzles, "Hint High Ledge", new Vector3(22.5f, 0f, -0.5f), new Vector3(30f, 2f, 3f), "Too high to climb!");
            Place("Exit", puzzles, new Vector3(38f, 4f, 0.5f));
            GoalFlag(puzzles, new Vector3(39.3f, 4f, 1.8f));

            // Start within CAST range (5 m) of the gap, so the opening shot is B at the gap and the first build bridges it.
            Rig(new Vector3(7.5f, 0f, 0f));
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

            // The paper body never rotates (it faces the camera); the start point's forward (+X) only sets B's facing.
            var player = Place("Player", null, start);
            var cam = Place("CameraRig", null, start);
            var game = Place("Game", null, Vector3.zero);

            var follow = cam.GetComponent<CameraFollow>();
            GameAssetBuilder.Fields(follow, ("target", player.transform));
            GameAssetBuilder.Fields(player.GetComponent<PlayerController>(), ("cameraTransform", cam.transform));
            GameAssetBuilder.Fields(game.GetComponent<RunManager>(),
                ("player", player.GetComponent<PlayerController>()),
                ("cameraFollow", follow),
                ("startPoint", startPoint));
            // Put the camera where CameraFollow's Tight framing puts it, so the Scene/Game view previews match.
            cam.transform.rotation = Quaternion.Euler(17f, 0f, 0f);
            cam.transform.position = start + Vector3.up - cam.transform.forward * 14f;
        }

        /// <summary>A collider with no renderer, from <paramref name="min"/> to <paramref name="max"/>.</summary>
        static GameObject Invisible(string name, Transform parent, Vector3 min, Vector3 max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = (min + max) * 0.5f;
            go.AddComponent<BoxCollider>().size = max - min;
            return go;
        }

        /// <summary>A HintZone trigger filling <paramref name="min"/>..<paramref name="max"/>.</summary>
        static void AddHint(Transform parent, string name, Vector3 min, Vector3 max, string text)
        {
            var go = Place("HintZone", parent, (min + max) * 0.5f);
            go.name = name;
            go.GetComponent<BoxCollider>().size = max - min;
            GameAssetBuilder.Fields(go.GetComponent<HintZone>(), ("text", text));
        }

        /// <summary>Goal flag on the island: looks only, no colliders (the Exit trigger next to it wins).</summary>
        static void GoalFlag(Transform parent, Vector3 foot)
        {
            var flag = new GameObject("Goal Flag").transform;
            flag.SetParent(parent, false);
            flag.position = foot;
            Prop(PrimitiveType.Cylinder, "Pole", flag, new Vector3(0f, 1.5f, 0f), new Vector3(0.12f, 1.5f, 0.12f), "Wall");
            Prop(PrimitiveType.Cube, "Cloth", flag, new Vector3(0.6f, 2.6f, 0f), new Vector3(1.1f, 0.7f, 0.05f), "Exit");
            Prop(PrimitiveType.Sphere, "Knob", flag, new Vector3(0f, 3.05f, 0f), new Vector3(0.25f, 0.25f, 0.25f), "Exit");
        }

        static void Prop(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, string material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = GameAssetBuilder.Mat(material);
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
