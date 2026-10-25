using System;
using System.Collections.Generic;
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
    /// level geometry switched on by solving, never a stand-in for the drawing. What solves it is a drawing's attributes
    /// (<see cref="SolvingTraits"/>), with <see cref="acceptedLabels"/> as a fallback by name.
    /// </summary>
    public abstract class PuzzleSocket : MonoBehaviour
    {
        [SerializeField] protected string socketId = "socket";
        [Tooltip("Names that solve this socket, besides its solving traits. Sent to the service as accepted_labels.")]
        [SerializeField] protected string[] acceptedLabels = Array.Empty<string>();
        [Tooltip("How close (m, on the ground plane) B must be for a BUILD to go to this socket. 0 = GameConfig.castRange.")]
        [SerializeField] protected float castRange;
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
        /// <summary>Traits that solve this socket, any one of them: anything edible feeds the villager.</summary>
        public virtual IReadOnlyList<Trait> SolvingTraits => Array.Empty<Trait>();
        /// <summary>The puzzle in words, sent with the drawing as POST /interpret's tie-breaker (never what decides it).</summary>
        public virtual string Goal => "";
        /// <summary>The note floating over this socket while B is in range, e.g. "draw a way across". "" for none.</summary>
        public virtual string Banner => "";
        public bool Solved { get; private set; }
        public abstract SocketType Type { get; }
        public Vector3 CastPoint => transform.position;
        /// <summary>This socket's own BUILD range, or 0 for the default.</summary>
        public float CastRange => castRange;

        protected Cutout CurrentCutout { get; private set; }
        /// <summary>The attributes of the last drawing given to this socket, or null.</summary>
        protected InventionSpec LastSpec { get; private set; }

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

        public void ApplyLabel(string label) => ApplySpec(InventionSpec.FromLabel(label));

        /// <summary>The drawing's attributes arrived: solve if they fit, react otherwise. Returns what they did here.</summary>
        public LabelOutcome ApplySpec(InventionSpec spec)
        {
            spec = spec ?? InventionSpec.Plain();
            LastSpec = spec;
            OnSpec(spec);
            var outcome = SocketLogic.Resolve(spec, SolvingTraits, acceptedLabels);
            if (Solved)
            {
                if (outcome == LabelOutcome.CatReaction) PlayCatReaction();
                return outcome;
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
                    OnWrongLabel(spec.Name);
                    break;
            }
            return outcome;
        }

        /// <summary>What the drawing did here, in plain words for the card: "FOOD → THE GATEKEEPER LETS YOU THROUGH".</summary>
        public virtual string Verdict(InventionSpec spec, LabelOutcome outcome) =>
            outcome == LabelOutcome.Solve ? "THAT DID IT!" : "NOT QUITE. TRY SOMETHING ELSE";

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
            LastSpec = null;
            Solved = false;
            if (solutionCollider != null) solutionCollider.enabled = false;
            OnReset();
        }

        protected void Solve()
        {
            Solved = true;
            if (solutionCollider != null) solutionCollider.enabled = true;
            Juice.PlayAfterCard(JuiceSound.Ding);
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
            if (CurrentCutout != null) Juice.Appear(CurrentCutout.transform.position, CurrentCutout.Size);
        }

        protected virtual Cutout CreateCutout(Texture2D texture, bool ownsTexture) =>
            Cutout.Create(texture, ownsTexture, cutoutMaterial, anchor, span, faceCamera, transform);

        protected virtual void OnCaptured(CaptureResponse capture) { }
        /// <summary>Called with every drawing's attributes, solved or not, before the solve check.</summary>
        protected virtual void OnSpec(InventionSpec spec) { }
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
