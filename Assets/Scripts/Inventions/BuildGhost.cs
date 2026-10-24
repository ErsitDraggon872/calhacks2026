using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Orange dashed outline of where the next free build will land: the front face of the largest invention, beside B
    /// on the side B faces (see <see cref="InventionPlacement.GhostBottomCentre"/>). Shown only while B stands in a
    /// <see cref="HintZone"/> that asks for it, and while <see cref="RunManager.BuildGhostVisible"/> (G toggles).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class BuildGhost : MonoBehaviour
    {
        static readonly List<HintZone> Zones = new List<HintZone>();
        const float FrontNudge = 0.03f;

        [SerializeField] GameConfig config;
        [Tooltip("Quad showing the outline, sized to the largest invention. Not under the flipping Visual.")]
        [SerializeField] Transform outline;

        PlayerController player;

        /// <summary>Called by HintZones with showBuildGhost while they're enabled.</summary>
        public static void Register(HintZone zone)
        {
            if (!Zones.Contains(zone)) Zones.Add(zone);
        }

        public static void Unregister(HintZone zone) => Zones.Remove(zone);

        void Awake()
        {
            player = GetComponent<PlayerController>();
            if (config == null) config = Resources.Load<GameConfig>("GameConfig");
            if (outline != null) outline.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (outline == null || config == null) return;
            var run = RunManager.Instance;
            var show = run != null && run.BuildGhostVisible && InAnyZone();
            if (outline.gameObject.activeSelf != show) outline.gameObject.SetActive(show);
            if (!show) return;

            var box = config.inventionSize;
            var bottom = InventionPlacement.GhostBottomCentre(transform.position, player.FacingSign, player.Radius, box);
            // Outline the build's front face (its colliders are inventionDepth deep, centred on B's depth), nudged toward
            // the camera so it isn't buried in a wall it would clip into: the overlap shows instead.
            bottom.z -= config.inventionDepth * 0.5f + FrontNudge;
            outline.SetPositionAndRotation(bottom + Vector3.up * (box.y * 0.5f), Quaternion.identity);
            outline.localScale = new Vector3(box.x, box.y, 1f);
        }

        // Checked against the zones' bounds each frame rather than trigger enter/exit, which teleports and resets skip.
        bool InAnyZone()
        {
            var centre = transform.position + Vector3.up;
            foreach (var zone in Zones)
                if (zone != null && zone.Contains(centre)) return true;
            return false;
        }
    }
}
