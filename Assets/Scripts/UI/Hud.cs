using UnityEngine;

namespace CalHacks
{
    /// <summary>Run timer, station instructions, BUILD cooldown bar and the win banner.</summary>
    public class Hud : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        const string StationSign = "1. Draw  ·  2. Press Enter to BUILD";

        GUIStyle small, big, sign;
        Texture2D panel, barBack, barFill, barReady;

        public static string FormatTime(float seconds)
        {
            var minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.00}";
        }

        void OnGUI()
        {
            var run = RunManager.Instance;
            if (run == null) return;
            EnsureStyles();
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;

            GUI.Label(new Rect(30f, 20f, 400f, 60f), FormatTime(run.State.Elapsed), small);

            var signRect = new Rect((width - 1100f) * 0.5f, 18f, 1100f, 54f);
            GUI.DrawTexture(signRect, panel);
            var state = run.State;
            var status = state.Finished ? StationSign
                : state.InFlight ? "Looking at the board..."
                : StationSign;
            GUI.Label(signRect, status, sign);

            DrawBuildBar(state, width);

            if (state.Finished)
            {
                var rect = new Rect((width - 1000f) * 0.5f, ReferenceHeight * 0.3f, 1000f, 260f);
                GUI.DrawTexture(rect, panel);
                GUI.Label(new Rect(rect.x, rect.y + 20f, rect.width, 130f), "You made it!\n" + FormatTime(run.FinalTime), big);
                GUI.Label(new Rect(rect.x, rect.y + 180f, rect.width, 60f), "Press F5 to play again", sign);
            }
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawBuildBar(RunState state, float width)
        {
            var rect = new Rect(width - 420f, ReferenceHeight - 100f, 390f, 70f);
            GUI.DrawTexture(rect, panel);
            var bar = new Rect(rect.x + 15f, rect.y + 44f, rect.width - 30f, 14f);
            GUI.DrawTexture(bar, barBack);
            var ready = state.CooldownRemaining <= 0f && !state.InFlight;
            var fill = state.InFlight ? 0f : state.CooldownFraction;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fill, bar.height), ready ? barReady : barFill);
            var text = state.InFlight ? "Looking at the board..."
                : ready ? "BUILD READY"
                : $"Recharging {state.CooldownRemaining:0.0}s";
            GUI.Label(new Rect(rect.x, rect.y + 4f, rect.width, 40f), text, sign);
        }

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        void EnsureStyles()
        {
            if (small != null) return;
            small = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            sign = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            panel = Solid(new Color(0.1f, 0.08f, 0.06f, 0.65f));
            barBack = Solid(new Color(1f, 1f, 1f, 0.15f));
            barFill = Solid(new Color(1f, 0.6f, 0.1f, 0.9f));
            barReady = Solid(new Color(0.3f, 0.85f, 0.4f, 0.95f));
        }

        void OnDestroy()
        {
            foreach (var texture in new[] { panel, barBack, barFill, barReady })
            {
                if (texture != null) Destroy(texture);
            }
        }
    }
}
