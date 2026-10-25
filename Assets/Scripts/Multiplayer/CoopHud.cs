using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Co-op panel, top right, drawn as a paper card (<see cref="Sketch"/>): connection and room code; the lobby (create
    /// or join) when not in a room; the live board (the drawing pad, or the webcam after M) taped to the card, with
    /// "bring to life" under it for the Explorer, or BUILD / clear for the Creator. F4 hides it.
    /// </summary>
    public class CoopHud : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        const float PanelWidth = 420f;
        const float Pad = 20f;
        const float PopupSeconds = 5f;

        GUIStyle small, text, title, link, button, cta, keyCap, placeholder, field, error;
        string code = "";
        bool hidden;
        float cardHeight;
        float previewHeight = (PanelWidth - Pad * 2f - 20f) * 9f / 16f + 12f;

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

            // The card is sized from what was drawn last pass; Layout runs before Repaint, so it never shows late.
            var card = new Rect(width - PanelWidth - 30f, 100f, PanelWidth, cardHeight);
            if (cardHeight > 0f) Sketch.Box(card, Sketch.Paper, 1, shadow: true);
            var x = card.x + Pad;
            var y = card.y + 12f;
            var w = PanelWidth - Pad * 2f;

            var connection = session.IsLocal ? "local server" : "key " + KeyText(session.KeyState);
            GUI.Label(new Rect(x, y, w, 26f), $"<color={LinkColor(session.Link)}>●</color> {session.Link.ToString().ToLowerInvariant()}  ·  {connection}", small);
            y += 30f;

            if (session.Room == null)
            {
                y = Lobby(session, x, y, w);
            }
            else
            {
                GUI.Label(new Rect(x, y, w, 44f), $"Room <b>{session.Room.RoomCode}</b>", title);
                if (Sketch.Link(new Rect(x + w - 90f, y, 90f, 44f), "leave", link)) session.LeaveRoom();
                y += 50f;
                y = session.Role == CoopRole.Creator ? Creator(x, y, w) : Explorer(x, y, w);
            }

            if (ShowError(session))
            {
                GUI.Label(new Rect(x, y, w, 56f), session.LastError, error);
                y += 60f;
            }
            cardHeight = y + 4f - card.y;
            if (session.Room != null && session.Role == CoopRole.Explorer) BringToLife(x, card.y + cardHeight + 16f, w);
            GUI.matrix = Matrix4x4.identity;
        }

        static bool ShowError(ThinkInkSession session) =>
            !string.IsNullOrEmpty(session.LastError) && Time.unscaledTime - session.LastErrorTime < 8f;

        float Lobby(ThinkInkSession session, float x, float y, float w)
        {
            var digital = RunManager.Instance != null && RunManager.Instance.UsesDigitalBoard;
            GUI.enabled = session.Link == CoopLink.Online;
            y += 6f;
            var createLabel = digital ? "create room + open drawing pad" : "create room (creator draws)";
            if (Sketch.Button(new Rect(x, y, w, 46f), createLabel, Sketch.Marker, 5, button))
            {
                if (digital) session.OpenTestWhiteboard();
                else session.CreateRoom();
            }
            y += 62f;
            var codeRect = new Rect(x, y, 120f, 46f);
            Sketch.Box(codeRect, Color.white, 6);
            var cursor = GUI.skin.settings.cursorColor;
            GUI.skin.settings.cursorColor = Sketch.Ink;
            code = GUI.TextField(codeRect, code, 4, field).ToUpperInvariant();
            GUI.skin.settings.cursorColor = cursor;
            if (Sketch.Button(new Rect(x + 136f, y, w - 136f, 46f), "join (explorer plays)", Sketch.Paper, 7, button)) session.JoinRoom(code);
            y += 62f;
            if (!digital && ThinkInkSession.TestWhiteboardPath != null)
            {
                var label = session.WaitingForWhiteboard ? "waiting for the browser tab..." : "open browser drawing pad";
                if (Sketch.Button(new Rect(x, y, w, 46f), label, Sketch.Paper, 8, button)) session.OpenTestWhiteboard();
                y += 62f;
            }
            GUI.enabled = true;
            return y;
        }

        float Creator(float x, float y, float w)
        {
            var build = CreatorBuild.Instance;
            if (build == null) return y;
            GUI.enabled = !build.Building;
            var label = build.Building ? "bringing to life..." : "bring to life!";
            if (Sketch.Button(new Rect(x, y, w - 100f, 48f), label, Sketch.Marker, 4, button)) build.Build();
            GUI.enabled = true;
            if (Sketch.Link(new Rect(x + w - 80f, y, 80f, 48f), "clear", link)) build.ClearDrawing();
            y += 60f;
            GUI.Label(new Rect(x, y, w, 26f), build.Status, small);
            y += 30f;
            return Preview(x, y, w, build.UsesWebcam ? "board, as your partner sees it" : "sent to your partner");
        }

        float Explorer(float x, float y, float w)
        {
            var arrival = ExplorerInventions.LastArrival;
            if (arrival != null && Time.unscaledTime - ExplorerInventions.LastArrivalTime < PopupSeconds)
            {
                GUI.Label(new Rect(x, y, w, 30f), $"new: <b>{(arrival.Name != "" ? arrival.Name : "drawing")}</b>", text);
                y += 34f;
            }
            return Preview(x, y, w, UsingWebcam ? "webcam, live" : "drawing pad, live");
        }

        /// <summary>The Explorer's BUILD, hanging under the card: what Enter does, as a button.</summary>
        void BringToLife(float x, float y, float w)
        {
            var run = RunManager.Instance;
            // Without the digital board a co-op Explorer's Cast does nothing (the Creator builds), so there's no button.
            if (run == null || !run.UsesDigitalBoard || run.State.Finished) return;
            var state = run.State;
            var ready = !state.InFlight && state.CooldownRemaining <= 0f;
            var label = state.InFlight ? "bringing to life..." : ready ? "bring to life!" : $"ready in {state.CooldownRemaining:0.0}s";
            var rect = new Rect(x + 6f, y, w - 12f, 64f);
            var hover = ready && rect.Contains(Event.current.mousePosition);
            Sketch.Tilted(rect.center, hover ? -0.8f : -2f, () =>
            {
                GUI.enabled = ready;
                var labelRect = ready ? new Rect(rect.x, rect.y, rect.width - 70f, rect.height) : rect;
                if (Sketch.Button(rect, "", ready ? Sketch.Marker : Sketch.Spent, 3, cta)) run.Cast();
                GUI.Label(labelRect, label, cta);
                GUI.enabled = true;
                if (!ready) return;
                var key = new Rect(rect.xMax - 78f, rect.center.y - 16f, 62f, 32f);
                Sketch.Box(key, Sketch.Paper, 9);
                GUI.Label(key, "enter", keyCap);
            });
        }

        static bool UsingWebcam => RunManager.Instance != null && RunManager.Instance.UsingWebcam;

        /// <summary>The live board: the webcam's board crop after M, else the browser whiteboard. Null if none yet.</summary>
        static Texture PreviewTexture(out Rect uv)
        {
            uv = new Rect(0f, 0f, 1f, 1f);
            if (!UsingWebcam) return WhiteboardPreview.Instance?.Texture;
            var cam = BoardCamera.Instance;
            if (cam == null || !cam.HasFreshFrame || cam.Preview == null) return null;
            uv = cam.Crop;
            return cam.Preview;
        }

        /// <summary>The board, taped slightly crooked to the card. Until there's an image it keeps its last size.</summary>
        float Preview(float x, float y, float w, string caption)
        {
            var texture = PreviewTexture(out var uv);
            var frame = new Rect(x + 4f, y + 10f, w - 8f, previewHeight);
            if (texture != null) frame.height = previewHeight = (frame.width - 12f) * Aspect(texture, uv) + 12f;
            Sketch.Tilted(frame.center, -1f, () =>
            {
                Sketch.Box(frame, Color.white, 2, shadow: true);
                var image = new Rect(frame.x + 6f, frame.y + 6f, frame.width - 12f, frame.height - 12f);
                if (texture != null) GUI.DrawTextureWithTexCoords(image, texture, uv);
                else GUI.Label(image, UsingWebcam ? "warming up the webcam..." : "waiting for the drawing pad...", placeholder);
                Sketch.Tape(new Vector2(frame.x + 14f, frame.y + 4f), -38f);
                Sketch.Tape(new Vector2(frame.xMax - 14f, frame.y + 4f), 38f);
            });
            y = frame.yMax + 8f;
            GUI.Label(new Rect(x + 4f, y, w, 26f), caption, small);
            return y + 30f;
        }

        static float Aspect(Texture texture, Rect uv) => texture.height * uv.height / (texture.width * uv.width);

        static string KeyText(SyncKeyState state) => state switch
        {
            SyncKeyState.Accepted => "<color=#3a9a4f>ok</color>",
            SyncKeyState.Sent => "<color=#b8901c>checking</color>",
            SyncKeyState.Rejected => "<color=#c0453a>wrong</color>",
            _ => "<color=#c0453a>missing</color>",
        };

        static string LinkColor(CoopLink link) => link switch
        {
            CoopLink.Online => "#3a9a4f",
            CoopLink.Connecting => "#b8901c",
            CoopLink.Reconnecting => "#c87a22",
            _ => "#c0453a",
        };

        void EnsureStyles()
        {
            if (text != null) return;
            small = Sketch.Text(19, Sketch.Muted);
            text = Sketch.Text(22, Sketch.Ink);
            title = Sketch.Text(34, Sketch.Ink, bold: true);
            link = Sketch.Text(20, Sketch.Muted, TextAnchor.MiddleRight);
            button = Sketch.Text(22, Sketch.Ink, TextAnchor.MiddleCenter, bold: true);
            cta = Sketch.Text(30, Sketch.Ink, TextAnchor.MiddleCenter, bold: true);
            keyCap = Sketch.Text(17, Sketch.Ink, TextAnchor.MiddleCenter);
            placeholder = Sketch.Text(19, Sketch.Muted, TextAnchor.MiddleCenter);
            error = Sketch.Text(18, new Color(0.75f, 0.22f, 0.15f));
            error.wordWrap = true;
            error.clipping = TextClipping.Clip;
            field = new GUIStyle(GUI.skin.textField)
            {
                font = Sketch.Hand, fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { background = null, textColor = Sketch.Ink }, hover = { background = null, textColor = Sketch.Ink },
                focused = { background = null, textColor = Sketch.Ink }, active = { background = null, textColor = Sketch.Ink },
            };
        }
    }
}
