using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// A villager blocks the path. Anything edible (a fish, an apple, a pizza) is taken, a line is said, and the villager
    /// steps aside; anything hostile scares them aside instead. If there's a <see cref="gate"/>, it rises once the
    /// villager is out of the way (the gatekeeper). With a <see cref="bubble"/>, the villager speaks in it instead of
    /// the subtitles, and the top sign is left alone.
    /// </summary>
    public class VillagerSocket : PuzzleSocket
    {
        static readonly Trait[] Wants = { Trait.Edible, Trait.Hostile };

        [SerializeField] Transform villager;
        [SerializeField] Transform stepAsidePoint;
        [Tooltip("Optional gate (with its collider) that rises once the villager steps aside.")]
        [SerializeField] Transform gate;
        [Tooltip("How far (m) the gate rises.")]
        [SerializeField] float gateRise = 4f;
        [Tooltip("Speech bubble over the villager's head for their lines. Optional: without it they use the subtitles.")]
        [SerializeField] SpeechBubble bubble;
        [SerializeField] string acceptLine = "Food! Just what I was craving. Go on through, friend!";
        [SerializeField] string scaredLine = "Yikes! Keep that thing away from me!";
        [SerializeField] string rejectLine = "Hmm... I can't eat that.";

        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 gateHome;

        public override SocketType Type => SocketType.Villager;
        public override IReadOnlyList<Trait> SolvingTraits => Wants;
        public override string Goal => $"something the {socketId} will accept";
        public override string Banner => bubble != null ? "" : $"the {socketId} is hungry  ·  draw something to eat";

        public override string Verdict(InventionSpec spec, LabelOutcome outcome)
        {
            var who = "THE " + socketId.ToUpperInvariant();
            if (outcome != LabelOutcome.Solve) return $"NOT FOOD → {who} SAYS NO";
            return spec.Has(Trait.Edible) ? $"FOOD → {who} LETS YOU THROUGH" : $"SCARY → {who} RUNS OFF";
        }

        protected override void Awake()
        {
            base.Awake();
            if (villager != null)
            {
                homePosition = villager.position;
                homeRotation = villager.rotation;
            }
            if (gate != null) gateHome = gate.position;
        }

        protected override void OnSolved()
        {
            // Food wins over fright: a hostile but edible drawing (a shark) is still dinner.
            var scared = LastSpec != null && LastSpec.Has(Trait.Hostile) && !LastSpec.Has(Trait.Edible);
            Say(scared ? scaredLine : acceptLine);
            StartCoroutine(StepAsideAndOpen(scared ? null : DetachCutout(), scared ? 0.6f : 1f));
        }

        IEnumerator StepAsideAndOpen(Cutout food, float duration)
        {
            yield return new WaitForSeconds(0.5f);
            if (food != null && villager != null)
            {
                food.HandTo(villager.position + Vector3.up * 1.2f);
                yield return new WaitForSeconds(0.7f);
            }
            if (villager != null && stepAsidePoint != null)
            {
                var start = villager.position;
                var target = stepAsidePoint.position;
                for (var t = 0f; t < duration; t += Time.deltaTime)
                {
                    var k = Mathf.SmoothStep(0f, 1f, t / duration);
                    villager.position = Vector3.Lerp(start, target, k) + Vector3.up * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f)) * 0.15f;
                    yield return null;
                }
                villager.position = target;
            }
            if (gate == null) yield break;

            const float gateTime = 1.2f;
            for (var t = 0f; t < gateTime; t += Time.deltaTime)
            {
                gate.position = gateHome + Vector3.up * gateRise * Mathf.SmoothStep(0f, 1f, t / gateTime);
                yield return null;
            }
            gate.position = gateHome + Vector3.up * gateRise;
        }

        protected override void OnWrongLabel(string label) => Say(rejectLine);

        void Say(string line)
        {
            if (bubble != null) bubble.Say(line);
            else Subtitles.Show(line);
        }

        protected override void OnReset()
        {
            if (bubble != null) bubble.Clear();
            if (villager != null) villager.SetPositionAndRotation(homePosition, homeRotation);
            if (gate != null) gate.position = gateHome;
        }
    }
}
