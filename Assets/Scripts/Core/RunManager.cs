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
    /// <see cref="Invention"/> just in front of the explorer.
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
        IVisionClient digitalVision;
        IVisionClient webcamVision;
        MockVisionClient mock;
        InterpretHttpClient interpretClient;
        public string LastInventionMessage { get; private set; }
        public int LastInkCost { get; private set; }
        DigitalDrawingCanvas digitalCanvas;
        BoardCamera boardCamera;
        CaptureSource activeCaptureSource;
        GUIStyle inputLabel;
        GUIStyle selectedInputButton;
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
        public CaptureSource ActiveCaptureSource => activeCaptureSource;
        public string LastProvider { get; private set; }
        public float LastLatencyMs { get; private set; } = -1f;
        public string LastLabel { get; private set; }
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

            // Keep both input systems ready, so users can switch inside the running game.
            // The digital canvas costs no camera resources until it is opened.
            digitalCanvas = GetComponent<DigitalDrawingCanvas>();
            if (digitalCanvas == null) digitalCanvas = gameObject.AddComponent<DigitalDrawingCanvas>();
            digitalCanvas.Initialize(this);
            boardCamera = GetComponent<BoardCamera>();
            if (boardCamera == null) boardCamera = FindFirstObjectByType<BoardCamera>();
            if (boardCamera == null) boardCamera = gameObject.AddComponent<BoardCamera>();
            interpretClient = new InterpretHttpClient(config);
            digitalVision = new DigitalVisionClient(digitalCanvas, new NoInterpreter());
            webcamVision = new WebcamVisionClient(config, boardCamera, new NoInterpreter());

            activeCaptureSource = config.ResolveCaptureSource();
            switch (activeCaptureSource)
            {
                case CaptureSource.Service:
                    vision = new HttpVisionClient(config);
                    break;
                case CaptureSource.Digital:
                    vision = digitalVision;
                    break;
                case CaptureSource.Webcam:
                    vision = webcamVision;
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
            if (GetComponent<InventionGuide>() == null) gameObject.AddComponent<InventionGuide>();

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
            Bind(GameInput.CameraPreview, _ =>
            {
                if (activeCaptureSource == CaptureSource.Digital) digitalCanvas.Toggle();
                else if (activeCaptureSource == CaptureSource.Webcam) PreviewVisible = !PreviewVisible;
            });
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
            if (activeCaptureSource == CaptureSource.Webcam) boardCamera.SetCaptureEnabled(true);
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

            // E triggers the closest interpreted object only, not all nearby boosters.
            // Keep HOLD activation state synchronized with the physical E key.
            bool holdingInteract = player != null && !player.InputLocked && Keyboard.current != null &&
                                   Keyboard.current.eKey.isPressed && !State.InFlight;
            foreach (var invention in inventions)
            {
                if (invention == null) continue;
                var behavior = invention.GetComponent<InventionPhysics>();
                if (behavior != null) behavior.SetHeld(holdingInteract);
            }
            if (holdingInteract && Keyboard.current.eKey.wasPressedThisFrame)
            {
                InventionPhysics closest = null;
                float best = 2.5f * 2.5f;
                foreach (var invention in inventions)
                {
                    if (invention == null) continue;
                    var physics = invention.GetComponent<InventionPhysics>();
                    if (physics == null || !physics.HasButton) continue;
                    float distance = float.PositiveInfinity;
                    foreach (var box in physics.GetComponentsInChildren<BoxCollider>())
                    {
                        var point = box.ClosestPoint(player.transform.position);
                        distance = Mathf.Min(distance, (point - player.transform.position).sqrMagnitude);
                    }
                    if (distance < best) { closest = physics; best = distance; }
                }
                if (closest != null) closest.TryInteract(player);
            }

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
            // AI paper inventions are free builds. Legacy socket label flow remains for Mock/Service mode.
            bool interpretBuild = config.enableAiPhysics &&
                (activeCaptureSource == CaptureSource.Digital || activeCaptureSource == CaptureSource.Webcam);
            var socket = interpretBuild ? null : NearestInRange();
            var runId = State.RunId;
            boundSocket = socket;
            State.BeginRequest();
            var activeRequest = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
            requestCts = activeRequest;
            var ct = activeRequest.Token;
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

                if (interpretBuild)
                {
                    Subtitles.ShowNow("Bringing your sketch to life...", 2f);
                    var isDigital = activeCaptureSource == CaptureSource.Digital && digitalCanvas != null;
                    var result = await interpretClient.InterpretAsync(capture.Data, runId, FreeBuildId,
                        config.inventionGoal, ct,
                        isDigital ? (digitalCanvas.IntentText + " " + digitalCanvas.IntentHint).Trim() : "",
                        isDigital ? digitalCanvas.IntentMode : "auto");
                    if (!State.IsCurrent(runId) || ct.IsCancellationRequested) { Destroy(paper); return; }
                    State.EndRequest();
                    if (!result.Ok)
                    {
                        Destroy(paper);
                        FailRequest(result.Error);
                        return;
                    }
                    var spec = result.Data;
                    if (!InterpretContract.Validate(spec, out var reason))
                    {
                        Destroy(paper);
                        LastError = reason;
                        Subtitles.ShowNow(reason, 4f);
                        return;
                    }
                    LastProvider = spec.provider;
                    LastLatencyMs = spec.latency_ms;
                    LastInkCost = spec.ink_cost;
                    LastInventionMessage = spec.message;
                    LastLabel = spec.name;
                    Debug.Log($"[DeerAI] {JsonUtility.ToJson(spec, true)}");
                    if (PlaceInvention(paper, spec))
                    {
                        State.StartCooldown(config.buildCooldown);
                        Subtitles.ShowNow("Invention ready! Explorer, it is your turn.", 2f);
                        InventionGuide.Show(spec);
                        digitalCanvas?.Close();
                        if (activeCaptureSource == CaptureSource.Digital) digitalCanvas.Clear();
                    }
                    return;
                }

                // The capture already owns an immutable copy; ready the pad for the next invention.
                if (activeCaptureSource == CaptureSource.Digital)
                {
                    digitalCanvas.Close();
                    digitalCanvas.Clear();
                }

                if (socket == null)
                {
                    // AI disabled: preserve solid-cutout builds.
                    State.EndRequest();
                    if (PlaceInvention(paper)) State.StartCooldown(config.buildCooldown);
                    return;
                }

                if (socket.ApplyCapture(capture.Data, paper)) State.StartCooldown(config.buildCooldown);

                var label = await vision.GetLabelAsync(capture.Data.id, ct);
                if (!State.IsCurrent(runId) || ct.IsCancellationRequested) return;
                State.EndRequest();
                if (!label.Ok)
                {
                    LastError = label.Error;
                    Debug.LogWarning("[Run] Label failed: " + label.Error);
                    ApplyLabel(socket, SocketLogic.Unknown);
                    return;
                }
                if (!State.ShouldApply(runId, label.Data.run_id))
                {
                    Debug.LogWarning($"[Run] Dropped stale label (run_id {label.Data.run_id}, current {State.RunId}).");
                    return;
                }
                LastProvider = label.Data.provider;
                LastLatencyMs = label.Data.latency_ms;
                LastConfidence = label.Data.confidence;
                ApplyLabel(socket, label.Data.status == "done" ? label.Data.label : SocketLogic.Unknown);
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
            finally
            {
                if (ReferenceEquals(requestCts, activeRequest)) requestCts = null;
                activeRequest.Dispose();
            }
        }

        /// <summary>
        /// Stands the drawing in the <see cref="BuildSlot"/> the explorer is in (replacing what was there), or else just in
        /// front of them on the side they face, moved out of the level's geometry. False if nothing was built.
        /// </summary>
        bool PlaceInvention(Texture2D paper, InterpretResponse spec = null)
        {
            if (player == null || inventionMaterial == null)
            {
                if (inventionMaterial == null) Debug.LogError("[Run] RunManager has no Invention Material; run Game/Build Assets.");
                Destroy(paper);
                return false;
            }
            var feet = player.transform.position;
            var slot = BuildSlot.At(feet);
            var desiredWidth = spec == null ? 0f : Mathf.Clamp(spec.width_m, 0.5f, 6f);
            // Keep the original silhouette aspect: do not force the sprite to a fixed box height.
            var box = spec == null ? (slot != null ? slot.Box : config.inventionSize)
                : new Vector2(desiredWidth, desiredWidth * paper.height / Mathf.Max(1f, paper.width));
            var depth = slot != null ? slot.Depth : config.inventionDepth;
            var size = InventionPlacement.FitInside(paper.width, paper.height, box);
            Vector3 bottom;
            if (slot != null)
            {
                bottom = slot.BottomCentreFor(size);
                RemoveInvention(slot.Occupant);
            }
            else
            {
                bottom = InventionPlacement.BottomCentre(feet, player.FacingSign, player.Radius, size.x);
                var build = InventionPlacement.BuildBounds(bottom, size, depth);
                if (!InventionPlacement.ClearSpot(build, ObstaclesAround(build), player.FacingSign, out var shift))
                {
                    Destroy(paper);
                    Subtitles.ShowNow("No room to build here!");
                    return false;
                }
                bottom += shift;
            }

            // A finer box grid follows the cutout edge more closely. These are all
            // primitive convex colliders and work with a dynamic Rigidbody.
            var cell = spec == null ? config.inventionCell : Mathf.Min(config.inventionCell, 0.10f);
            var invention = Invention.Create(paper, box, bottom, depth, inventionMaterial, inventionRoot, cell);
            inventions.Add(invention);
            if (spec != null)
            {
                var physics = invention.gameObject.AddComponent<InventionPhysics>();
                physics.Configure(invention, spec);
            }
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

        void ApplyLabel(PuzzleSocket socket, string label)
        {
            var normalized = SocketLogic.Normalize(label);
            LastLabel = normalized;
            Debug.Log($"[Run] Label '{normalized}' -> socket {socket.SocketId}");
            Subtitles.Show($"Looks like: {normalized}.");
            socket.ApplyLabel(normalized);
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
            ApplyLabel(socket, SocketLogic.Unknown);
        }

        /// <summary>The bound socket while unsolved, else the nearest unsolved socket in range, else the nearest unsolved.</summary>
        PuzzleSocket OperatorTarget()
        {
            if (boundSocket != null && !boundSocket.Solved) return boundSocket;
            return NearestInRange() ?? NearestUnsolved(float.PositiveInfinity);
        }

        PuzzleSocket NearestInRange() => NearestUnsolved(config.castRange);

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

        /// <summary>In-game input selector. Switching is unavailable while a build request is running.</summary>
        public void SelectCaptureSource(CaptureSource source)
        {
            if (source != CaptureSource.Digital && source != CaptureSource.Webcam) return;
            if (State.InFlight)
            {
                Subtitles.ShowNow("Finish the current build before changing input.", 2f);
                return;
            }
            if (source == activeCaptureSource)
            {
                if (source == CaptureSource.Digital) digitalCanvas.Open();
                return;
            }

            // Release webcam when drawing digitally, so USB devices need not be plugged in.
            PreviewVisible = false;
            if (boardCamera != null) boardCamera.SetCaptureEnabled(source == CaptureSource.Webcam);
            if (digitalCanvas != null) digitalCanvas.Close();
            activeCaptureSource = source;
            vision = source == CaptureSource.Digital ? digitalVision : webcamVision;
            mock = null;
            LastHealth = null;
            LastHealthError = null;
            LastError = null;
            if (source == CaptureSource.Digital)
            {
                digitalCanvas.Open();
                Subtitles.ShowNow("Digital drawing selected: draw in black, then BUILD.", 2f);
            }
            else
            {
                Subtitles.ShowNow("Webcam selected: aim your USB camera at the board (P to preview).", 3f);
                PreviewVisible = true;
            }
            PollHealth();
            Debug.Log($"[Run] Input source changed to {source}.");
        }

        // Visible during Play mode and in standalone builds, without editor-only menus.
        void OnGUI()
        {
            // Keep the webcam selector out of the drawing modal itself.
            if (digitalCanvas != null && digitalCanvas.IsOpen) return;
            if (inputLabel == null)
            {
                inputLabel = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                selectedInputButton = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold
                };
            }
            GUI.depth = -200; // Above the drawing pad, so modes can be switched while drawing.
            float x = Mathf.Max(8f, Screen.width - 312f);
            GUI.Box(new Rect(x, 8f, 304f, 46f), GUIContent.none);
            GUI.Label(new Rect(x + 8f, 12f, 54f, 36f), "INPUT", inputLabel);
            if (GUI.Button(new Rect(x + 65f, 14f, 112f, 32f),
                activeCaptureSource == CaptureSource.Digital ? "DRAW ✓" : "DRAW",
                activeCaptureSource == CaptureSource.Digital ? selectedInputButton : GUI.skin.button))
                SelectCaptureSource(CaptureSource.Digital);
            if (GUI.Button(new Rect(x + 184f, 14f, 112f, 32f),
                activeCaptureSource == CaptureSource.Webcam ? "WEBCAM ✓" : "WEBCAM",
                activeCaptureSource == CaptureSource.Webcam ? selectedInputButton : GUI.skin.button))
                SelectCaptureSource(CaptureSource.Webcam);
        }

        // ---- Run flow -----------------------------------------------------------------------------------

        /// <summary>F5: new run_id, cancel pending waits, restore every socket, remove free builds, reset the explorer and the camera.</summary>
        /// <summary>Keep B still while Player A is drawing; avoid unlocking after winning.</summary>
        public void SetDigitalDrawingOpen(bool open)
        {
            if (player != null) player.InputLocked = open || State.Finished;
        }

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
            LastInventionMessage = null;
            LastInkCost = 0;

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
            InventionGuide.Close();
            if (digitalCanvas != null)
            {
                digitalCanvas.Clear();
                if (activeCaptureSource == CaptureSource.Digital) digitalCanvas.Open();
                else digitalCanvas.Close();
            }
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
