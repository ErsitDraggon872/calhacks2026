using UnityEngine;

namespace CalHacks
{
    /// <summary>On-screen instructions for the other player after an AI invention spawns.</summary>
    public sealed class InventionGuide : MonoBehaviour
    {
        static InventionGuide instance;
        string title, description, action;
        float endsAt;
        bool visible;
        GUIStyle titleStyle, textStyle, actionStyle;

        public static void Show(InterpretResponse spec)
        {
            if (instance == null || spec == null) return;
            instance.title = "YOU MADE: " + (spec.name ?? "invention").ToUpperInvariant();
            instance.description = spec.message ?? "";
            bool button = false, bounce = false, flying = false, tethered = false;
            if (spec.components != null)
            foreach (var part in spec.components)
            {
                if (part == null) continue;
                if (part.activation == "button" || part.activation == "hold" || part.activation == "toggle") button = true;
                if (part.type == "bounce") bounce = true;
                if (part.type == "lift" || part.type == "glide") flying = true;
                if (part.type == "tether") tethered = true;
            }
            instance.action = button ? "GET CLOSE + PRESS E TO USE  |  SPACE TO JUMP OFF" :
                bounce ? "LAND ON IT TO BOUNCE" : flying ? "RIDE TO FLY OR GLIDE" :
                tethered ? "GET ON AND SWING" : "JUMP ON OR WALK ACROSS IT";
            if (spec.attributes != null && flying && spec.attributes.carry_capacity_kg < InventionPhysics.DemoPlayerMassKg)
                instance.action = "LOW CARRY CAPACITY: IT MAY FALL WITH YOU!";
            instance.endsAt = Time.unscaledTime + 12f;
            instance.visible = true;
        }

        public static void Close() { if (instance != null) instance.visible = false; }
        void Awake() { instance = this; }
        void OnDestroy() { if (instance == this) instance = null; }
        void OnGUI()
        {
            if (!visible) return;
            if (Time.unscaledTime > endsAt) { visible = false; return; }
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter,
                    wordWrap = true, normal = { textColor = Color.white } };
                actionStyle = new GUIStyle(textStyle) { fontSize = 16, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.86f, 0.42f) } };
            }
            GUI.depth = -170;
            float w = Mathf.Min(530f, Screen.width - 32f);
            var rect = new Rect((Screen.width - w) * 0.5f, Mathf.Max(60f, Screen.height * .12f), w, 172f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 12, rect.y + 8, w - 24, 34), title, titleStyle);
            GUI.Label(new Rect(rect.x + 16, rect.y + 40, w - 32, 51), description, textStyle);
            GUI.Label(new Rect(rect.x + 12, rect.y + 92, w - 24, 32), action, actionStyle);
            if (GUI.Button(new Rect(rect.x + w / 2 - 55, rect.y + 131, 110, 32), "GOT IT"))
                visible = false;
        }
    }
}
