using System;
using UnityEngine;

namespace CalHacks
{
    public enum SocketType
    {
        Gap,
        Light,
        Villager,
    }

    /// <summary>
    /// A place in the level where a drawing lands, shown as a cutout at <see cref="anchor"/>. A socket decides where the
    /// drawing goes and what solves it, never the drawing's physics: anything solid in a drawing comes from its silhouette
    /// (the gap builds an <see cref="Invention"/>, like a free build). The optional <see cref="solutionCollider"/> is
    /// level geometry switched on by solving, never a stand-in for the drawing.
    /// </summary>
    public abstract class PuzzleSocket : MonoBehaviour
    {
        [SerializeField] protected string socketId = "socket";
        [Tooltip("Labels that solve this socket. Sent to the service as accepted_labels.")]
        [SerializeField] protected string[] acceptedLabels = Array.Empty<string>();
        [Tooltip("Bottom-centre of the cutout. Its forward is used when Face Camera is off.")]
        [SerializeField] protected Transform anchor;
        [Tooltip("Largest width and height (metres) the cutout is fitted into, keeping the drawing's aspect.")]
        [SerializeField] protected Vector2 span = new Vector2(3f, 2f);
        [SerializeField] protected bool faceCamera = true;
        [Tooltip("Hidden, pre-placed collider that is enabled when the socket is solved. Optional.")]
        [SerializeField] protected BoxCollider solutionCollider;
        [Tooltip("Shown when the operator forces a solve (F1) before any drawing arrived.")]
        [SerializeField] protected Texture2D fallbackDrawing;
        [SerializeField] protected Material cutoutMaterial;
        [SerializeField] protected string catLine = "Meow! The cat curls up and watches you.";

        public string SocketId => socketId;
        public string[] AcceptedLabels => acceptedLabels;
        public bool Solved { get; private set; }
        public abstract SocketType Type { get; }
        public Vector3 CastPoint => transform.position;

        protected Cutout CurrentCutout { get; private set; }

        protected virtual void Awake()
        {
            if (anchor == null) anchor = transform;
            if (solutionCollider != null) solutionCollider.enabled = false;
        }

        /// <summary>A capture arrived for this socket: show the drawing now, before the label. False if no cutout was made.</summary>
        public bool ApplyCapture(CaptureResponse capture, Texture2D paper)
        {
            if (Solved)
            {
                Destroy(paper);
                return false;
            }
            ShowCutout(paper, true);
            OnCaptured(capture);
            return true;
        }

        public void ApplyLabel(string label)
        {
            var normalized = SocketLogic.Normalize(label);
            var outcome = SocketLogic.Resolve(normalized, acceptedLabels);
            if (Solved)
            {
                if (outcome == LabelOutcome.CatReaction) PlayCatReaction();
                return;
            }
            switch (outcome)
            {
                case LabelOutcome.Solve:
                    Solve();
                    break;
                case LabelOutcome.CatReaction:
                    PlayCatReaction();
                    break;
                default:
                    OnWrongLabel(normalized);
                    break;
            }
        }

        /// <summary>Manual accept (F1): solve regardless of the label, using the fallback drawing if needed.</summary>
        public virtual void ForceSolve()
        {
            if (Solved) return;
            if (CurrentCutout == null && fallbackDrawing != null)
            {
                ShowCutout(PaperTexture.MakePaper(fallbackDrawing), true);
            }
            Solve();
        }

        public void ResetSocket()
        {
            StopAllCoroutines();
            foreach (var cutout in GetComponentsInChildren<Cutout>(true)) Destroy(cutout.gameObject);
            CurrentCutout = null;
            Solved = false;
            if (solutionCollider != null) solutionCollider.enabled = false;
            OnReset();
        }

        protected void Solve()
        {
            Solved = true;
            if (solutionCollider != null) solutionCollider.enabled = true;
            OnSolved();
        }

        protected void PlayCatReaction()
        {
            if (CurrentCutout != null) CurrentCutout.PlayCatReaction();
            Subtitles.Show(catLine);
        }

        /// <summary>Hands the current cutout to the caller; the socket stops tracking it.</summary>
        protected Cutout DetachCutout()
        {
            var cutout = CurrentCutout;
            CurrentCutout = null;
            return cutout;
        }

        protected void ShowCutout(Texture2D texture, bool ownsTexture)
        {
            if (CurrentCutout != null) Destroy(CurrentCutout.gameObject);
            CurrentCutout = CreateCutout(texture, ownsTexture);
        }

        protected virtual Cutout CreateCutout(Texture2D texture, bool ownsTexture) =>
            Cutout.Create(texture, ownsTexture, cutoutMaterial, anchor, span, faceCamera, transform);

        protected virtual void OnCaptured(CaptureResponse capture) { }
        protected abstract void OnSolved();
        protected virtual void OnWrongLabel(string label) { }
        protected virtual void OnReset() { }

        protected virtual void OnDrawGizmos()
        {
            var a = anchor != null ? anchor : transform;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(a.position + Vector3.up * span.y * 0.5f, Quaternion.Euler(0f, faceCamera ? 0f : a.eulerAngles.y, 0f), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(span.x, span.y, 0.05f));
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, 5f);
        }
    }
}
