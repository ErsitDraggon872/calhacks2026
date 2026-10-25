using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Dashed outline of the next free build's size envelope, using the actual placement resolver. A retained drawing
    /// previews its exact fitted bounds; red means terrain leaves no clear spot. G toggles the outline.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class BuildGhost : MonoBehaviour
    {
        const float FrontNudge = 0.03f;

        [Tooltip("Quad showing the outline, drawn at BuildSlot.DefaultBox's aspect. Not under the flipping Visual.")]
        [SerializeField] Transform outline;
        float nextPreview;
        Renderer outlineRenderer;
        MaterialPropertyBlock tint;

        void Awake()
        {
            if (outline != null) outline.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (outline == null) return;
            var run = RunManager.Instance;
            var show = run != null && run.BuildGhostVisible && !run.State.Finished && !run.Player.IsRidingAnything &&
                (run.PendingBuildCount > 0 || run.NearestSocket == null);
            if (outline.gameObject.activeSelf != show) outline.gameObject.SetActive(show);
            if (!show) return;
            if (Time.unscaledTime < nextPreview) return;
            nextPreview = Time.unscaledTime + .15f;
            var clear = run.PlacementPreview(out var bounds);
            outline.SetPositionAndRotation(bounds.center - Vector3.forward * FrontNudge, Quaternion.identity);
            outline.localScale = new Vector3(bounds.size.x, bounds.size.y, 1f);
            outlineRenderer ??= outline.GetComponent<Renderer>();
            if (outlineRenderer != null)
            {
                tint ??= new MaterialPropertyBlock();
                var color = clear ? new Color(1f, .62f, .15f) : new Color(1f, .2f, .15f);
                tint.SetColor("_BaseColor", color);
                tint.SetColor("_Color", color);
                outlineRenderer.SetPropertyBlock(tint);
            }
        }
    }
}
