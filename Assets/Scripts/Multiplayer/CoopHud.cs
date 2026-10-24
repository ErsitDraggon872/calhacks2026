using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Co-op panel, top right: connection, room code, role, partner, Ink; the lobby (create or join) when not in a
    /// room; BUILD / CLEAR and the build status for the Creator; the live whiteboard and new-invention popup for the
    /// Explorer. F4 hides it.
    /// </summary>
    public class CoopHud : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        const float PanelWidth = 420f;
        const float PopupSeconds = 5f;

        GUIStyle text, title, button, field, error;
        Texture2D panel;
        string code = "";
        bool hidden;

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current?.f4Key.wasPressedThisFrame == true) hidden = !hidden;
        }

        void OnGUI()
        {
            var session = ThinkInkSession.Instance;
            if (session == null || hidden) return;
            EnsureStyles();
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;

            var x = width - PanelWidth - 30f;
            var y = 100f;
            var rect = new Rect(x, y, PanelWidth, PanelHeight(session));
            GUI.DrawTexture(rect, panel);
            x += 16f;
            y += 12f;
            var w = PanelWidth - 32f;

            var connection = session.IsLocal ? "Local drawing server" : "key " + KeyText(session.KeyState);
            GUI.Label(new Rect(x, y, w, 30f), $"<color={LinkColor(session.Link)}>●</color> {session.Link}  ·  {connection}", text);
            y += 32f;

            if (session.Room == null)
            {
                y = Lobby(session, x, y, w);
            }
            else
            {
                var partner = session.Role == CoopRole.Creator ? "Explorer" : "Creator";
                GUI.Label(new Rect(x, y, w, 40f), $"Room <b>{session.Room.RoomCode}</b>  ·  {session.Role}", title);
                if (GUI.Button(new Rect(x + w - 80f, y + 4f, 80f, 32f), "Leave", button)) session.LeaveRoom();
                y += 40f;
                GUI.Label(new Rect(x, y, w, 30f), $"{partner}: {(session.PartnerOnline ? "<color=#5d5>online</color>" : "<color=#d75>not here</color>")}", text);
                y += 30f;
                GUI.Label(new Rect(x, y, w, 30f), $"Ink: <b>{session.Room.InkRemaining}</b>", text);
                y += 36f;
                y = session.Role == CoopRole.Creator ? Creator(x, y, w) : Explorer(session, x, y, w);
            }

            if (ShowError(session))
                GUI.Label(new Rect(x, y, w, 60f), session.LastError, error);
            GUI.matrix = Matrix4x4.identity;
        }

        float PanelHeight(ThinkInkSession session)
        {
            var h = 60f + (ShowError(session) ? 60f : 0f);
            if (session.Room == null) return h + 150f + (ThinkInkSession.TestWhiteboardPath != null ? 54f : 0f);
            h += 110f;
            if (session.Role == CoopRole.Creator) h += 90f;
            else if (Time.unscaledTime - ExplorerInventions.LastArrivalTime < PopupSeconds) h += 70f;
            return h + PreviewHeight();
        }

        static bool ShowError(ThinkInkSession session) =>
            !string.IsNullOrEmpty(session.LastError) && Time.unscaledTime - session.LastErrorTime < 8f;

        float Lobby(ThinkInkSession session, float x, float y, float w)
        {
            var digital = RunManager.Instance != null && RunManager.Instance.UsesDigitalBoard;
            GUI.enabled = session.Link == CoopLink.Online;
            var createLabel = digital ? "Create room + open drawing pad" : "Create room (Creator: draws)";
            if (GUI.Button(new Rect(x, y, w, 44f), createLabel, button))
            {
                if (digital) session.OpenTestWhiteboard();
                else session.CreateRoom();
            }
            y += 54f;
            code = GUI.TextField(new Rect(x, y, 120f, 44f), code, 4, field).ToUpperInvariant();
            if (GUI.Button(new Rect(x + 130f, y, w - 130f, 44f), "Join (Explorer: plays)", button)) session.JoinRoom(code);
            y += 54f;
            if (!digital && ThinkInkSession.TestWhiteboardPath != null)
            {
                var label = session.WaitingForWhiteboard ? "Waiting for the browser tab..." : "Open browser drawing pad";
                if (GUI.Button(new Rect(x, y, w, 44f), label, button)) session.OpenTestWhiteboard();
                y += 54f;
            }
            GUI.enabled = true;
            return y;
        }

        float Creator(float x, float y, float w)
        {
            var build = CreatorBuild.Instance;
            if (build == null) return y;
            GUI.enabled = !build.Building;
            if (GUI.Button(new Rect(x, y, w * 0.5f - 5f, 44f), "BUILD (Enter)", button)) build.Build();
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + w * 0.5f + 5f, y, w * 0.5f - 5f, 44f), "CLEAR", button)) build.ClearDrawing();
            y += 50f;
            GUI.Label(new Rect(x, y, w, 30f), "BUILD: " + build.Status, text);
            y += 40f;
            return Preview(x, y, w, build.UsesWebcam ? "Board, as your partner sees it" : "Sent to your partner");
        }

        float Explorer(ThinkInkSession session, float x, float y, float w)
        {
            var arrival = ExplorerInventions.LastArrival;
            if (arrival != null && Time.unscaledTime - ExplorerInventions.LastArrivalTime < PopupSeconds)
            {
                GUI.Label(new Rect(x, y, w, 34f), $"New: <b>{(arrival.Name != "" ? arrival.Name : "drawing")}</b>", title);
                GUI.Label(new Rect(x, y + 34f, w, 34f), arrival.Message, text);
                y += 70f;
            }
            return Preview(x, y, w, "Creator's whiteboard (live)");
        }

        float PreviewHeight()
        {
            var texture = WhiteboardPreview.Instance?.Texture;
            return texture == null ? 0f : 34f + (PanelWidth - 32f) * texture.height / texture.width + 10f;
        }

        float Preview(float x, float y, float w, string caption)
        {
            var texture = WhiteboardPreview.Instance?.Texture;
            if (texture == null) return y;
            GUI.Label(new Rect(x, y, w, 30f), caption, text);
            y += 34f;
            var h = w * texture.height / texture.width;
            GUI.DrawTexture(new Rect(x, y, w, h), texture);
            return y + h + 10f;
        }

        static string KeyText(SyncKeyState state) => state switch
        {
            SyncKeyState.Accepted => "<color=#5d5>ok</color>",
            SyncKeyState.Sent => "<color=#dd5>checking</color>",
            SyncKeyState.Rejected => "<color=#d55>wrong</color>",
            _ => "<color=#d55>missing</color>",
        };

        static string LinkColor(CoopLink link) => link switch
        {
            CoopLink.Online => "#5d5",
            CoopLink.Connecting => "#dd5",
            CoopLink.Reconnecting => "#da5",
            _ => "#d55",
        };

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        void EnsureStyles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, wordWrap = true, normal = { textColor = Color.white } };
            title = new GUIStyle(text) { fontSize = 28 };
            error = new GUIStyle(text) { fontSize = 18, normal = { textColor = new Color(1f, 0.55f, 0.45f) } };
            button = new GUIStyle(GUI.skin.button) { fontSize = 20 };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            panel = Solid(new Color(0.1f, 0.08f, 0.06f, 0.75f));
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }
    }
}
