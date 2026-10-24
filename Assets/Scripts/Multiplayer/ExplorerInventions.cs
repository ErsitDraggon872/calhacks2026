using System;
using System.Collections.Generic;
using UnityEngine;
using InventionRow = SpacetimeDB.Types.Invention;

namespace CalHacks
{
    /// <summary>
    /// On an Invention the Explorer spawned from the Creator's BUILD. Carries the server row's id and the /interpret
    /// physics JSON, so the physics code can read the components and report activation back.
    /// </summary>
    public class RemoteInvention : MonoBehaviour
    {
        public ulong InventionId { get; internal set; }
        public string CaptureId { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Message { get; internal set; }
        /// <summary>The /interpret response verbatim (docs/interpret-contract.md): components, width_m, ink_cost...</summary>
        public string PhysicsJson { get; internal set; }

        /// <summary>Call when the player triggers it (E for a thrust or button bounce). Both screens show "activated".</summary>
        public void ReportActivated() => ThinkInkSession.Instance?.UpdateInventionState(InventionId, "activated");

        /// <summary>Call when it's spent (fuel ran out): back to "spawned" so it can be activated again.</summary>
        public void ReportIdle() => ThinkInkSession.Instance?.UpdateInventionState(InventionId, "spawned");
    }

    /// <summary>
    /// Explorer side: accepts the Creator's inventions (spend_ink), spawns each once with the existing free-build
    /// placement, and keeps resets in step with the room. Spawning happens when the server row turns "spawned", so a
    /// reconnect, a repeated publish or a second Explorer client never spawns the same capture twice.
    /// </summary>
    public class ExplorerInventions : MonoBehaviour
    {
        /// <summary>A Creator invention was spawned here. Hook point for applying components (thrust, bounce...).</summary>
        public static event Action<Invention, RemoteInvention> Spawned;

        readonly Dictionary<ulong, Invention> spawned = new Dictionary<ulong, Invention>();
        readonly HashSet<ulong> requested = new HashSet<ulong>();
        ThinkInkSession session;
        bool resettingFromRemote;

        /// <summary>The latest arrival, for the HUD popup.</summary>
        public static InventionRow LastArrival { get; private set; }
        public static float LastArrivalTime { get; private set; } = -100f;

        void OnEnable()
        {
            RunManager.RunReset += OnLocalReset;
            RunManager.Won += OnWon;
        }

        void OnDisable()
        {
            RunManager.RunReset -= OnLocalReset;
            RunManager.Won -= OnWon;
            Unhook();
        }

        void Update()
        {
            // The session may come up after this component; hook once it exists.
            if (session == null && ThinkInkSession.Instance != null)
            {
                session = ThinkInkSession.Instance;
                session.InventionChanged += OnInvention;
                session.InventionRemoved += OnRemoved;
                session.LevelResetByPartner += OnPartnerReset;
            }
        }

        void Unhook()
        {
            if (session == null) return;
            session.InventionChanged -= OnInvention;
            session.InventionRemoved -= OnRemoved;
            session.LevelResetByPartner -= OnPartnerReset;
            session = null;
        }

        void OnInvention(InventionRow row)
        {
            if (session.Role != CoopRole.Explorer) return;
            // With the digital board, the page's BUILD is a CAST trigger here (DigitalBoardFrameSource), not an invention.
            if (RunManager.Instance != null && RunManager.Instance.UsesDigitalBoard) return;
            switch (row.Status)
            {
                case "pending":
                    Subtitles.ShowNow("Your partner is building something...", 2f);
                    break;
                case "ready":
                    // Accept it: the server charges the Ink once and flips it to "spawned".
                    if (requested.Add(row.InventionId)) session.SpendInk(row.InventionId);
                    break;
                case "spawned":
                case "activated":
                    if (!spawned.ContainsKey(row.InventionId)) Spawn(row);
                    break;
                case "failed":
                    if (spawned.ContainsKey(row.InventionId)) break;
                    Subtitles.ShowNow(string.IsNullOrEmpty(row.Message) ? "That drawing didn't work." : row.Message, 3f);
                    break;
            }
        }

        void Spawn(InventionRow row)
        {
            var run = RunManager.Instance;
            var paper = PaperTexture.FromBase64(row.ImagePngBase64);
            if (run == null || paper == null)
            {
                Debug.LogWarning($"[Coop] Can't spawn invention {row.InventionId}: {(run == null ? "no RunManager" : "bad PNG")}");
                return;
            }
            var invention = run.SpawnInvention(paper, SpecFrom(row.PhysicsJson));
            if (invention == null)
            {
                session.UpdateInventionState(row.InventionId, "failed");
                return;
            }
            var remote = invention.gameObject.AddComponent<RemoteInvention>();
            remote.InventionId = row.InventionId;
            remote.CaptureId = row.CaptureId;
            remote.DisplayName = row.Name;
            remote.Message = row.Message;
            remote.PhysicsJson = row.PhysicsJson;
            invention.name = $"Invention {row.Name} ({row.CaptureId})";
            spawned[row.InventionId] = invention;

            LastArrival = row;
            LastArrivalTime = Time.unscaledTime;
            Subtitles.ShowNow($"New invention: {(row.Name != "" ? row.Name : "drawing")}! {row.Message}", 4f);
            Debug.Log($"[Coop] Spawned invention {row.InventionId} '{row.Name}' (capture {row.CaptureId})");
            Spawned?.Invoke(invention, remote);
        }

        /// <summary>The Creator's /interpret reply as attributes, cleaned and clamped like a local free build's.</summary>
        static InventionSpec SpecFrom(string physicsJson)
        {
            if (string.IsNullOrEmpty(physicsJson)) return InventionSpec.Plain();
            try
            {
                return InventionSpec.Parse(JsonUtility.FromJson<InterpretResponse>(physicsJson));
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning("[Coop] Unreadable physics JSON, building a plain support: " + e.Message);
                return InventionSpec.Plain();
            }
        }

        void OnRemoved(InventionRow row)
        {
            requested.Remove(row.InventionId);
            if (spawned.Remove(row.InventionId, out var invention) && invention != null) RunManager.Instance?.RemoveInvention(invention);
        }

        /// <summary>This Explorer pressed F5: the room resets too (full Ink, empty board, no inventions).</summary>
        void OnLocalReset()
        {
            if (resettingFromRemote || session == null || session.Role != CoopRole.Explorer) return;
            spawned.Clear();
            requested.Clear();
            session.ResetLevel();
        }

        void OnPartnerReset()
        {
            if (session.Role != CoopRole.Explorer || RunManager.Instance == null) return;
            resettingFromRemote = true;
            try
            {
                spawned.Clear();
                requested.Clear();
                RunManager.Instance.ResetRun();
            }
            finally
            {
                resettingFromRemote = false;
            }
        }

        void OnWon() => session?.SetPuzzleCompleted(true);
    }
}
