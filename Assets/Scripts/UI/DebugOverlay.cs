using System.Text;
using UnityEngine;

namespace CalHacks
{
    /// <summary>F3 overlay: provider, latency, run_id, lock state, health.</summary>
    public class DebugOverlay : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;

        GUIStyle style;
        Texture2D panel;
        readonly StringBuilder sb = new StringBuilder();

        void OnGUI()
        {
            var run = RunManager.Instance;
            if (run == null || !run.OverlayVisible) return;
            EnsureStyles();
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var s = run.State;
            sb.Clear();
            sb.AppendLine($"Vision client: {run.VisionName}");
            sb.AppendLine($"Provider: {run.LastProvider ?? "-"}   latency: {(run.LastLatencyMs >= 0 ? run.LastLatencyMs.ToString("0") + " ms" : "-")}");
            sb.AppendLine($"Last label: {run.LastLabel ?? "-"}   confidence: {(run.LastConfidence >= 0 ? run.LastConfidence.ToString("0.00") : "-")}");
            sb.AppendLine($"run_id: {s.RunId}   time: {Hud.FormatTime(s.Elapsed)}{(s.Finished ? " (finished)" : "")}");
            sb.AppendLine($"Build cooldown: {(s.CooldownRemaining > 0f ? s.CooldownRemaining.ToString("0.0") + " s" : "ready")}   in flight: {(s.InFlight ? "YES" : "no")}");
            if (BoardCamera.Instance != null) sb.AppendLine($"Camera: {BoardCamera.Instance.Status}   (P preview)");
            sb.AppendLine($"Bound socket: {run.BoundSocketId ?? "-"}   nearest in range: {run.NearestSocketId ?? "-"}");
            sb.AppendLine($"Solved: {run.SolvedSummary}   free builds: {run.InventionCount}");
            var h = run.LastHealth;
            sb.AppendLine(run.LastHealthError != null
                ? $"Health: ERROR {run.LastHealthError}"
                : h == null
                    ? "Health: (waiting)"
                    : $"Health: camera {(h.camera ? "ok" : "DOWN")}, calibrated {(h.calibrated ? "yes" : "NO")}, provider {h.provider} {(h.provider_reachable ? "reachable" : "UNREACHABLE")}");
            if (run.MockNext != null) sb.AppendLine($"Mock next: {run.MockNext}   (keys 1-5 pick, 0 = empty board)");
            if (!string.IsNullOrEmpty(run.LastError)) sb.AppendLine($"Last error: {run.LastError}");
            sb.Append("F1 accept · F2 reject · F3 overlay · F5 reset · F9 recalibrate");

            var content = new GUIContent(sb.ToString());
            var rect = new Rect(20f, 90f, 980f, style.CalcHeight(content, 940f) + 30f);
            GUI.DrawTexture(rect, panel);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 15f, rect.width - 40f, rect.height - 30f), content, style);
            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (style != null) return;
            style = new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = true, normal = { textColor = new Color(0.7f, 1f, 0.7f) } };
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.8f));
            panel.Apply();
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }
    }
}
