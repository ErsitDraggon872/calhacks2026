using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Orange dashed outline of where the next free build will land: the front face of the <see cref="BuildSlot"/> B is
    /// standing in. Every drawing is fitted inside that box, so the outline is honest whatever is drawn, and the slot
    /// sits in front of anything that could hide it. Shown only inside a slot, and while
    /// <see cref="RunManager.BuildGhostVisible"/> (G toggles).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class BuildGhost : MonoBehaviour
    {
        const float FrontNudge = 0.03f;

        [Tooltip("Quad showing the outline, drawn at BuildSlot.DefaultBox's aspect. Not under the flipping Visual.")]
        [SerializeField] Transform outline;

        void Awake()
        {
            if (outline != null) outline.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (outline == null) return;
            var run = RunManager.Instance;
            // Checked against the slots' bounds each frame rather than trigger enter/exit, which teleports and resets skip.
            var slot = run != null && run.BuildGhostVisible ? BuildSlot.At(transform.position) : null;
            var show = slot != null;
            if (outline.gameObject.activeSelf != show) outline.gameObject.SetActive(show);
            if (!show) return;

            // Outline the slot's front face, nudged toward the camera so the build's own drawing doesn't cover it.
            var bottom = slot.SlotBottom;
            bottom.z -= slot.Depth * 0.5f + FrontNudge;
            outline.SetPositionAndRotation(bottom + Vector3.up * (slot.Box.y * 0.5f), Quaternion.identity);
            outline.localScale = new Vector3(slot.Box.x, slot.Box.y, 1f);
        }
    }
}
