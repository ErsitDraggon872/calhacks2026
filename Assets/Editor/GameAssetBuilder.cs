using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CalHacks.EditorTools
{
    /// <summary>
    /// Builds the materials, GameConfig and every gameplay prefab from code, so they can be regenerated after a bad edit.
    /// Re-running overwrites the prefabs in place (GUIDs are kept, so scenes keep their references).
    /// </summary>
    public static class GameAssetBuilder
    {
        public const string PrefabDir = "Assets/Prefabs";
        public const string MaterialDir = "Assets/Prefabs/Materials";
        public const string ConfigPath = "Assets/Resources/GameConfig.asset";

        public static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        [MenuItem("Game/Build Assets (materials, config, prefabs)")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(MaterialDir);
            MockDrawingGenerator.GenerateIfMissing();
            BuildMaterials();
            var config = BuildConfig();
            BuildPrefabs(config);
            AssetDatabase.SaveAssets();
            Debug.Log("[Builder] Materials, GameConfig and prefabs built.");
        }

        // ---- Materials ------------------------------------------------------------------------------------

        static void BuildMaterials()
        {
            Lit("Ground", new Color(0.93f, 0.89f, 0.80f));
            Lit("Wall", new Color(0.62f, 0.57f, 0.52f));
            Lit("Rock", new Color(0.33f, 0.31f, 0.33f));
            Lit("Player", new Color(0.25f, 0.5f, 0.92f));
            Lit("Villager", new Color(0.92f, 0.55f, 0.25f));
            Lit("Flag", new Color(0.85f, 0.85f, 0.85f));
            Lit("Exit", new Color(1f, 0.8f, 0.2f));
            Lit("Paper", new Color(0.98f, 0.96f, 0.93f));

            var cutout = Load("Cutout", "Universal Render Pipeline/Unlit");
            cutout.SetColor("_BaseColor", Color.white);
            cutout.SetFloat("_AlphaClip", 1f);
            cutout.SetFloat("_Cutoff", 0.5f);
            cutout.SetFloat("_Cull", (float)CullMode.Off);
            cutout.EnableKeyword("_ALPHATEST_ON");
            cutout.SetOverrideTag("RenderType", "TransparentCutout");
            cutout.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(cutout);

            var dark = Load("Darkness", "Universal Render Pipeline/Unlit");
            dark.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.05f, 0.94f));
            dark.SetFloat("_Surface", 1f);
            dark.SetFloat("_Blend", 0f);
            dark.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            dark.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            dark.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            dark.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            dark.SetFloat("_ZWrite", 0f);
            dark.SetFloat("_Cull", (float)CullMode.Back);
            dark.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dark.SetOverrideTag("RenderType", "Transparent");
            dark.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(dark);
        }

        static void Lit(string name, Color color)
        {
            var m = Load(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
        }

        static Material Load(string name, string shader)
        {
            var path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader.name != shader)
            {
                m.shader = Shader.Find(shader);
            }
            Materials[name] = m;
            return m;
        }

        public static Material Mat(string name)
        {
            if (!Materials.TryGetValue(name, out var m) || m == null)
            {
                m = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");
                Materials[name] = m;
            }
            return m;
        }

        // ---- Config -----------------------------------------------------------------------------------------

        static GameConfig BuildConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<GameConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        static Texture2D Drawing(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Resources/MockDrawings/{name}.png");

        // ---- Prefabs ----------------------------------------------------------------------------------------

        static void BuildPrefabs(GameConfig config)
        {
            Save(BuildPlayer(), "Player");
            Save(BuildCameraRig(), "CameraRig");
            Save(BuildGame(config), "Game");
            Save(BuildGapSocket(), "GapSocket");
            Save(BuildCaveSocket(), "CaveSocket");
            Save(BuildVillagerSocket(), "VillagerSocket");
            Save(BuildCheckpoint(), "Checkpoint");
            Save(BuildExit(), "Exit");
            Save(BuildHintZone(), "HintZone");
            Save(Block("Block", "Ground"), "Block");
            Save(Block("Wall", "Wall"), "Wall");
            Save(Block("Rock", "Rock"), "Rock");
        }

        static void Save(GameObject go, string name)
        {
            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        public static GameObject LoadPrefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");

        static GameObject BuildPlayer()
        {
            var root = new GameObject("Player") { tag = "Player" };
            var cc = root.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 1f, 0f);
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.05f;
            root.AddComponent<PlayerController>();
            Visual(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), "Player");
            Visual(PrimitiveType.Cube, "Nose", root.transform, new Vector3(0f, 1.4f, 0.4f), new Vector3(0.25f, 0.2f, 0.25f), "Flag");
            return root;
        }

        static GameObject BuildCameraRig()
        {
            var root = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = root.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.80f, 0.86f, 0.90f);
            root.AddComponent<AudioListener>();
            root.AddComponent<IsoCameraFollow>();
            return root;
        }

        static GameObject BuildGame(GameConfig config)
        {
            var root = new GameObject("Game");
            var run = root.AddComponent<RunManager>();
            root.AddComponent<Subtitles>();
            root.AddComponent<Hud>();
            root.AddComponent<DebugOverlay>();
            Fields(run, ("config", config));
            return root;
        }

        static GameObject BuildGapSocket()
        {
            var root = new GameObject("GapSocket");
            var socket = root.AddComponent<GapSocket>();
            // Far face of the bridge, at deck level; the near face mirrors it 3.2 m towards the camera.
            var anchor = Empty("Anchor", root.transform, new Vector3(0f, 0f, 1.6f));
            var deck = Empty("Deck", root.transform, Vector3.zero);
            var walkway = Visual(PrimitiveType.Cube, "Walkway", root.transform, new Vector3(0f, -0.05f, 0f), new Vector3(4.7f, 0.1f, 3.2f), "Paper");
            var box = deck.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.25f, 0f);
            box.size = new Vector3(5.2f, 0.5f, 3.2f);
            box.enabled = false;
            var rails = Empty("Rails", root.transform, Vector3.zero);
            foreach (var z in new[] { -1.75f, 1.75f })
            {
                var rail = rails.AddComponent<BoxCollider>();
                rail.center = new Vector3(0f, 0.6f, z);
                rail.size = new Vector3(5.2f, 1.2f, 0.3f);
            }
            Hint(root.transform, new Vector3(-3.6f, 1f, 0f), new Vector3(2f, 2f, 3.2f), "The gap is too wide to cross.", socket);
            Fields(socket,
                ("socketId", "gap"),
                ("acceptedLabels", new string[0]),
                ("anchor", anchor.transform),
                ("span", new Vector2(4.9f, 2.2f)),
                ("faceCamera", false),
                ("rails", rails),
                ("deckVisual", walkway),
                ("solutionCollider", box),
                ("fallbackDrawing", Drawing("bridge")),
                ("cutoutMaterial", Mat("Cutout")));
            return root;
        }

        static GameObject BuildCaveSocket()
        {
            var root = new GameObject("CaveSocket");
            var socket = root.AddComponent<CaveSocket>();
            var anchor = Empty("Anchor", root.transform, new Vector3(-0.5f, 1.2f, 1.2f));
            var darkness = Visual(PrimitiveType.Cube, "Darkness", root.transform, new Vector3(6.2f, 1.8f, 0f), new Vector3(12.4f, 3.6f, 4.6f), "Darkness");
            darkness.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var blockerGo = Empty("Blocker", root.transform, new Vector3(0.3f, 1f, 0f));
            var blocker = blockerGo.AddComponent<BoxCollider>();
            blocker.size = new Vector3(0.4f, 2f, 3.2f);
            Hint(root.transform, new Vector3(-1.8f, 1f, 0f), new Vector3(2.5f, 2f, 3.2f), "It's too dark to go in there.", socket);
            Fields(socket,
                ("socketId", "cave"),
                ("acceptedLabels", new[] { "light" }),
                ("anchor", anchor.transform),
                ("span", new Vector2(2.2f, 2.2f)),
                ("fallbackDrawing", Drawing("sun")),
                ("cutoutMaterial", Mat("Cutout")),
                ("darkness", darkness.GetComponent<MeshRenderer>()),
                ("blocker", blocker));
            return root;
        }

        static GameObject BuildVillagerSocket()
        {
            var root = new GameObject("VillagerSocket");
            var socket = root.AddComponent<VillagerSocket>();
            var villager = Empty("Villager", root.transform, Vector3.zero);
            var capsule = villager.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1f, 0f);
            capsule.radius = 0.6f;
            capsule.height = 2f;
            Visual(PrimitiveType.Capsule, "Body", villager.transform, new Vector3(0f, 1f, 0f), new Vector3(1.1f, 1f, 1.1f), "Villager");
            Visual(PrimitiveType.Cylinder, "Hat", villager.transform, new Vector3(0f, 2.1f, 0f), new Vector3(1.3f, 0.08f, 1.3f), "Wall");
            var aside = Empty("StepAside", root.transform, new Vector3(2.2f, 0f, 0.95f));
            var anchor = Empty("Anchor", root.transform, new Vector3(-1.4f, 0.3f, 1.1f));
            Hint(root.transform, new Vector3(-3f, 1f, 0f), new Vector3(2.5f, 2f, 2.4f),
                "Villager: I'm sooo hungry... I'd love something fresh from the river.", socket);
            Fields(socket,
                ("socketId", "villager"),
                ("acceptedLabels", new[] { "fish" }),
                ("anchor", anchor.transform),
                ("span", new Vector2(2.2f, 1.6f)),
                ("fallbackDrawing", Drawing("fish")),
                ("cutoutMaterial", Mat("Cutout")),
                ("villager", villager.transform),
                ("stepAsidePoint", aside.transform));
            return root;
        }

        static GameObject BuildCheckpoint()
        {
            var root = new GameObject("Checkpoint");
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(1f, 2f, 3.2f);
            var cp = root.AddComponent<Checkpoint>();
            Visual(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 1f, 1.8f), new Vector3(0.1f, 1f, 0.1f), "Wall");
            var flag = Visual(PrimitiveType.Cube, "Flag", root.transform, new Vector3(0.35f, 1.7f, 1.8f), new Vector3(0.6f, 0.4f, 0.05f), "Flag");
            Fields(cp, ("flag", flag.GetComponent<MeshRenderer>()));
            return root;
        }

        static GameObject BuildExit()
        {
            var root = new GameObject("Exit");
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(1.5f, 2f, 3.2f);
            root.AddComponent<ExitZone>();
            Visual(PrimitiveType.Cube, "PillarL", root.transform, new Vector3(0f, 1.25f, -1.8f), new Vector3(0.4f, 2.5f, 0.4f), "Exit");
            Visual(PrimitiveType.Cube, "PillarR", root.transform, new Vector3(0f, 1.25f, 1.8f), new Vector3(0.4f, 2.5f, 0.4f), "Exit");
            Visual(PrimitiveType.Cube, "Beam", root.transform, new Vector3(0f, 2.6f, 0f), new Vector3(0.5f, 0.3f, 4f), "Exit");
            return root;
        }

        static GameObject BuildHintZone()
        {
            var root = new GameObject("HintZone");
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2f, 2f, 3.2f);
            root.AddComponent<HintZone>();
            return root;
        }

        static GameObject Block(string name, string material, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(material);
            return go;
        }

        // ---- Helpers ----------------------------------------------------------------------------------------

        static HintZone Hint(Transform parent, Vector3 position, Vector3 size, string text, PuzzleSocket socket)
        {
            var go = Empty("Hint", parent, position);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var hint = go.AddComponent<HintZone>();
            Fields(hint, ("text", text), ("socket", socket));
            return hint;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go;
        }

        /// <summary>A primitive used only for looks: its collider is removed.</summary>
        static GameObject Visual(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, string material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(material);
            return go;
        }

        /// <summary>Sets serialized (often private) fields by name.</summary>
        public static void Fields(Object target, params (string name, object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                var p = so.FindProperty(name);
                if (p == null)
                {
                    Debug.LogError($"[Builder] {target.GetType().Name} has no serialized field '{name}'");
                    continue;
                }
                switch (value)
                {
                    case null: p.objectReferenceValue = null; break;
                    case Object o: p.objectReferenceValue = o; break;
                    case string s: p.stringValue = s; break;
                    case bool b: p.boolValue = b; break;
                    case int i: p.intValue = i; break;
                    case float f: p.floatValue = f; break;
                    case Vector2 v2: p.vector2Value = v2; break;
                    case Vector3 v3: p.vector3Value = v3; break;
                    case Color c: p.colorValue = c; break;
                    case string[] arr:
                        p.arraySize = arr.Length;
                        for (var k = 0; k < arr.Length; k++) p.GetArrayElementAtIndex(k).stringValue = arr[k];
                        break;
                    default: Debug.LogError($"[Builder] Unsupported field type for {name}: {value.GetType()}"); break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
