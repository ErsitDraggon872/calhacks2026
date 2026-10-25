using UnityEngine;

namespace CalHacks
{
    /// <summary>Run timer, station instructions, BUILD cooldown bar and the win banner, as paper notes (<see cref="Sketch"/>).</summary>
    public class Hud : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        const string StationSign = "1. draw  ·  2. press enter to build";
        const string Reading = "reading your drawing...";
        static readonly Color ReadyFill = new Color(0.45f, 0.78f, 0.42f);

        GUIStyle timer, small, big, sign, keyLabel, tagLabel;

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

            var tag = new Rect(30f, 22f, 210f, run.InputName != null ? 88f : 58f);
            Sketch.Box(tag, Sketch.Paper, 11, shadow: true);
            Sketch.Label(new Rect(tag.x + 16f, tag.y + 4f, tag.width - 32f, 50f), FormatTime(run.State.Elapsed), timer);
            if (run.InputName != null)
                Sketch.Label(new Rect(tag.x + 16f, tag.y + 52f, tag.width - 32f, 28f), $"{run.InputName.ToLowerInvariant()}  ·  M", small);

            var state = run.State;
            // Near an obstacle the sign names it and what to draw, so it's clear where the drawing will go.
            var banner = run.NearestSocket?.Banner;
            var explorer = ThinkInkSession.InRoom && ThinkInkSession.Instance.Role == CoopRole.Explorer && !run.UsesDigitalBoard;
            // E and SPACE only show when they'd do something: E near a usable invention, SPACE while riding one.
            // On a ride, E is only the ride's own function (go / stop / hold), and the sign stays on the ride.
            var usable = run.Usable;
            var ride = run.Player != null ? run.Player.Riding : null;
            var riding = ride != null;
            var controls = riding
                ? (ride.RideAction != null ? ride.RideAction + "  ·  " : "") + "space jump off"
                : "draw → bring to life"
                    + (usable == null ? "" : usable.Rideable ? "  ·  E ride" : "  ·  E activate");
            var status = explorer ? "your partner draws  ·  you bring it to life"
                : state.Finished ? StationSign
                : run.PlacementStatus != null ? run.PlacementStatus
                : state.InFlight ? Reading
                : riding ? controls
                : !string.IsNullOrEmpty(banner) ? banner
                : controls;
            // The note fits its words (in 40 px steps, so a few sizes cover every sign).
            var signWidth = Mathf.Min(1100f, Mathf.Ceil((sign.CalcSize(new GUIContent(status)).x + 70f) / 40f) * 40f);
            var signRect = new Rect((width - signWidth) * 0.5f, 22f, signWidth, 54f);
            Sketch.Tilted(signRect.center, -0.6f, () =>
            {
                Sketch.Box(signRect, Sketch.Paper, 12, shadow: true);
                Sketch.Label(signRect, status, sign);
            });
            if (usable != null && !riding && !state.Finished) DrawKeyBadge(usable, scale);
            if (!explorer && !riding && !state.Finished && !InventionCard.Visible) DrawDestinationTag(run, scale);

            DrawBuildBar(state, width);

            if (state.Finished)
            {
                var rect = new Rect((width - 760f) * 0.5f, ReferenceHeight * 0.3f, 760f, 260f);
                Sketch.Tilted(rect.center, -1.5f, () =>
                {
                    Sketch.Box(rect, Sketch.Paper, 16, shadow: true);
                    Sketch.Tape(new Vector2(rect.center.x, rect.y + 2f), 3f, 120f, 26f);
                    Sketch.Label(new Rect(rect.x, rect.y + 24f, rect.width, 130f), "You made it!\n" + FormatTime(run.FinalTime), big);
                    Sketch.Label(new Rect(rect.x, rect.y + 178f, rect.width, 56f), "press F5 to play again", sign);
                });
            }
            GUI.matrix = Matrix4x4.identity;
        }

        /// <summary>An "E" key cap floating over the invention E would use.</summary>
        void DrawKeyBadge(InventionPhysics usable, float scale)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var bounds = usable.VisualBounds();
            var screen = cam.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y + 0.3f, bounds.center.z));
            if (screen.z <= 0f) return;
            var bob = Mathf.Sin(Time.time * 4f) * 6f;
            var centre = new Vector2(screen.x / scale, ReferenceHeight - screen.y / scale - 30f + bob);
            var rect = new Rect(centre.x - 28f, centre.y - 28f, 56f, 56f);
            Sketch.Box(rect, Sketch.Marker, 15, shadow: true);
            Sketch.Label(rect, "E", keyLabel);
        }

        /// <summary>
        /// A note over where the next drawing goes: the obstacle in range (the gap, the guard), or the build ghost in
        /// front of B. While the AI reads a free build it says so over the drawing's ghost.
        /// </summary>
        void DrawDestinationTag(RunManager run, float scale)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var socket = run.PendingBuildCount > 0 ? null : run.NearestSocket;
            Vector3 point;
            string text;
            if (socket != null)
            {
                point = socket.CastPoint + Vector3.up * 2.5f;
                text = socket.Type == SocketType.Gap ? "drawing goes here ↓  ·  a bridge?"
                    : socket.Type == SocketType.Villager ? "drawing goes here ↓  ·  food?"
                    : "drawing goes here ↓";
            }
            else if (BuildGhost.Showing is Bounds ghost)
            {
                point = new Vector3(ghost.center.x, ghost.max.y + 0.3f, ghost.center.z);
                text = run.Thinking ? "thinking..." : run.PendingBuildCount > 0 ? "needs room" : "your drawing appears here ↓";
            }
            else return;
            var screen = cam.WorldToScreenPoint(point);
            if (screen.z <= 0f) return;
            var bob = Mathf.Sin(Time.unscaledTime * 3f) * 5f;
            var w = Mathf.Ceil(tagLabel.CalcSize(new GUIContent(text)).x + 36f);
            var centre = new Vector2(screen.x / scale, ReferenceHeight - screen.y / scale - 24f + bob);
            var rect = new Rect(centre.x - w * 0.5f, centre.y - 20f, w, 40f);
            Sketch.Box(rect, Sketch.Paper, 16, shadow: true);
            Sketch.Label(rect, text, tagLabel);
        }

        void DrawBuildBar(RunState state, float width)
        {
            var rect = new Rect(width - 420f, ReferenceHeight - 104f, 390f, 76f);
            Sketch.Box(rect, Sketch.Paper, 13, shadow: true);
            var ready = state.CooldownRemaining <= 0f && !state.InFlight;
            var text = state.InFlight ? Reading
                : ready ? "ready to invent"
                : $"recharging {state.CooldownRemaining:0.0}s";
            Sketch.Label(new Rect(rect.x, rect.y + 4f, rect.width, 40f), text, sign);
            var bar = new Rect(rect.x + 22f, rect.y + 48f, rect.width - 44f, 14f);
            Sketch.Box(bar, Color.white, 14);
            var fill = state.InFlight ? 0f : state.CooldownFraction;
            var old = GUI.color;
            GUI.color = ready ? ReadyFill : Sketch.Marker;
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * fill, bar.height - 4f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        void EnsureStyles()
        {
            if (timer != null) return;
            timer = Sketch.Text(38, Sketch.Ink, bold: true);
            small = Sketch.Text(20, Sketch.Muted);
            sign = Sketch.Text(28, Sketch.Ink, TextAnchor.MiddleCenter);
            big = Sketch.Text(60, Sketch.Ink, TextAnchor.MiddleCenter, bold: true);
            keyLabel = Sketch.Text(34, Sketch.Ink, TextAnchor.MiddleCenter, bold: true);
            tagLabel = Sketch.Text(22, Sketch.Ink, TextAnchor.MiddleCenter, bold: true);
        }
    }
}
