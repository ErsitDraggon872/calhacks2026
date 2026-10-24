using UnityEngine;

namespace CalHacks
{
    /// <summary>Run timer, station instructions, board-lock hint and the win banner.</summary>
    public class Hud : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        const string StationSign = "1. Draw  ·  2. Press CAST  ·  3. Wipe  ·  4. Press NEW BOARD";

        GUIStyle small, big, sign;
        Texture2D panel;

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
                : state.BoardLocked ? "Wipe the board, then press NEW BOARD"
                : StationSign;
            GUI.Label(signRect, status, sign);

            if (state.Finished)
            {
                var rect = new Rect((width - 1000f) * 0.5f, ReferenceHeight * 0.3f, 1000f, 260f);
                GUI.DrawTexture(rect, panel);
                GUI.Label(new Rect(rect.x, rect.y + 20f, rect.width, 130f), "You made it!\n" + FormatTime(run.FinalTime), big);
                GUI.Label(new Rect(rect.x, rect.y + 180f, rect.width, 60f), "Press F5 to play again", sign);
            }
            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (small != null) return;
            small = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            sign = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0.1f, 0.08f, 0.06f, 0.65f));
            panel.Apply();
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }
    }
}
