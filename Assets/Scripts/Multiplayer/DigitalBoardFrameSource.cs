using System.Collections.Generic;
using UnityEngine;
using InventionRow = SpacetimeDB.Types.Invention;

namespace CalHacks
{
    /// <summary>
    /// The SpacetimeDB browser whiteboard (tools/whiteboard/index.html) as this machine's board, for
    /// <see cref="CaptureSource.DigitalBoard"/>. The page is the room's Creator and this game joins as the Explorer.
    /// Strokes are kept here per capture_id and drawn onto a clean board for <see cref="WebcamVisionClient"/>, so a
    /// BUILD runs the usual CAST: ink cutout, /interpret, sockets. The page's BUILD publishes the drawing as a
    /// "pending" invention; that row is what triggers CAST here.
    ///
    /// This keeps its own copy of the strokes, because the page clears the board right after its BUILD and the clear
    /// can arrive before CAST has grabbed the frame.
    /// </summary>
    public class DigitalBoardFrameSource : MonoBehaviour, IBoardFrameSource
    {
        [Tooltip("How long a BUILD from the page waits for CAST to be free (cooldown, a reading in flight).")]
        [SerializeField] float castWaitSeconds = 5f;

        readonly StrokeBoard board = new StrokeBoard();
        readonly HashSet<string> seenBuilds = new HashSet<string>();
        ThinkInkSession session;
        ulong roomId;
        /// <summary>The capture the page's BUILD asked for; CAST grabs it rather than the latest strokes.</summary>
        string pinned;
        string pinnedImage;
        float castDeadline;
        bool castWaiting;
        string lastGrabbed;

        public string DeviceName => "digital board " + (ThinkInkSession.InRoom ? ThinkInkSession.Instance.Room.RoomCode : "-");

        public string Status
        {
            get
            {
                var s = ThinkInkSession.Instance;
                if (s == null) return "no co-op session (the Coop prefab is missing from the scene)";
                if (s.Link != CoopLink.Online)
                    return "SpacetimeDB " + s.Link.ToString().ToLowerInvariant() + (s.LastError != null ? ": " + s.LastError : "");
                if (s.KeyState != SyncKeyState.Accepted) return "sync key " + s.KeyState.ToString().ToLowerInvariant();
                if (s.Room == null) return "not in a room: open the whiteboard from the co-op panel (F4)";
                if (!s.RoomReady) return "loading room " + s.Room.RoomCode;
                return "ready";
            }
        }

        public bool HasFreshFrame => ThinkInkSession.InRoom && ThinkInkSession.Instance.RoomReady;

        public Color32[] Grab(out int width, out int height)
        {
            width = height = 0;
            if (!HasFreshFrame) return null;
            var capture = pinned ?? board.Latest;
            lastGrabbed = capture;
            if (capture != null && capture == pinned && !board.Has(capture) && !string.IsNullOrEmpty(pinnedImage))
            {
                // The strokes never reached us (joined mid-drawing): use the cutout the page sent with its BUILD.
                var cutout = PaperTexture.FromBase64(pinnedImage);
                var frame = StrokeBoard.RenderCutout(cutout, out width, out height);
                if (cutout != null) PaperTexture.DestroySafe(cutout);
                return frame;
            }
            return board.Render(capture, out width, out height);
        }

        void OnEnable() => RunManager.RunReset += OnRunReset;

        void OnDisable()
        {
            RunManager.RunReset -= OnRunReset;
            Unhook();
        }

        void Update()
        {
            // The session may come up after this component; hook once it exists.
            if (session == null && ThinkInkSession.Instance != null)
            {
                session = ThinkInkSession.Instance;
                session.StrokeAdded += OnStroke;
                session.DrawingCleared += OnCleared;
                session.InventionChanged += OnInvention;
                session.RoomChanged += OnRoomChanged;
                OnRoomChanged();
            }
            if (castWaiting) TryCast();
        }

        void Unhook()
        {
            if (session == null) return;
            session.StrokeAdded -= OnStroke;
            session.DrawingCleared -= OnCleared;
            session.InventionChanged -= OnInvention;
            session.RoomChanged -= OnRoomChanged;
            session = null;
        }

        void OnRoomChanged()
        {
            var id = session.Room?.RoomId ?? 0;
            if (id == roomId) return;
            roomId = id;
            board.Clear();
            seenBuilds.Clear();
            Unpin();
        }

        void OnStroke(SpacetimeDB.Types.DrawingStroke stroke) => board.Add(stroke.CaptureId, stroke.Points);

        /// <summary>The page cleared its board (CLEAR, or right after BUILD): keep only a BUILD that's still waiting.</summary>
        void OnCleared() => board.ForgetAllBut(pinned);

        void OnInvention(InventionRow row)
        {
            if (row.Status != "pending" || !seenBuilds.Add(row.CaptureId)) return;
            // Rows that were already there when the room loaded are old BUILDs, not new ones.
            if (!session.RoomReady) return;
            pinned = row.CaptureId;
            pinnedImage = row.ImagePngBase64;
            castDeadline = Time.unscaledTime + castWaitSeconds;
            castWaiting = true;
            Debug.Log($"[DigitalBoard] BUILD from the whiteboard: capture {row.CaptureId}");
            TryCast();
        }

        void TryCast()
        {
            var run = RunManager.Instance;
            if (run == null) return;
            switch (run.State.CanCast())
            {
                case CastBlock.None:
                    castWaiting = false;
                    lastGrabbed = null;
                    run.Cast();
                    // CAST grabs the frame before its first await; once it has, this capture is done with.
                    if (lastGrabbed != null && lastGrabbed == pinned)
                    {
                        board.Forget(pinned);
                        Unpin();
                    }
                    return;
                case CastBlock.Finished:
                    Unpin();
                    return;
                default:
                    if (Time.unscaledTime < castDeadline) return;
                    Subtitles.ShowNow("Still busy with the last drawing. Press BUILD again.");
                    Unpin();
                    return;
            }
        }

        void Unpin()
        {
            pinned = null;
            pinnedImage = null;
            castWaiting = false;
        }

        void OnRunReset()
        {
            board.Clear();
            Unpin();
        }
    }
}
