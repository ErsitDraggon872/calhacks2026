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
        public const string TextureDir = "Assets/Prefabs/Textures";
        public const string ConfigPath = "Assets/Resources/GameConfig.asset";

        public static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        [MenuItem("Game/Build Assets (materials, config, prefabs)")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(TextureDir);
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
            // World palette: saturated mid-tones, so black ink on white paper (the drawings, B) is the only near-black
            // and near-white on screen. Orange (DrawHere) and gold (Exit) are kept for things you interact with.
            Lit("Ground", new Color(0.60f, 0.40f, 0.26f));    // earth: sides of walkable blocks
            Lit("Grass", new Color(0.42f, 0.70f, 0.31f));     // walkable tops
            Lit("Wall", new Color(0.36f, 0.55f, 0.58f));      // dusty teal backdrop and end walls: the orange outline pops on it
            Lit("Rock", new Color(0.60f, 0.46f, 0.66f));      // plum: the high ledge
            Lit("Sandstone", new Color(0.85f, 0.62f, 0.36f)); // the floating island
            Lit("Wood", new Color(0.47f, 0.31f, 0.20f));      // poles
            Lit("Metal", new Color(0.55f, 0.57f, 0.60f));     // the launchpad and its gantry
            Lit("Player", new Color(0.25f, 0.5f, 0.92f));
            Lit("Villager", new Color(0.92f, 0.55f, 0.25f));
            Lit("Flag", new Color(0.85f, 0.30f, 0.27f));      // checkpoint flag; Checkpoint turns it green when reached
            Lit("Exit", new Color(1f, 0.8f, 0.2f));
            Lit("Paper", new Color(0.98f, 0.96f, 0.93f));

            // Flat, unshaded cardboard hills behind the level; the far row is paler and bluer (aerial perspective).
            Flat("HillNear", new Color(0.45f, 0.66f, 0.45f));
            Flat("HillFar", new Color(0.60f, 0.75f, 0.74f));

            AlphaClip("Cutout", null, Color.white);

            // Player B is a paper cutout like the drawings: a character drawn in code, run through MakePaper.
            var ink = MockDrawingGenerator.Character();
            var paper = PaperTexture.MakePaper(ink);
            Object.DestroyImmediate(ink);
            AlphaClip("PlayerPaper", SaveTexture(paper, "PlayerPaper"), Color.white);

            // The gatekeeper is drawn the same way, in the old capsule villager's colours.
            var guardInk = MockDrawingGenerator.Guard();
            var guardPaper = PaperTexture.MakePaper(guardInk);
            Object.DestroyImmediate(guardInk);
            AlphaClip("GuardPaper", SaveTexture(guardPaper, "GuardPaper"), Color.white);

            // Dashed "draw here" outline at the gap, sized like GapSocket's DrawHere quad (4.6 x 2.2 m).
            // The same outline for the build ghost, sized like a build slot (BuildSlot.DefaultBox) at the same pixels per
            // metre, so the dashes match.
            var ghostPixels = BuildSlot.DefaultBox * (1046f / 4.6f);
            AlphaClip("BuildGhost", SaveTexture(MockDrawingGenerator.DrawHereOutline(Mathf.RoundToInt(ghostPixels.x),
                Mathf.RoundToInt(ghostPixels.y)), "BuildGhost"),
                new Color(1f, 0.62f, 0.15f));
            AlphaClip("DrawHere", SaveTexture(MockDrawingGenerator.DrawHereOutline(1046, 500), "DrawHere"),
                new Color(1f, 0.62f, 0.15f));

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

        /// <summary>Unlit, double-sided, alpha-clipped: how every paper cutout is drawn.</summary>
        static Material AlphaClip(string name, Texture2D texture, Color color)
        {
            var m = Load(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            if (texture != null)
            {
                m.SetTexture("_BaseMap", texture);
                m.SetTexture("_MainTex", texture);
            }
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Writes <paramref name="texture"/> as a PNG under <see cref="TextureDir"/> (GUID kept on re-runs), destroys it and returns the imported asset.</summary>
        static Texture2D SaveTexture(Texture2D texture, string name)
        {
            var path = $"{TextureDir}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Lit(string name, Color color)
        {
            var m = Load(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
        }

        /// <summary>Unlit and opaque: a flat colour with no shading, for backdrop scenery.</summary>
        static void Flat(string name, Color color)
        {
            var m = Load(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
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
            Save(BuildPlayer(config), "Player");
            Save(BuildCameraRig(), "CameraRig");
            Save(BuildGame(config), "Game");
            Save(BuildGapSocket(), "GapSocket");
            Save(BuildCaveSocket(), "CaveSocket");
            Save(BuildGatekeeperSocket(), "GatekeeperSocket");
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

        static GameObject BuildPlayer(GameConfig config)
        {
            var root = new GameObject("Player") { tag = "Player" };
            var cc = root.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 1f, 0f);
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.stepOffset = 0.3f; // climbs the 0.15 m stair-steps of a drawn silhouette
            cc.skinWidth = 0.05f;
            var controller = root.AddComponent<PlayerController>();

            // Flat, camera-facing paper (the side camera's yaw is 0, so an unrotated quad faces it). "Visual" is what
            // PlayerController flips to face left or right; the quad sits 5 cm toward the camera so B draws over an
            // invention standing at the same depth.
            var visual = Empty("Visual", root.transform, Vector3.zero);
            var material = Mat("PlayerPaper");
            var texture = material != null ? material.GetTexture("_BaseMap") : null;
            const float height = 1.9f;
            var width = texture != null && texture.height > 0 ? height * texture.width / texture.height : 0.9f;
            // The CharacterController rests skinWidth above the ground, and the paper has a margin under the drawn feet:
            // drop the quad by both so the feet touch the surface (the paper under them dips into it).
            var drop = cc.skinWidth + FeetMargin(MockDrawingGenerator.Character()) * height;
            var quad = Visual(PrimitiveType.Quad, "Paper", visual.transform, new Vector3(0f, height * 0.5f - drop, -0.05f),
                new Vector3(width, height, 1f), "PlayerPaper");
            quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;

            // Build ghost: outside Visual, so it doesn't flip; BuildGhost places and sizes it every frame.
            var ghost = root.AddComponent<BuildGhost>();
            var outline = Visual(PrimitiveType.Quad, "BuildGhost", root.transform, Vector3.zero, Vector3.one, "BuildGhost");
            outline.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            Fields(controller, ("config", config), ("visual", visual.transform));
            Fields(ghost, ("outline", outline.transform));
            return root;
        }

        /// <summary>Fraction of the paper made from <paramref name="ink"/> (a character drawing, which this destroys) below the drawn feet.</summary>
        static float FeetMargin(Texture2D ink)
        {
            var paper = PaperTexture.MakePaper(ink);
            var row = PaperTexture.LowestInkRow(paper.GetPixels32(), paper.width, paper.height);
            var margin = row > 0 ? (float)row / paper.height : 0f;
            Object.DestroyImmediate(ink);
            Object.DestroyImmediate(paper);
            return margin;
        }

        static GameObject BuildCameraRig()
        {
            var root = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = root.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.56f, 0.79f, 0.95f); // sky
            root.AddComponent<AudioListener>();
            root.AddComponent<CameraFollow>();
            return root;
        }

        static GameObject BuildGame(GameConfig config)
        {
            var root = new GameObject("Game");
            var run = root.AddComponent<RunManager>();
            root.AddComponent<Subtitles>();
            root.AddComponent<Hud>();
            root.AddComponent<DebugOverlay>();
            Fields(root.AddComponent<BoardCamera>(), ("config", config));
            Fields(root.AddComponent<CameraPreview>(), ("config", config));
            Fields(run, ("config", config), ("inventionMaterial", Mat("Cutout")));
            return root;
        }

        static GameObject BuildGapSocket()
        {
            var root = new GameObject("GapSocket");
            var socket = root.AddComponent<GapSocket>();
            // The bridge is an invention built from the drawing (no hidden deck): one face at ledge level, in the middle
            // of its 3.2 m depth, its colliders reaching the rails' inner faces (z -1.6..1.6).
            var anchor = Empty("Anchor", root.transform, Vector3.zero);
            var rails = Empty("Rails", root.transform, Vector3.zero);
            foreach (var z in new[] { -1.75f, 1.75f })
            {
                var rail = rails.AddComponent<BoxCollider>();
                rail.center = new Vector3(0f, 0.6f, z);
                rail.size = new Vector3(5.2f, 1.2f, 0.3f);
            }
            // Where the drawing will appear: 4.6 m wide (the gap), top 0.4 m above the ledges (GapSocket.railHeight).
            var drawHint = Visual(PrimitiveType.Quad, "DrawHere", root.transform, new Vector3(0f, 0.4f - 1.1f, 0f),
                new Vector3(4.6f, 2.2f, 1f), "DrawHere");
            drawHint.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Hint(root.transform, new Vector3(-3.6f, 1f, 0f), new Vector3(2f, 2f, 3.2f), "Too far to jump!", socket);
            Fields(socket,
                ("socketId", "gap"),
                ("acceptedLabels", new string[0]),
                ("anchor", anchor.transform),
                ("span", new Vector2(4.9f, 2.2f)),
                ("faceCamera", false),
                ("rails", rails),
                ("drawHint", drawHint),
                ("depth", 3.2f),
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

        /// <summary>A paper guard cutout like B (the GuardPaper drawing), standing on <paramref name="localPosition"/>.</summary>
        static GameObject Villager(Transform parent, Vector3 localPosition)
        {
            var villager = Empty("Villager", parent, localPosition);
            // Taller than they look (gate height and more), so B can't jump up onto them or over them.
            var capsule = villager.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1.75f, 0f);
            capsule.radius = 0.6f;
            capsule.height = 3.5f;
            // A head taller than B; feet on the ground the way BuildPlayer does it.
            var texture = Mat("GuardPaper").GetTexture("_BaseMap");
            const float height = 2.2f;
            var width = height * texture.width / texture.height;
            var drop = FeetMargin(MockDrawingGenerator.Guard()) * height;
            var quad = Visual(PrimitiveType.Quad, "Paper", villager.transform, new Vector3(0f, height * 0.5f - drop, 0f),
                new Vector3(width, height, 1f), "GuardPaper");
            quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            return villager;
        }

        /// <summary>
        /// Side-view gatekeeper beside the gate at the foot of the island stairs (SceneBuilder builds the stairs). The gate
        /// closes the left side of the two lowest steps (2.5 m of depth from the front edge, 3 m high): B's capsule rides
        /// up edges to about 1.65 m, so the 1.6 m second step is in reach from the floor too; the higher ones aren't. Anything edible feeds them (anything hostile scares them): they step back and the
        /// gate rises. Their BUILD range is short, so builds near the high ledge aren't taken for food.
        /// </summary>
        static GameObject BuildGatekeeperSocket()
        {
            var root = new GameObject("GatekeeperSocket");
            var socket = root.AddComponent<VillagerSocket>();
            var villager = Villager(root.transform, new Vector3(0f, 0f, 0.2f));
            var aside = Empty("StepAside", root.transform, new Vector3(-0.8f, 0f, 0.8f));
            var anchor = Empty("Anchor", root.transform, new Vector3(-1.3f, 0.2f, -0.6f));
            var gate = Block("Gate", "Wood");
            gate.transform.SetParent(root.transform, false);
            gate.transform.localPosition = new Vector3(1.2f, 1.5f, 0.25f);
            gate.transform.localScale = new Vector3(0.4f, 3f, 2.5f);
            // They speak in a bubble over their head while B is near, not in the subtitles or the top sign.
            var bubble = villager.AddComponent<SpeechBubble>();
            Fields(bubble, ("idleLine", "Nobody climbs my stairs! ...Unless they bring me a snack."), ("range", 4.5f),
                ("socket", socket));
            Fields(socket,
                ("socketId", "gatekeeper"),
                ("castRange", 2.5f),
                // No names: only what the AI says the drawing is (edible / hostile) opens the gate, so any food works
                // and a drawing merely called "food" doesn't.
                ("acceptedLabels", new string[0]),
                ("anchor", anchor.transform),
                ("span", new Vector2(1.6f, 1.4f)),
                ("fallbackDrawing", Drawing("fish")),
                ("cutoutMaterial", Mat("Cutout")),
                ("villager", villager.transform),
                ("stepAsidePoint", aside.transform),
                ("gate", gate.transform),
                ("gateRise", 3.5f),
                ("bubble", bubble),
                ("acceptLine", "Food! Thanks, friend. Up you go!"),
                ("scaredLine", "Yikes! Fine, fine, go on up! Just keep that thing away!"),
                ("rejectLine", "I can't eat that! I'm hungry, not desperate."));
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
            Visual(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 1f, 1.8f), new Vector3(0.1f, 1f, 0.1f), "Wood");
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
