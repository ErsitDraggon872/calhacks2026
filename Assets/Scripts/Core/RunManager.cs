using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CalHacks
{
    /// <summary>
    /// Owns the run: run_id, timer, BUILD cooldown, socket binding, vision requests, free builds, checkpoints, reset and win.
    /// BUILD next to an unsolved socket hands the drawing to that socket; anywhere else it becomes a free-standing
    /// <see cref="Invention"/> just in front of the explorer, once its attributes (<see cref="InventionSpec"/>) are back.
    /// </summary>
    public class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }

        [SerializeField] GameConfig config;
        [SerializeField] PlayerController player;
        [SerializeField] CameraFollow cameraFollow;
        [SerializeField] Transform startPoint;
        [Tooltip("Base material for free-built inventions (the Cutout material).")]
        [SerializeField] Material inventionMaterial;

        /// <summary>socket_id sent with a capture that isn't for any socket.</summary>
        public const string FreeBuildId = "free";

        IVisionClient vision;
        MockVisionClient mock;
        PuzzleSocket[] sockets = Array.Empty<PuzzleSocket>();
        Checkpoint[] checkpoints = Array.Empty<Checkpoint>();
        HintZone[] hints = Array.Empty<HintZone>();
        CancellationTokenSource runCts = new CancellationTokenSource();
        CancellationTokenSource requestCts;
        PuzzleSocket boundSocket;
        readonly List<Invention> inventions = new List<Invention>();
        Transform inventionRoot;
        Checkpoint currentCheckpoint;
        Vector3 startPosition;
        Quaternion startRotation;
        float nextHealthPoll;
        bool healthInFlight;
        readonly Queue<Action> pendingInput = new Queue<Action>();
        readonly List<(InputAction, Action<InputAction.CallbackContext>)> inputBindings =
            new List<(InputAction, Action<InputAction.CallbackContext>)>();

        public RunState State { get; } = new RunState();
        public float FinalTime { get; private set; }
        public bool OverlayVisible { get; private set; }
        public bool PreviewVisible { get; private set; }
        /// <summary>The build ghost outline in free-build hint zones. On by default; G toggles.</summary>
        public bool BuildGhostVisible { get; private set; } = true;
        public string VisionName => vision?.Name ?? "-";
        public string LastProvider { get; private set; }
        public float LastLatencyMs { get; private set; } = -1f;
        public string LastLabel { get; private set; }
        /// <summary>The last drawing's attributes, as <see cref="InventionSpec.Describe"/> prints them.</summary>
        public string LastAttributes { get; private set; }
        public float LastConfidence { get; private set; } = -1f;
        public HealthResponse LastHealth { get; private set; }
        public string LastHealthError { get; private set; }
        public string LastError { get; private set; }
        public string BoundSocketId => boundSocket != null ? boundSocket.SocketId : null;
        public string NearestSocketId => NearestInRange()?.SocketId;
        public string MockNext => mock?.DescribeNext();
        public string SolvedSummary => sockets.Length == 0
            ? "-"
            : string.Join(", ", sockets.Select(s => $"{s.SocketId} {(s.Solved ? "✓" : "·")}"));
        public IReadOnlyList<PuzzleSocket> Sockets => sockets;
        public int InventionCount => inventions.Count;

        void Awake()
        {
            Instance = this;
            if (config == null) config = Resources.Load<GameConfig>("GameConfig");
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();
            Application.targetFrameRate = config.targetFrameRate;

            switch (config.ResolveCaptureSource())
            {
                case CaptureSource.Service:
                    vision = new HttpVisionClient(config);
                    break;
                case CaptureSource.Webcam:
                    var boardCamera = GetComponent<BoardCamera>();
                    if (boardCamera == null) boardCamera = FindFirstObjectByType<BoardCamera>();
                    // No interpreter backend yet (POST /interpret, docs/interpret-contract.md): every drawing is "unknown".
                    vision = new WebcamVisionClient(config, boardCamera, new NoInterpreter());
                    break;
                default:
                    mock = new MockVisionClient(config);
                    vision = mock;
                    break;
            }

            sockets = FindObjectsByType<PuzzleSocket>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(s => s.SocketId).ToArray();
            checkpoints = FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            hints = FindObjectsByType<HintZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (cameraFollow == null) cameraFollow = FindFirstObjectByType<CameraFollow>();
            inventionRoot = new GameObject("Inventions").transform;

            var start = startPoint != null ? startPoint : player != null ? player.transform : transform;
            startPosition = start.position;
            startRotation = start.rotation;
            GameInput.Enable();
        }

        // Button presses are latched from `performed` callbacks rather than polled with WasPressedThisFrame,
        // so a press is never lost when events are processed outside the player update (e.g. unfocused Game view).
        void OnEnable()
        {
            Bind(GameInput.Cast, _ => Cast());
            Bind(GameInput.ResetLevel, _ => ResetRun());
            Bind(GameInput.ManualAccept, _ => ManualAccept());
            Bind(GameInput.ManualReject, _ => ManualReject());
            Bind(GameInput.DebugOverlay, _ => OverlayVisible = !OverlayVisible);
            Bind(GameInput.CameraPreview, _ => PreviewVisible = !PreviewVisible);
            Bind(GameInput.BuildGhost, _ => BuildGhostVisible = !BuildGhostVisible);
            Bind(GameInput.Recalibrate, _ => Recalibrate());
            Bind(GameInput.MockPick, control => PickMockDrawing(control as KeyControl));
        }

        void OnDisable()
        {
            foreach (var (action, callback) in inputBindings) action.performed -= callback;
            inputBindings.Clear();
            pendingInput.Clear();
        }

        void Bind(InputAction action, Action<InputControl> handler)
        {
            Action<InputAction.CallbackContext> callback = ctx =>
            {
                var control = ctx.control;
                pendingInput.Enqueue(() => handler(control));
            };
            action.performed += callback;
            inputBindings.Add((action, callback));
        }

        void Start()
        {
            Debug.Log($"[Run] Vision client: {vision.Name}. Sockets: {string.Join(", ", sockets.Select(s => s.SocketId))}");
            ResetRun();
            PollHealth();
        }

        void OnDestroy()
        {
            runCts.Cancel();
            runCts.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            State.Tick(Time.deltaTime);

            while (pendingInput.Count > 0) pendingInput.Dequeue()();

            if (player != null && player.transform.position.y < config.killY) Respawn();

            if (OverlayVisible && Time.unscaledTime >= nextHealthPoll) PollHealth();
        }

        // ---- CAST ----------------------------------------------------------------------------------------

        public async void Cast()
        {
            switch (State.CanCast())
            {
                case CastBlock.InFlight:
                case CastBlock.Finished:
                case CastBlock.Cooldown:
                    return;
            }

            // An unsolved socket in range gets the drawing (that's how the gap snaps it across itself); otherwise it's a free build.
            var socket = NearestInRange();
            var runId = State.RunId;
            boundSocket = socket;
            State.BeginRequest();
            requestCts = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
            var ct = requestCts.Token;
            var request = new CaptureRequest
            {
                socket_id = socket != null ? socket.SocketId : FreeBuildId,
                run_id = runId,
                accepted_labels = socket != null && socket.AcceptedLabels != null ? socket.AcceptedLabels : Array.Empty<string>(),
            };
            Debug.Log($"[Run] CAST run_id={runId} socket={request.socket_id}");

            try
            {
                var capture = await vision.CaptureAsync(request, ct);
                if (!State.IsCurrent(runId) || ct.IsCancellationRequested) return;
                if (!capture.Ok)
                {
                    FailRequest(capture.Error);
                    return;
                }
                if (!State.ShouldApply(runId, capture.Data.run_id))
                {
                    Debug.LogWarning($"[Run] Dropped stale capture (run_id {capture.Data.run_id}, current {State.RunId}).");
                    State.EndRequest();
                    return;
                }
                if (capture.Data.empty_board || string.IsNullOrEmpty(capture.Data.png_base64))
                {
                    State.EndRequest();
                    Subtitles.ShowNow("The board looks empty. Draw something first!");
                    return;
                }
                var paper = PaperTexture.FromBase64(capture.Data.png_base64);
                if (paper == null)
                {
                    FailRequest("could not decode png_base64");
                    return;
                }

                // A socket shows the drawing right away (the gap never waits); a free build waits for what it is, so
                // it can be checked before it's placed.
                if (socket != null && socket.ApplyCapture(capture.Data, paper)) State.StartCooldown(config.buildCooldown);
                if (socket == null) Subtitles.ShowNow("Hmm, what is it...", 3f);

                var label = await vision.GetLabelAsync(capture.Data.id, ct);
                if (!State.IsCurrent(runId) || ct.IsCancellationRequested)
                {
                    if (socket == null) Destroy(paper);
                    return;
                }
                State.EndRequest();
                var spec = SpecFor(label, runId, out var stale);
                if (stale)
                {
                    if (socket == null) Destroy(paper);
                    return;
                }

                if (socket == null)
                {
                    if (PlaceInvention(paper, spec)) State.StartCooldown(config.buildCooldown);
                    return;
                }
                ApplySpec(socket, spec);
            }
            catch (OperationCanceledException)
            {
                // Reset or manual override; whoever cancelled already settled the state.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (State.IsCurrent(runId)) FailRequest(e.Message);
            }
        }

        /// <summary>
        /// The drawing's attributes from a label reply: the backend's interpretation when there is one, else the label
        /// mapped through <see cref="InventionSpec.FromLabel"/>. A failed reply is a plain Support. Sets
        /// <paramref name="stale"/> for a reply from an older run, which must be dropped.
        /// </summary>
        InventionSpec SpecFor(VisionResult<LabelResponse> label, int runId, out bool stale)
        {
            stale = false;
            if (!label.Ok)
            {
                LastError = label.Error;
                Debug.LogWarning("[Run] Label failed: " + label.Error);
                return InventionSpec.Plain();
            }
            if (!State.ShouldApply(runId, label.Data.run_id))
            {
                Debug.LogWarning($"[Run] Dropped stale label (run_id {label.Data.run_id}, current {State.RunId}).");
                stale = true;
                return null;
            }
            LastProvider = label.Data.provider;
            LastLatencyMs = label.Data.latency_ms;
            LastConfidence = label.Data.confidence;
            var spec = InventionSpec.IsPresent(label.Data.interpretation)
                ? InventionSpec.Parse(label.Data.interpretation)
                : InventionSpec.FromLabel(label.Data.status == "done" ? label.Data.label : SocketLogic.Unknown);
            if (spec.Dropped.Count > 0) Debug.LogWarning("[Run] Ignored unknown attributes: " + string.Join(", ", spec.Dropped));
            LastLabel = spec.Name;
            LastAttributes = spec.Describe();
            return spec;
        }

        /// <summary>
        /// Stands the drawing in the <see cref="BuildSlot"/> the explorer is in (replacing what was there), or else just in
        /// front of them on the side they face, moved out of the level's geometry, once <see cref="InventionFeasibility"/>
        /// agrees. False if nothing was built (nothing is spent then).
        /// </summary>
        bool PlaceInvention(Texture2D paper, InventionSpec spec)
        {
            if (player == null || inventionMaterial == null)
            {
                if (inventionMaterial == null) Debug.LogError("[Run] RunManager has no Invention Material; run Game/Build Assets.");
                Destroy(paper);
                return false;
            }
            var feet = player.transform.position;
            var slot = BuildSlot.At(feet);
            var box = slot != null ? slot.Box : config.inventionSize;
            var depth = slot != null ? slot.Depth : config.inventionDepth;
            var size = InventionPlacement.FitInside(paper.width, paper.height, box);
            Vector3 bottom;
            var spotClear = true;
            if (slot != null)
            {
                bottom = slot.BottomCentreFor(size);
            }
            else
            {
                bottom = InventionPlacement.BottomCentre(feet, player.FacingSign, player.Radius, size.x);
                var build = InventionPlacement.BuildBounds(bottom, size, depth);
                spotClear = InventionPlacement.ClearSpot(build, ObstaclesAround(build), player.FacingSign, out var shift);
                bottom += shift;
            }

            var feasibility = InventionFeasibility.Check(spec, spotClear);
            Debug.Log($"[Run] Free build '{spec.Name}': {feasibility.Verdict}");
            if (!feasibility.Builds)
            {
                Destroy(paper);
                Subtitles.ShowNow(feasibility.Message);
                return false;
            }
            if (slot != null) RemoveInvention(slot.Occupant);

            var invention = Invention.Create(paper, box, bottom, depth, inventionMaterial, inventionRoot, config.inventionCell);
            invention.Apply(spec);
            Subtitles.Show(feasibility.Verdict == Verdict.Downgraded ? feasibility.Message : Understood(spec));
            inventions.Add(invention);
            if (slot != null) slot.Occupant = invention;
            LiftPlayerOnto(invention);
            Debug.Log($"[Run] Free build #{inventions.Count} at {bottom}{(slot != null ? " in " + slot.name : "")}");
            return true;
        }

        /// <summary>Bounds of every solid collider near <paramref name="build"/> except the explorer's: level geometry and other builds.</summary>
        List<Bounds> ObstaclesAround(Bounds build)
        {
            var reach = InventionPlacement.MaxShift + 1f;
            var hits = Physics.OverlapBox(build.center, build.extents + new Vector3(reach, 1f, reach), Quaternion.identity,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            var obstacles = new List<Bounds>(hits.Length);
            foreach (var hit in hits)
            {
                if (hit.GetComponentInParent<PlayerController>() == null) obstacles.Add(hit.bounds);
            }
            return obstacles;
        }

        /// <summary>If the new build appeared around the explorer (a slot build where they stand), puts them on top of it.</summary>
        void LiftPlayerOnto(Invention invention)
        {
            Physics.SyncTransforms();
            var body = player.GetComponent<Collider>();
            if (body == null) return;
            var top = InventionPlacement.LiftTo(body.bounds,
                invention.GetComponentsInChildren<BoxCollider>().Select(box => box.bounds));
            if (top == null) return;
            var feet = player.transform.position;
            player.Teleport(new Vector3(feet.x, top.Value + 0.05f, feet.z),
                Quaternion.LookRotation(Vector3.right * player.FacingSign));
        }

        void RemoveInvention(Invention invention)
        {
            if (invention == null) return;
            inventions.Remove(invention);
            Destroy(invention.gameObject);
        }

        void ClearInventions()
        {
            foreach (var invention in inventions)
            {
                if (invention != null) Destroy(invention.gameObject);
            }
            inventions.Clear();
        }

        void ApplySpec(PuzzleSocket socket, InventionSpec spec)
        {
            LastLabel = spec.Name;
            LastAttributes = spec.Describe();
            Debug.Log($"[Run] {spec.Describe()} -> socket {socket.SocketId}");
            Subtitles.Show(Understood(spec));
            socket.ApplySpec(spec);
        }

        /// <summary>The "AI understood" line: "Looks like: flaming rubber skateboard (glows, hot)."</summary>
        static string Understood(InventionSpec spec)
        {
            var shown = spec.Traits.Where(t => t == Trait.Glows || t == Trait.Hot || t == Trait.Cold || t == Trait.Edible
                    || t == Trait.Hostile || t == Trait.Bouncy || t == Trait.Slippery || t == Trait.Sticky)
                .OrderBy(t => t).Select(t => InventionSpec.Snake(t)).ToArray();
            return $"Looks like: {spec.Name}{(shown.Length > 0 ? " (" + string.Join(", ", shown) + ")" : "")}.";
        }

        void FailRequest(string error)
        {
            State.EndRequest();
            LastError = error;
            Debug.LogWarning("[Run] Capture failed: " + error);
            Subtitles.ShowNow(DescribeError(error));
        }

        static string DescribeError(string error)
        {
            var e = (error ?? string.Empty).ToLowerInvariant();
            if (e.Contains("calibrat")) return "The camera isn't calibrated. Press F9 to recalibrate.";
            if (e.Contains("empty")) return "The board looks empty. Draw something first!";
            if (e.Contains("camera")) return "Can't see the board right now (camera error).";
            if (e.Contains("unreachable") || e.Contains("connect")) return "Can't reach the vision service.";
            if (e.Contains("timeout") || e.Contains("timed out")) return "The camera took too long. Try CAST again.";
            return "Something went wrong reading the board. Try CAST again.";
        }

        void CancelRequest()
        {
            if (requestCts == null) return;
            requestCts.Cancel();
            requestCts = null;
            State.EndRequest();
        }

        // ---- Station buttons and operator keys ----------------------------------------------------------

        public void ManualAccept()
        {
            var socket = OperatorTarget();
            if (socket == null) return;
            if (boundSocket == socket) CancelRequest();
            Debug.Log("[Run] Manual accept -> " + socket.SocketId);
            socket.ForceSolve();
        }

        public void ManualReject()
        {
            var socket = OperatorTarget();
            if (socket == null) return;
            if (boundSocket == socket) CancelRequest();
            Debug.Log("[Run] Manual reject -> " + socket.SocketId);
            ApplySpec(socket, InventionSpec.Plain());
        }

        /// <summary>The bound socket while unsolved, else the nearest unsolved socket in range, else the nearest unsolved.</summary>
        PuzzleSocket OperatorTarget()
        {
            if (boundSocket != null && !boundSocket.Solved) return boundSocket;
            return NearestInRange() ?? NearestUnsolved(float.PositiveInfinity);
        }

        /// <summary>How far above or below a socket B can be and still BUILD for it (B on a ledge over it can't).</summary>
        const float MaxCastRise = 1.5f;

        /// <summary>The nearest unsolved socket within its own BUILD range (default <see cref="GameConfig.castRange"/>).</summary>
        PuzzleSocket NearestInRange()
        {
            if (player == null || sockets.Length == 0) return null;
            var index = SocketLogic.FindNearest(sockets.Select(s => s.CastPoint).ToArray(), sockets.Select(s => s.Solved).ToArray(),
                sockets.Select(s => s.CastRange).ToArray(), player.transform.position, config.castRange, MaxCastRise);
            return index >= 0 ? sockets[index] : null;
        }

        PuzzleSocket NearestUnsolved(float range)
        {
            if (player == null || sockets.Length == 0) return null;
            var points = sockets.Select(s => s.CastPoint).ToArray();
            var solved = sockets.Select(s => s.Solved).ToArray();
            var index = SocketLogic.FindNearest(points, solved, player.transform.position, range);
            return index >= 0 ? sockets[index] : null;
        }

        void PickMockDrawing(KeyControl key)
        {
            if (mock == null || key == null) return;
            var digit = key.keyCode - Key.Digit1;
            if (key.keyCode == Key.Digit0) digit = MockVisionClient.EmptyBoard;
            if (digit != MockVisionClient.EmptyBoard && (digit < 0 || digit >= config.mockDrawings.Length)) return;
            mock.ForceNext(digit);
            Subtitles.ShowNow("[mock] next drawing: " + mock.DescribeNext(), 1.5f);
        }

        async void Recalibrate()
        {
            Subtitles.ShowNow("Recalibrating the camera...", 2f);
            try
            {
                var result = await vision.CalibrateAsync(runCts.Token);
                if (result.Ok)
                {
                    Subtitles.ShowNow(result.Data.saved
                        ? $"Calibrated ({result.Data.markers_found} markers found)."
                        : $"Calibration not saved: only {result.Data.markers_found} markers found.");
                }
                else
                {
                    LastError = result.Error;
                    Subtitles.ShowNow("Calibration failed: " + result.Error);
                }
            }
            catch (OperationCanceledException)
            {
            }
            PollHealth();
        }

        async void PollHealth()
        {
            nextHealthPoll = Time.unscaledTime + config.healthPollInterval;
            if (healthInFlight) return;
            healthInFlight = true;
            try
            {
                var result = await vision.HealthAsync(destroyCancellationToken);
                LastHealth = result.Ok ? result.Data : null;
                LastHealthError = result.Ok ? null : result.Error;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                healthInFlight = false;
            }
        }

        // ---- Run flow -----------------------------------------------------------------------------------

        /// <summary>F5: new run_id, cancel pending waits, restore every socket, remove free builds, reset the explorer and the camera.</summary>
        public void ResetRun()
        {
            runCts.Cancel();
            runCts.Dispose();
            runCts = new CancellationTokenSource();
            requestCts = null;
            var runId = State.Reset();
            boundSocket = null;
            currentCheckpoint = null;
            FinalTime = 0f;

            foreach (var socket in sockets) socket.ResetSocket();
            ClearInventions();
            foreach (var checkpoint in checkpoints) checkpoint.SetReached(false);
            foreach (var hint in hints) hint.ResetHint();
            if (player != null)
            {
                player.InputLocked = false;
                player.Teleport(startPosition, startRotation);
            }
            if (cameraFollow != null)
            {
                cameraFollow.SetFraming(CameraFraming.Tight, true);
                cameraFollow.Snap();
            }
            Subtitles.Clear();
            Debug.Log($"[Run] Reset. run_id={runId}");
        }

        public void ReachCheckpoint(Checkpoint checkpoint)
        {
            if (currentCheckpoint == checkpoint) return;
            var first = currentCheckpoint == null;
            if (currentCheckpoint != null) currentCheckpoint.SetReached(false);
            currentCheckpoint = checkpoint;
            checkpoint.SetReached(true);
            Subtitles.Show("Checkpoint!", 1.5f);
            // The run's first checkpoint is where the world opens up: zoom out to show it, then back in.
            if (first && cameraFollow != null) cameraFollow.Reveal();
        }

        void Respawn()
        {
            if (currentCheckpoint != null) player.Teleport(currentCheckpoint.RespawnPosition, currentCheckpoint.RespawnRotation);
            else player.Teleport(startPosition, startRotation);
            if (cameraFollow != null) cameraFollow.Snap();
            Subtitles.ShowNow("Whoops! Back to the checkpoint.", 2f);
        }

        public void Win()
        {
            if (State.Finished) return;
            State.Finish();
            FinalTime = State.Elapsed;
            CancelRequest();
            if (player != null) player.InputLocked = true;
            Debug.Log($"[Run] Win. run_id={State.RunId} time={Hud.FormatTime(FinalTime)}");
        }
    }
}
