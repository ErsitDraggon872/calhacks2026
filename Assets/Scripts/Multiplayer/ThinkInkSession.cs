using System;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;
using InventionRow = SpacetimeDB.Types.Invention;

namespace CalHacks
{
    public enum CoopRole { None, Creator, Explorer }

    public enum CoopLink { Offline, Connecting, Online, Reconnecting }

    /// <summary>The shared sync key: none found, sent and waiting, accepted by the server, or rejected.</summary>
    public enum SyncKeyState { Missing, Sent, Accepted, Rejected }

    /// <summary>
    /// The SpacetimeDB connection for co-op: one Creator draws, one Explorer plays. Owns the room, the role and the
    /// room-scoped subscription, and turns table changes into plain C# events so drawing sources, the preview and the
    /// gameplay side never touch the SDK directly. Server: server/spacetimedb/src/index.ts (database "thinkink").
    /// </summary>
    public class ThinkInkSession : MonoBehaviour
    {
        public static ThinkInkSession Instance { get; private set; }

        /// <summary>True once this client is in a room. Local single-player flows (CAST from the local webcam) step aside.</summary>
        public static bool InRoom => Instance != null && Instance.Room != null;

        [SerializeField] string serverUri = "https://maincloud.spacetimedb.com";
        [SerializeField] string databaseName = "thinkink";
        [Tooltip("Each slot keeps its own identity, so two clients on one machine are two players. -thinkink-slot=B on the command line overrides it.")]
        [SerializeField] string identitySlot = "A";
        [SerializeField] string levelId = "level1";

        DbConnection conn;
        SubscriptionHandle roomSubscription;
        ulong subscribedRoomId;
        bool lobbySubscribed;

        public CoopLink Link { get; private set; } = CoopLink.Offline;
        public Identity? LocalIdentity { get; private set; }
        public CoopRole Role { get; private set; }
        public Room Room { get; private set; }
        public GameState GameState { get; private set; }
        /// <summary>The partner is connected, or (an HTTP-only client like the browser whiteboard) sent a heartbeat recently.</summary>
        public bool PartnerOnline
        {
            get
            {
                if (Room == null || conn == null || !partnerId.HasValue) return false;
                var p = conn.Db.Player.Identity.Find(partnerId.Value);
                if (p == null || p.RoomId != Room.RoomId) return false;
                var ageMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L - p.LastSeen.MicrosecondsSinceUnixEpoch;
                return p.Online || ageMicros < HeartbeatWindowSeconds * 1_000_000L;
            }
        }

        const long HeartbeatWindowSeconds = 10;
        Identity? partnerId;
        /// <summary>Invite tag of a test whiteboard this client opened; the room carrying it is joined automatically.</summary>
        string pendingInvite;
        /// <summary>True once the room's strokes, frame and inventions have arrived after joining or reconnecting.</summary>
        public bool RoomReady { get; private set; }
        public string LastError { get; private set; }
        public float LastErrorTime { get; private set; }
        public DbConnection Connection => conn;

        public event Action RoomChanged;
        public event Action<DrawingStroke> StrokeAdded;
        /// <summary>The drawing was cleared (CLEAR, a reset, or a new BUILD): drop strokes and the board frame.</summary>
        public event Action DrawingCleared;
        public event Action<BoardFrame> BoardFrameChanged;
        public event Action<InventionRow> InventionChanged;
        public event Action<InventionRow> InventionRemoved;
        /// <summary>The other player reset the level (reset_level). Not raised for this client's own resets.</summary>
        public event Action LevelResetByPartner;
        public event Action<string> Error;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
            // The partner's changes must keep arriving while this window isn't focused.
            Application.runInBackground = true;
            identitySlot = ArgValue("-thinkink-slot=") ?? identitySlot;
            serverUri = ArgValue("-thinkink-server=") ?? serverUri;
            databaseName = ArgValue("-thinkink-db=") ?? databaseName;
            syncKey = FindSyncKey();
        }

        // ---- Sync key ----------------------------------------------------------------------------------------
        // The server only lets devices that presented the shared key into rooms. The key is never in the repo:
        // -thinkink-key=..., else THINKINK_KEY, else a thinkink.key file in the project root (Editor) or next to
        // the game (builds).

        string syncKey;

        public SyncKeyState KeyState { get; private set; } = SyncKeyState.Missing;

        string FindSyncKey()
        {
            var key = ArgValue("-thinkink-key=") ?? Environment.GetEnvironmentVariable("THINKINK_KEY");
            if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
            var data = Application.dataPath;
            foreach (var dir in new[] { System.IO.Path.Combine(data, ".."), System.IO.Path.Combine(data, "..", "..") })
            {
                var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, "thinkink.key"));
                if (System.IO.File.Exists(file)) return System.IO.File.ReadAllText(file).Trim();
            }
            return null;
        }

        void SendSyncKey()
        {
            if (string.IsNullOrEmpty(syncKey))
            {
                KeyState = SyncKeyState.Missing;
                Fail("No sync key: add thinkink.key, or start with -thinkink-key=...");
                return;
            }
            KeyState = SyncKeyState.Sent;
            conn.Reducers.Authorize(syncKey);
        }

        void OnAuthorized(ReducerEventContext ctx, string _)
        {
            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    KeyState = SyncKeyState.Accepted;
                    break;
                case Status.Failed(var reason):
                    KeyState = SyncKeyState.Rejected;
                    Fail(reason);
                    break;
            }
        }

        void Start() => Connect();

        void Update() => conn?.FrameTick();

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            conn?.Disconnect();
        }

        string TokenKey => "ThinkInk.Token." + databaseName + "." + identitySlot;

        public void Connect()
        {
            if (conn != null) return;
            Link = CoopLink.Connecting;
            conn = DbConnection.Builder()
                .WithUri(serverUri)
                .WithDatabaseName(databaseName)
                .WithToken(PlayerPrefs.GetString(TokenKey, null))
                .WithAutomaticReconnect()
                .OnConnect(OnConnected)
                .OnConnectError((e, next) =>
                {
                    Fail("Can't reach SpacetimeDB: " + e.Message);
                    if (next == null)
                    {
                        Link = CoopLink.Offline;
                        conn = null;
                    }
                })
                .OnDisconnect((c, e, next) =>
                {
                    Link = next != null ? CoopLink.Reconnecting : CoopLink.Offline;
                    if (e != null) Debug.LogWarning("[Coop] Disconnected: " + e.Message);
                    if (next == null) conn = null;
                })
                .Build();
            RegisterCallbacks();
        }

        void OnConnected(DbConnection c, Identity identity, string token)
        {
            Link = CoopLink.Online;
            LocalIdentity = identity;
            PlayerPrefs.SetString(TokenKey, token);
            PlayerPrefs.Save();
            Debug.Log($"[Coop] Connected to {databaseName} as {identity} (slot {identitySlot}).");
            // Before anything else on this connection, so the room calls that follow are allowed.
            SendSyncKey();
            // Subscriptions survive automatic reconnects, so only the first connect makes them.
            if (lobbySubscribed) return;
            lobbySubscribed = true;
            c.SubscriptionBuilder()
                .OnApplied(_ => RefreshRoom())
                .OnError((_, e) => Fail("Subscription failed: " + e.Message))
                .Subscribe(new[] { "SELECT * FROM room", "SELECT * FROM player" });
        }

        void RegisterCallbacks()
        {
            var db = conn.Db;
            conn.Reducers.OnAuthorize += OnAuthorized;
            db.Player.OnInsert += (_, _) => RefreshRoom();
            db.Player.OnUpdate += (_, _, _) => RefreshRoom();
            db.Player.OnDelete += (_, _) => RefreshRoom();
            db.Room.OnInsert += (_, _) => RefreshRoom();
            db.Room.OnUpdate += (_, _, _) => RefreshRoom();

            db.DrawingStroke.OnInsert += (_, s) => { if (Mine(s.RoomId)) StrokeAdded?.Invoke(s); };
            db.DrawingStroke.OnDelete += (_, s) => { if (Mine(s.RoomId)) ClearedSoon(); };
            db.BoardFrame.OnInsert += (_, f) => { if (Mine(f.RoomId)) BoardFrameChanged?.Invoke(f); };
            db.BoardFrame.OnUpdate += (_, _, f) => { if (Mine(f.RoomId)) BoardFrameChanged?.Invoke(f); };
            db.BoardFrame.OnDelete += (_, f) => { if (Mine(f.RoomId)) ClearedSoon(); };

            db.Invention.OnInsert += (_, i) => { if (Mine(i.RoomId)) InventionChanged?.Invoke(i); };
            db.Invention.OnUpdate += (_, _, i) => { if (Mine(i.RoomId)) InventionChanged?.Invoke(i); };
            db.Invention.OnDelete += (_, i) => { if (Mine(i.RoomId)) InventionRemoved?.Invoke(i); };

            db.GameState.OnInsert += (_, g) => { if (Mine(g.RoomId)) GameState = g; };
            db.GameState.OnUpdate += (ctx, old, g) =>
            {
                if (!Mine(g.RoomId)) return;
                GameState = g;
                if (g.ResetCount != old.ResetCount && !CausedByMe(ctx)) LevelResetByPartner?.Invoke();
            };

            conn.OnUnhandledReducerError += (_, e) => Fail(e.Message);
        }

        bool Mine(ulong roomId) => Room != null && Room.RoomId == roomId;

        bool CausedByMe(EventContext ctx) =>
            ctx.Event is Event<Reducer>.Reducer r && LocalIdentity.HasValue && r.ReducerEvent.CallerIdentity == LocalIdentity.Value;

        // Deleting a drawing removes many rows in one transaction; announce it once, after the batch.
        bool clearPending;

        void ClearedSoon() => clearPending = true;

        void LateUpdate()
        {
            if (!clearPending) return;
            clearPending = false;
            DrawingCleared?.Invoke();
        }

        /// <summary>Reads this identity's player row: which room, which role, and whether the partner is connected.</summary>
        void RefreshRoom()
        {
            if (conn == null || !LocalIdentity.HasValue) return;
            var me = conn.Db.Player.Identity.Find(LocalIdentity.Value);
            var room = me != null ? conn.Db.Room.RoomId.Find(me.RoomId) : null;
            Role = room == null ? CoopRole.None : me.Role == "creator" ? CoopRole.Creator : CoopRole.Explorer;

            partnerId = room == null ? (Identity?)null : Role == CoopRole.Creator ? room.ExplorerId : room.CreatorId;
            if (room == null && pendingInvite != null) JoinInvitedRoom();

            var changedRoom = (room?.RoomId ?? 0) != (Room?.RoomId ?? 0);
            Room = room;
            if (changedRoom) SubscribeRoom(room);
            RoomChanged?.Invoke();
        }

        void SubscribeRoom(Room room)
        {
            var old = roomSubscription;
            roomSubscription = null;
            subscribedRoomId = 0;
            RoomReady = false;
            GameState = null;
            DrawingCleared?.Invoke();
            if (room != null)
            {
                var id = room.RoomId;
                subscribedRoomId = id;
                roomSubscription = conn.SubscriptionBuilder()
                    .OnApplied(_ =>
                    {
                        if (subscribedRoomId != id) return;
                        RoomReady = true;
                        Debug.Log($"[Coop] Room {room.RoomCode} loaded as {Role}.");
                    })
                    .OnError((_, e) => Fail("Room subscription failed: " + e.Message))
                    .Subscribe(new[]
                    {
                        $"SELECT * FROM drawing_stroke WHERE room_id = {id}",
                        $"SELECT * FROM board_frame WHERE room_id = {id}",
                        $"SELECT * FROM invention WHERE room_id = {id}",
                        $"SELECT * FROM game_state WHERE room_id = {id}",
                    });
            }
            // Subscribe to the new room before dropping the old one.
            if (old != null && old.IsActive) old.Unsubscribe();
        }

        // ---- Reducer calls. All are fire-and-forget; results arrive as table changes, failures through Error. ----

        public void CreateRoom() => Call(() => conn.Reducers.CreateRoom(levelId));

        public void JoinRoom(string code) => Call(() => conn.Reducers.JoinRoom(code ?? ""));

        public void LeaveRoom() => Call(() => conn.Reducers.LeaveRoom());

        /// <summary>
        /// Opens the browser test whiteboard (tools/whiteboard/index.html) as a second device. It creates a room as the
        /// Creator, tagged with an invite; this client sees the room appear and joins it as the Explorer.
        /// </summary>
        public void OpenTestWhiteboard()
        {
            var page = TestWhiteboardPath;
            if (page == null)
            {
                Fail("tools/whiteboard/index.html not found (the test whiteboard only runs from the project).");
                return;
            }
            pendingInvite = Guid.NewGuid().ToString("N").Substring(0, 12);
            var url = new Uri(page).AbsoluteUri + $"?invite={pendingInvite}&db={Uri.EscapeDataString(databaseName)}" +
                      $"&server={Uri.EscapeDataString(serverUri)}";
            // The key rides in the #fragment, which browsers never send to a server; the log line below omits it.
            var withKey = string.IsNullOrEmpty(syncKey) ? url : url + "#key=" + Uri.EscapeDataString(syncKey);
            // Opening a file:// URL drops its ?query on macOS, so open a one-line launcher that redirects with it.
            var launcher = System.IO.Path.Combine(Application.temporaryCachePath, "thinkink-whiteboard.html");
            System.IO.File.WriteAllText(launcher, $"<!doctype html><script>location.replace({Quote(withKey)});</script>");
            Debug.Log("[Coop] Opening test whiteboard: " + url);
            Application.OpenURL(new Uri(launcher).AbsoluteUri);
            // The launcher holds the key; it's only needed until the browser has read it.
            DeleteLater(launcher);
        }

        public static string TestWhiteboardPath
        {
            get
            {
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "tools", "whiteboard", "index.html"));
                return System.IO.File.Exists(path) ? path : null;
            }
        }

        static async void DeleteLater(string path)
        {
            await System.Threading.Tasks.Task.Delay(20000);
            try { System.IO.File.Delete(path); } catch (System.IO.IOException) { }
        }

        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "\\u003c") + "\"";

        public bool WaitingForWhiteboard => pendingInvite != null && Room == null;

        void JoinInvitedRoom()
        {
            foreach (var r in conn.Db.Room.Iter())
            {
                if (r.Invite != pendingInvite) continue;
                pendingInvite = null;
                Debug.Log($"[Coop] Test whiteboard opened room {r.RoomCode}; joining as Explorer.");
                JoinRoom(r.RoomCode);
                return;
            }
        }

        public void AddStroke(string captureId, uint sequence, List<float> points) =>
            CallInRoom(CoopRole.Creator, r => conn.Reducers.AddStroke(r, captureId, sequence, points));

        public void PublishBoardFrame(string captureId, string pngBase64, int width, int height) =>
            CallInRoom(CoopRole.Creator, r => conn.Reducers.PublishBoardFrame(r, captureId, pngBase64, (uint)width, (uint)height));

        public void ClearDrawing() => CallInRoom(CoopRole.Creator, r => conn.Reducers.ClearDrawing(r));

        /// <summary>Empty <paramref name="physicsJson"/> publishes the drawing as "pending" while it's interpreted.</summary>
        public void PublishInvention(string captureId, string pngBase64, string physicsJson, Vector2 position) =>
            CallInRoom(CoopRole.Creator, r =>
                conn.Reducers.PublishInvention(r, captureId, pngBase64 ?? "", physicsJson ?? "", position.x, position.y));

        public void SpendInk(ulong inventionId) => CallInRoom(CoopRole.Explorer, _ => conn.Reducers.SpendInk(inventionId));

        public void UpdateInventionState(ulong inventionId, string status) =>
            CallInRoom(CoopRole.Explorer, _ => conn.Reducers.UpdateInventionState(inventionId, status));

        public void SetPuzzleCompleted(bool completed) =>
            CallInRoom(CoopRole.Explorer, r => conn.Reducers.SetPuzzleCompleted(r, completed));

        public void ResetLevel() => CallInRoom(CoopRole.None, r => conn.Reducers.ResetLevel(r));

        public IEnumerable<DrawingStroke> Strokes() =>
            Room != null && conn != null ? conn.Db.DrawingStroke.RoomId.Filter(Room.RoomId) : Array.Empty<DrawingStroke>();

        public BoardFrame BoardFrame() => Room != null && conn != null ? conn.Db.BoardFrame.RoomId.Find(Room.RoomId) : null;

        public InventionRow FindInvention(string captureId)
        {
            if (Room == null || conn == null) return null;
            foreach (var i in conn.Db.Invention.ByRoomCapture.Filter((Room.RoomId, captureId))) return i;
            return null;
        }

        void Call(Action call)
        {
            if (conn == null || Link != CoopLink.Online)
            {
                Fail("Not connected to SpacetimeDB.");
                return;
            }
            call();
        }

        /// <summary>Calls a room reducer if this client is in a room (and has <paramref name="role"/>, unless None).</summary>
        void CallInRoom(CoopRole role, Action<ulong> call)
        {
            if (Room == null || (role != CoopRole.None && Role != role)) return;
            var roomId = Room.RoomId;
            Call(() => call(roomId));
        }

        void Fail(string message)
        {
            LastError = message;
            LastErrorTime = Time.unscaledTime;
            Debug.LogWarning("[Coop] " + message);
            Error?.Invoke(message);
        }

        static string ArgValue(string prefix)
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return arg.Substring(prefix.Length);
            }
            return null;
        }
    }
}
