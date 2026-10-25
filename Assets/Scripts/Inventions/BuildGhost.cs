using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Dashed outline of where a free build will stand, using the actual placement resolver, shown from BUILD until it's
    /// built. BUILD locks the spot, so the outline stays put while B walks off. Once the drawing is in (being read, or
    /// waiting for room) a pale copy of it stands in the outline, exactly where and as big as it will be built. A BUILD
    /// refused for want of room flashes it red where B is. G toggles the ghost.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class BuildGhost : MonoBehaviour
    {
        const float FrontNudge = 0.03f;
        /// <summary>How far the ghost drawing's ink is lifted toward white, so it reads as "not built yet".</summary>
        const float Pale = 0.55f;

        [Tooltip("Quad showing the outline, drawn at BuildSlot.DefaultBox's aspect. Not under the flipping Visual.")]
        [SerializeField] Transform outline;
        float nextPreview;
        Renderer outlineRenderer;
        MaterialPropertyBlock tint;
        Bounds bounds;
        bool clear;

        // The ghost drawing: a pale copy of RunManager's texture (which it keeps owning), rebuilt when that changes.
        Texture2D source;
        Cutout drawing;
        Renderer drawingRenderer;

        /// <summary>Where the next free build will stand, while the ghost shows; for the HUD's tag.</summary>
        public static Bounds? Showing { get; private set; }

        void Awake()
        {
            if (outline != null) outline.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            Showing = null;
            ClearDrawing();
        }

        void LateUpdate()
        {
            if (outline == null) return;
            var run = RunManager.Instance;
            // Only between BUILD and the build: while a free build is captured and read, or waits for room.
            var show = run != null && run.BuildGhostVisible && !run.State.Finished && !run.Player.IsRidingAnything &&
                (run.PendingBuildCount > 0 || run.Thinking || run.NoRoomFlash ||
                 (run.State.InFlight && run.BoundSocketId == null));
            if (outline.gameObject.activeSelf != show) outline.gameObject.SetActive(show);
            if (!show)
            {
                Showing = null;
                ClearDrawing();
                return;
            }
            if (Time.unscaledTime >= nextPreview)
            {
                nextPreview = Time.unscaledTime + .15f;
                clear = run.PlacementPreview(out bounds);
            }
            // Every frame: the outline rides under B, so it would drift off a locked spot between previews.
            outline.SetPositionAndRotation(bounds.center - Vector3.forward * FrontNudge, Quaternion.identity);
            outline.localScale = new Vector3(bounds.size.x, bounds.size.y, 1f);
            Showing = bounds;
            var color = clear ? new Color(1f, .62f, .15f) : new Color(1f, .2f, .15f);
            outlineRenderer ??= outline.GetComponent<Renderer>();
            Tint(outlineRenderer, color);
            UpdateDrawing(run, color);
        }

        void UpdateDrawing(RunManager run, Color color)
        {
            var paper = run.PreviewDrawing;
            // Destroyed counts as gone too (Unity's ==), so a drawing that was dropped never lingers.
            if (paper == null)
            {
                ClearDrawing();
                return;
            }
            var size = new Vector2(bounds.size.x, bounds.size.y);
            // A new drawing, or the same one fitted to a different box (B stepped into a build slot).
            if (!ReferenceEquals(paper, source) || (drawing != null && (drawing.Size - size).sqrMagnitude > 0.0025f))
            {
                ClearDrawing();
                source = paper;
                var copy = run.InventionMaterial != null ? PaleCopy(paper) : null;
                if (copy != null)
                {
                    drawing = Cutout.Create(copy, true, run.InventionMaterial,
                        new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), 0f, size, null);
                    drawing.name = "Build Ghost Drawing";
                    drawingRenderer = drawing.GetComponent<Renderer>();
                    drawingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            if (drawing == null) return;
            // In front of the outline, bobbing a little: it isn't built yet.
            var bob = Mathf.Sin(Time.unscaledTime * 3f) * 0.05f;
            drawing.transform.position = bounds.center + new Vector3(0f, bob, -FrontNudge * 2f);
            Tint(drawingRenderer, color);
        }

        void Tint(Renderer target, Color color)
        {
            if (target == null) return;
            tint ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(tint);
            tint.SetColor("_BaseColor", color);
            tint.SetColor("_Color", color);
            target.SetPropertyBlock(tint);
        }

        /// <summary>The drawing with its ink lifted toward white (alpha kept), so the outline's tint shows through it.</summary>
        static Texture2D PaleCopy(Texture2D paper)
        {
            if (!paper.isReadable) return null;
            var pixels = paper.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                pixels[i] = new Color32((byte)Mathf.Lerp(p.r, 255f, Pale), (byte)Mathf.Lerp(p.g, 255f, Pale),
                    (byte)Mathf.Lerp(p.b, 255f, Pale), p.a);
            }
            var copy = new Texture2D(paper.width, paper.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            copy.SetPixels32(pixels);
            copy.Apply();
            return copy;
        }

        void ClearDrawing()
        {
            if (drawing != null) Destroy(drawing.gameObject);
            drawing = null;
            drawingRenderer = null;
            source = null;
        }
    }
}
