using System.Collections;
using UnityEngine;

namespace CalHacks
{
    /// <summary>A villager blocks the path. A "fish" drawing is taken, a line is said, and the villager steps aside.</summary>
    public class VillagerSocket : PuzzleSocket
    {
        [SerializeField] Transform villager;
        [SerializeField] Transform stepAsidePoint;
        [SerializeField] string acceptLine = "A fish! Just what I was craving. Go on through, friend!";
        [SerializeField] string rejectLine = "Hmm... I can't eat that.";

        Vector3 homePosition;
        Quaternion homeRotation;

        public override SocketType Type => SocketType.Villager;

        protected override void Awake()
        {
            base.Awake();
            if (villager != null)
            {
                homePosition = villager.position;
                homeRotation = villager.rotation;
            }
        }

        protected override void OnSolved()
        {
            Subtitles.Show(acceptLine);
            StartCoroutine(TakeAndStepAside(DetachCutout()));
        }

        IEnumerator TakeAndStepAside(Cutout cutout)
        {
            yield return new WaitForSeconds(0.5f);
            if (cutout != null && villager != null) cutout.HandTo(villager.position + Vector3.up * 1.2f);
            yield return new WaitForSeconds(0.7f);
            if (villager == null || stepAsidePoint == null) yield break;

            var start = villager.position;
            var target = stepAsidePoint.position;
            const float duration = 1f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / duration);
                villager.position = Vector3.Lerp(start, target, k) + Vector3.up * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f)) * 0.15f;
                yield return null;
            }
            villager.position = target;
        }

        protected override void OnWrongLabel(string label) => Subtitles.Show(rejectLine);

        protected override void OnReset()
        {
            if (villager != null) villager.SetPositionAndRotation(homePosition, homeRotation);
        }
    }
}
