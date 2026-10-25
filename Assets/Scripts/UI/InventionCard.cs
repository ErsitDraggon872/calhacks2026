using System.Linq;
using UnityEngine;

namespace CalHacks
{
    /// <summary>The traits a judge should see, in plain words: edible is "food", never "edible".</summary>
    public static class TraitWords
    {
        static readonly (Trait trait, string word)[] Shown =
        {
            (Trait.Edible, "food"), (Trait.Glows, "gives light"), (Trait.Hostile, "scary"), (Trait.Hot, "hot"),
            (Trait.Cold, "cold"), (Trait.Bouncy, "bouncy"), (Trait.Slippery, "slippery"), (Trait.Sticky, "sticky"),
        };

        public static string[] For(InventionSpec spec) =>
            spec == null ? new string[0] : Shown.Where(s => spec.Has(s.trait)).Select(s => s.word).ToArray();

        /// <summary>What B can do with a free build, from what it is.</summary>
        public static string FreeBuildAction(InventionSpec spec) =>
            spec.HasPart(PartType.Roll) ? "PRESS E TO DRIVE  ·  SPACE TO JUMP OFF"
            : spec.HasPart(PartType.Thrust) || spec.HasPart(PartType.Lift) || spec.HasPart(PartType.Glide) || spec.HasPart(PartType.Attach)
                ? "PRESS E TO RIDE  ·  SPACE TO JUMP OFF"
            : spec.Has(Trait.Bouncy) || spec.HasPart(PartType.Bounce) ? "IT'S BOUNCY: JUMP ON IT"
            : spec.Has(Trait.Slippery) ? "CAREFUL, IT'S SLIPPERY"
            : spec.Has(Trait.Sticky) ? "IT'S STICKY: NO SLIDING OFF"
            : spec.Has(Trait.Glows) ? "IT GIVES OFF LIGHT"
            : "JUMP ON IT OR WALK ACROSS";
    }

    /// <summary>The words on the card, worked out without any scene state so they can be tested.</summary>
    public readonly struct CardText
    {
        public const string Header = "✦ YOUR DRAWING CAME TO LIFE!";

        public readonly string Title, Body, Traits, Action;
        /// <summary>True: it worked here; false: it didn't; null: nothing to judge (the gap, a free build).</summary>
        public readonly bool? Worked;

        CardText(string title, string body, string traits, string action, bool? worked)
        {
            Title = title;
            Body = body;
            Traits = traits;
            Action = action;
            Worked = worked;
        }

        /// <param name="action">What it did or what to do with it, e.g. "FOOD → THE GATEKEEPER LETS YOU THROUGH".</param>
        /// <param name="body">Replaces the backend's message, e.g. the "doesn't work yet, so it's just solid" line.</param>
        public static CardText For(InventionSpec spec, string action, bool? worked, string body = null)
        {
            spec = spec ?? InventionSpec.Plain();
            var mystery = spec.Name == SocketLogic.Unknown;
            var title = mystery ? "MYSTERY SHAPE" : spec.Name.ToUpperInvariant();
            body = !string.IsNullOrWhiteSpace(body) ? body
                : mystery ? "Couldn't tell what that is, so it's just a solid shape."
                : spec.Message;
            return new CardText(title, body ?? "", string.Join("  ·  ", TraitWords.For(spec)), action ?? "", worked);
        }
    }

    /// <summary>
    /// "Your drawing came to life!": what the AI says the drawing is and what it did, shown after every BUILD. The game
    /// is paused (time scale 0) until LET'S PLAY or Space (Enter too: RunManager turns that BUILD into a dismiss), so what
    /// the drawing does (the gatekeeper eating it or cutting it up) plays after the card, not behind it.
    /// </summary>
    public sealed class InventionCard : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;

        static InventionCard instance;

        CardText card;
        bool visible;
        GUIStyle header, title, body, traits, action, button;
        Texture2D panel;

        public static bool Visible => instance != null && instance.visible;

        public static void Show(InventionSpec spec, string action, bool? worked, string body = null)
        {
            var text = CardText.For(spec, action, worked, body);
            Debug.Log($"[Card] {text.Title} | {text.Body} | {text.Traits} | {text.Action}");
            if (instance == null) return;
            instance.card = text;
            instance.visible = true;
            Time.timeScale = 0f;
        }

        public static void Hide()
        {
            if (instance != null) instance.Dismiss();
        }

        void Dismiss()
        {
            visible = false;
            Time.timeScale = 1f;
        }

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (visible) Time.timeScale = 1f;
            if (panel != null) Destroy(panel);
        }

        void OnGUI()
        {
            if (!visible) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
            {
                e.Use();
                Dismiss();
                return;
            }
            EnsureStyles();
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.depth = -100;
            var width = Screen.width / scale;
            var w = Mathf.Min(1100f, width - 60f);
            var x = (width - w) * 0.5f;
            var inner = w - 80f;

            var bodyHeight = string.IsNullOrEmpty(card.Body) ? 0f : body.CalcHeight(new GUIContent(card.Body), inner);
            var traitsHeight = string.IsNullOrEmpty(card.Traits) ? 0f : 44f;
            var actionHeight = string.IsNullOrEmpty(card.Action) ? 0f : action.CalcHeight(new GUIContent(card.Action), inner) + 12f;
            var h = 40f + 46f + 96f + bodyHeight + traitsHeight + actionHeight + 20f + 70f + 34f;
            var rect = new Rect(x, 150f, w, h);
            GUI.DrawTexture(rect, panel);

            var y = rect.y + 40f;
            GUI.Label(new Rect(x + 40f, y, inner, 46f), CardText.Header, header);
            y += 46f;
            GUI.Label(new Rect(x + 40f, y, inner, 96f), card.Title, title);
            y += 96f;
            if (bodyHeight > 0f)
            {
                GUI.Label(new Rect(x + 40f, y, inner, bodyHeight), card.Body, body);
                y += bodyHeight;
            }
            if (traitsHeight > 0f)
            {
                GUI.Label(new Rect(x + 40f, y + 4f, inner, 40f), card.Traits, traits);
                y += traitsHeight;
            }
            if (actionHeight > 0f)
            {
                action.normal.textColor = card.Worked == true ? new Color(0.55f, 0.95f, 0.6f)
                    : card.Worked == false ? new Color(1f, 0.62f, 0.5f)
                    : new Color(1f, 0.86f, 0.42f);
                Sketch.HoldColor(action);
                GUI.Label(new Rect(x + 40f, y + 12f, inner, actionHeight - 12f), card.Action, action);
                y += actionHeight;
            }
            y += 20f;
            if (GUI.Button(new Rect(x + (w - 300f) * 0.5f, y, 300f, 70f), "LET'S PLAY", button)) Dismiss();
            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (header != null) return;
            GUIStyle Centered(int size, FontStyle fontStyle, Color color) => Sketch.HoldColor(new GUIStyle(GUI.skin.label)
            {
                fontSize = size, fontStyle = fontStyle, alignment = TextAnchor.MiddleCenter, wordWrap = true,
                normal = { textColor = color },
            });
            header = Centered(32, FontStyle.Bold, new Color(0.6f, 0.95f, 0.85f));
            title = Centered(80, FontStyle.Bold, Color.white);
            body = Centered(34, FontStyle.Normal, new Color(0.9f, 0.92f, 0.95f));
            traits = Centered(30, FontStyle.Bold, new Color(0.6f, 0.95f, 0.85f));
            action = Centered(42, FontStyle.Bold, new Color(1f, 0.86f, 0.42f));
            button = new GUIStyle(GUI.skin.button) { fontSize = 32, fontStyle = FontStyle.Bold };
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0.27f, 0.32f, 0.39f, 0.94f));
            panel.Apply();
        }
    }
}
