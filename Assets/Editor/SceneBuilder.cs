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
            Place("Checkpoint", puzzles, new Vector3(14f, 0f, 0f));
            Place("Exit", puzzles, new Vector3(18f, 0f, -8f));

            Rig(new Vector3(0f, 0f, 0f));
            Save(scene, SandboxPath);
        }

        [MenuItem("Game/Rebuild Level Greybox (overwrites Level.unity)")]
        public static void BuildLevelForced() => BuildLevel(true);

        /// <summary>
        /// Side-view demo level along +X, with depth (Z) toward the back. Start ledge (front path + raised back terrace)
        /// → the gap (every drawing becomes a bridge, solid where it's inked) → the hub: checkpoint, then two puzzles in
        /// either order up to a floating island with the exit: a high ledge (build a step) and the gatekeeper (feed them
        /// and they open the gate to the island stairs).
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
            Walkable("Block", geo, "Start Path", new Vector3(-6f, -1f, -1f), new Vector3(10f, 0f, 1f));
            Walkable("Block", geo, "Start Terrace", new Vector3(-6f, -1f, 1f), new Vector3(10f, 0.8f, 3f));

            // The gap is x 10..14.6 (GapSocket's 4.9 m bridge overlaps both ledges). Hub floor, x 14.6..40, z -2..3.
            Walkable("Block", geo, "Hub Floor", new Vector3(14.6f, -1f, -2f), new Vector3(40f, 0f, 3f));

            // High ledge on the back layer, 2.3 m: out of jump reach (1.2 m), one invention plus a jump gets up. It runs
            // right up to the stairs (x 31), so there's no pocket between them to fall into behind the gate.
            Walkable("Rock", geo, "High Ledge", new Vector3(24f, 0f, 0.5f), new Vector3(31f, 2.3f, 3f));

            // The island: a cliff from the hub floor up to 4 m, with the exit and the goal flag. Reached by the stairs.
            Walkable("Rock", geo, "Floating Island", new Vector3(34f, 0f, -1.5f), new Vector3(40f, 4f, 3f))
                .GetComponent<MeshRenderer>().sharedMaterial = GameAssetBuilder.Mat("Sandstone");

            // Island stairs, x 31..34: five 0.8 m steps climbing toward the back (z -2..3), the top one level with the
            // island. Only the two lowest steps are in reach from the floor (B rides edges up to ~1.65 m), and the
            // gatekeeper's gate closes their side. The other ways up: from the high ledge (step 3 is right beside it and 0.1 m
            // higher), or builds.
            for (var i = 0; i < 5; i++)
            {
                Walkable("Rock", geo, $"Stair {i + 1}", new Vector3(31f, 0f, -2f + i), new Vector3(34f, 0.8f * (i + 1), -1f + i))
                    .GetComponent<MeshRenderer>().sharedMaterial = GameAssetBuilder.Mat("Sandstone");
            }

            // Backdrop wall behind everything, low enough that the sky and hills show above it, and end walls. Nothing in
            // front, so the side camera sees in. Both stand well above a jump from the terrace (~2 m) so they read as
            // solid. It runs well past both ends so the Wide framing never sees round it.
            Box("Wall", geo, new Vector3(-30f, -16f, 3f), new Vector3(70f, 2.8f, 3.6f)).name = "Back Wall";
            Box("Wall", geo, new Vector3(-7f, -1f, -2f), new Vector3(-6f, 3.5f, 3f)).name = "Left End";
            Box("Wall", geo, new Vector3(40f, -1f, -2f), new Vector3(41f, 6f, 3f)).name = "Right End";

            // Invisible back edge above the wall: B can't step off the back of the high ledge or the island.
            Invisible("Back Edge", geo, new Vector3(-7f, 2.8f, 3f), new Vector3(41f, 10f, 3.6f));
            Hills();

            // Invisible front edges: B can't walk off toward the camera. The gap's own rails cover the bridge.
            Invisible("Front Edge Start", geo, new Vector3(-6f, 0f, -1.4f), new Vector3(10f, 4f, -1f));
            Invisible("Front Edge Hub", geo, new Vector3(14.6f, 0f, -2.4f), new Vector3(40f, 8f, -2f));
            Invisible("Front Edge Island", geo, new Vector3(34f, 4f, -1.9f), new Vector3(40f, 7f, -1.5f));
            // At the island's left edge, so B can't walk off the stairs' right end into the strip in front of the cliff.
            Invisible("Under Island", geo, new Vector3(34f, 0f, -2f), new Vector3(34.4f, 3f, 3f));

            var puzzles = new GameObject("Puzzles").transform;
            Place("GapSocket", puzzles, new Vector3(12.3f, 0f, 0f)); // its Hint says "Too far to jump!"
            Place("Checkpoint", puzzles, new Vector3(17f, 0f, 0.5f)).name = "Checkpoint 1";
            // Two puzzles, taken in either order, both leading up to the island. The high ledge: its hint and build slot
            // cover only the approach to its left face (x 18..24), not its length.
            AddHint(puzzles, "Hint High Ledge", new Vector3(19.5f, 0f, -2f), new Vector3(24f, 2f, 3f), "Too high to climb!");
            // A build made on the floor in front of the ledge lands as a step against its left face, at the ledge's
            // depth (the ghost shows the box). 3 x 1.2 m keeps almost any fitted drawing a 0.7-1.6 m step: jump on, jump off.
            AddBuildSlot(puzzles, "Build Slot High Ledge", new Vector3(18f, 0f, -2f), new Vector3(24f, 1f, 3f),
                new Vector3(24f - BuildSlot.DefaultBox.x * 0.5f, 0f, 1.75f), 2.5f);
            // The gatekeeper, on the front path beside the gate at the foot of the island stairs (gate x 30.6..31,
            // z -2..0.5). Anything edible opens it. Their 2.5 m BUILD range keeps the high ledge's builds out of it.
            Place("GatekeeperSocket", puzzles, new Vector3(29.6f, 0f, -1f));
            // Centred on the island's depth (z -1.5..3), so B touches the 3.2 m-deep trigger wherever B crosses it.
            Place("Exit", puzzles, new Vector3(38f, 4f, 0.75f));
            GoalFlag(puzzles, new Vector3(39.3f, 4f, 1.8f));

            // Start back from the gap, near the left end, so B walks up to it (BUILD reaches the gap from x 7.3).
            Rig(new Vector3(0.5f, 0f, 0f));
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

        /// <summary>
        /// A <see cref="BuildSlot"/> (box <see cref="BuildSlot.DefaultBox"/>, pushed right) standing on
        /// <paramref name="slotBottom"/>, <paramref name="depth"/> deep, used while B's on the floor within
        /// <paramref name="min"/>..<paramref name="max"/>.
        /// </summary>
        static void AddBuildSlot(Transform parent, string name, Vector3 min, Vector3 max, Vector3 slotBottom, float depth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = (min + max) * 0.5f;
            var area = go.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.size = max - min;
            GameAssetBuilder.Fields(go.AddComponent<BuildSlot>(), ("bottomCentre", slotBottom), ("box", BuildSlot.DefaultBox),
                ("depth", depth), ("align", 1f));
        }

        /// <summary>Goal flag on the island: looks only, no colliders (the Exit trigger next to it wins).</summary>
        static void GoalFlag(Transform parent, Vector3 foot)
        {
            var flag = new GameObject("Goal Flag").transform;
            flag.SetParent(parent, false);
            flag.position = foot;
            Prop(PrimitiveType.Cylinder, "Pole", flag, new Vector3(0f, 1.5f, 0f), new Vector3(0.12f, 1.5f, 0.12f), "Wood");
            Prop(PrimitiveType.Cube, "Cloth", flag, new Vector3(0.6f, 2.6f, 0f), new Vector3(1.1f, 0.7f, 0.05f), "Exit");
            Prop(PrimitiveType.Sphere, "Knob", flag, new Vector3(0f, 3.05f, 0f), new Vector3(0.25f, 0.25f, 0.25f), "Exit");
        }

        /// <summary>
        /// Two rows of flat, unshaded hills far behind the back wall (looks only), wide enough to fill the Wide framing
        /// anywhere along the level. Each hill is a squashed sphere sunk well below the floor, so only its cap shows.
        /// </summary>
        static void Hills()
        {
            var backdrop = new GameObject("Backdrop").transform;
            float[] farTops = { 5.5f, 4.2f, 6.2f, 4.8f, 5.8f, 4.5f, 6f };
            for (var i = 0; i < farTops.Length; i++)
                Hill(backdrop, "HillFar", new Vector3(-45f + i * 22f, 0f, 48f), 34f, farTops[i]);
            float[] nearTops = { 2f, 3.2f, 1.2f, 3.6f, 2.4f, 1.8f, 3f, 2.2f };
            for (var i = 0; i < nearTops.Length; i++)
                Hill(backdrop, "HillNear", new Vector3(-30f + i * 16f, 0f, 26f), 22f + (i % 3) * 3f, nearTops[i]);
        }

        static void Hill(Transform parent, string material, Vector3 foot, float width, float top)
        {
            const float sunk = 8f; // the sphere's centre sits this far below y 0
            var hill = Prop(PrimitiveType.Sphere, "Hill", parent, new Vector3(foot.x, -sunk, foot.z),
                new Vector3(width, 2f * (top + sunk), 4f), material);
            var renderer = hill.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>A block you can walk on, with a grass top. Returns the block.</summary>
        static GameObject Walkable(string prefab, Transform parent, string name, Vector3 min, Vector3 max)
        {
            var block = Box(prefab, parent, min, max);
            block.name = name;
            // Slightly oversized, look-only slab, so its lip shows on the front face and the block's top never z-fights.
            const float lip = 0.18f, overhang = 0.03f;
            var lo = new Vector3(min.x - overhang, max.y - lip, min.z - overhang);
            var hi = new Vector3(max.x + overhang, max.y + 0.01f, max.z + overhang);
            var grass = Prop(PrimitiveType.Cube, name + " Grass", parent, (lo + hi) * 0.5f, hi - lo, "Grass");
            grass.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return block;
        }

        static GameObject Prop(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, string material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = GameAssetBuilder.Mat(material);
            return go;
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
