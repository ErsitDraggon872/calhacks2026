using System.Collections.Generic;
using UnityEngine;
using InventionRow = SpacetimeDB.Types.Invention;

namespace CalHacks
{
    /// <summary>The browser board is this game's input. Each BUILD owns its frame until CAST settles.</summary>
    public class DigitalBoardFrameSource : MonoBehaviour, IBoardFrameSource
    {
        const int MaxQueuedBuilds = 8;
        sealed class Capture
        {
            public string Id;
            public Color32[] Pixels;
        }
        readonly StrokeBoard board = new StrokeBoard();
        readonly HashSet<string> seenBuilds = new HashSet<string>();
        readonly Queue<Capture> pending = new Queue<Capture>();
        ThinkInkSession session;
        ulong roomId;
        Capture active;
        bool processing;

        public string DeviceName => "digital board " + (ThinkInkSession.InRoom ? ThinkInkSession.Instance.Room.RoomCode : "-");
        public string Status
        {
            get
            {
                var s = ThinkInkSession.Instance;
                if (s == null) return "no co-op session (the Coop prefab is missing from the scene)";
                if (s.Link != CoopLink.Online)
                    return "SpacetimeDB " + s.Link.ToString().ToLowerInvariant() + (s.LastError != null ? ": " + s.LastError : "");
                if (s.KeyState == SyncKeyState.Rejected) return "sync key rejected: " + s.LastError;
                if (s.Room == null) return "not in a room: open the whiteboard from the co-op panel (F4)";
                return s.RoomReady ? "ready" : "loading room " + s.Room.RoomCode;
            }
        }
        public bool HasFreshFrame => ThinkInkSession.InRoom && ThinkInkSession.Instance.RoomReady;
        public Color32[] Grab(out int width, out int height)
        {
            width = height = 0;
            if (!HasFreshFrame) return null;
            if (active == null) return board.Render(board.Latest, out width, out height);
            width = StrokeBoard.Width;
            height = StrokeBoard.Height;
            return (Color32[])active.Pixels.Clone();
        }
        void OnEnable() => RunManager.RunReset += OnRunReset;
        void OnDisable()
        {
            RunManager.RunReset -= OnRunReset;
            ClearCaptures();
            Unhook();
        }
        void Update()
        {
            if (session == null && ThinkInkSession.Instance != null)
            {
                session = ThinkInkSession.Instance;
                session.StrokeAdded += OnStroke;
                session.DrawingCleared += OnCleared;
                session.InventionChanged += OnInvention;
                session.RoomChanged += OnRoomChanged;
                OnRoomChanged();
            }
            TryCast();
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
            RunManager.Instance?.CancelBoardBuild();
            roomId = id;
            ClearCaptures();
            seenBuilds.Clear();
        }
        void OnStroke(SpacetimeDB.Types.DrawingStroke stroke)
        {
            if (stroke.RoomId == roomId && !seenBuilds.Contains(stroke.CaptureId))
                board.Add(stroke.CaptureId, stroke.Points);
        }
        // BUILD snapshots are independent of the live board, so CLEAR cannot erase queued work.
        void OnCleared() => board.Clear();
        void OnInvention(InventionRow row)
        {
            if (row.RoomId != roomId || row.Status != "pending" || string.IsNullOrEmpty(row.CaptureId) ||
                !seenBuilds.Add(row.CaptureId)) return;
            // Historical rows are marked seen before subscription readiness, including their later updates.
            if (!session.RoomReady) return;
            if (pending.Count >= MaxQueuedBuilds)
            {
                Subtitles.ShowNow("Too many drawings waiting. Wait for BUILD to finish, then send again.");
                return;
            }
            Color32[] pixels;
            if (board.Has(row.CaptureId)) pixels = board.Render(row.CaptureId, out _, out _);
            else
            {
                var cutout = PaperTexture.FromBase64(row.ImagePngBase64);
                try { pixels = StrokeBoard.RenderCutout(cutout, out _, out _); }
                finally { if (cutout != null) PaperTexture.DestroySafe(cutout); }
            }
            pending.Enqueue(new Capture { Id = row.CaptureId, Pixels = pixels });
            board.Forget(row.CaptureId);
            Debug.Log("[DigitalBoard] Queued BUILD " + row.CaptureId);
        }
        async void TryCast()
        {
            var run = RunManager.Instance;
            if (processing || pending.Count == 0 || run == null || !HasFreshFrame) return;
            if (run.State.CanCast() == CastBlock.Finished) { ClearCaptures(); return; }
            if (run.State.CanCast() != CastBlock.None) return;
            if (run.PendingBuildCount > 0) return;
            processing = true;
            var capture = pending.Dequeue();
            active = capture;
            try { await run.CastAsync(); }
            finally
            {
                if (ReferenceEquals(active, capture)) active = null;
                processing = false;
            }
        }
        void ClearCaptures()
        {
            board.Clear();
            pending.Clear();
            active = null;
        }
        void OnRunReset() => ClearCaptures();
    }
}
