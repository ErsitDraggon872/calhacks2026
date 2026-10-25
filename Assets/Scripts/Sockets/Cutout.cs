using System.Collections;
using UnityEngine;

namespace CalHacks
{
    /// <summary>A drawing shown in the world as an upright paper quad. Visual only: it never has a collider.</summary>
    public class Cutout : MonoBehaviour
    {
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int MainTex = Shader.PropertyToID("_MainTex");

        Texture2D texture;
        bool ownsTexture;
        Material material;
        Vector3 fullScale;
        float facingSign = 1f;

        /// <summary>
        /// Mirror the PAPER ART only. Colliders and the root do not rotate, so the
        /// creature still walks normally on the ground when it turns around.
        /// Remember facing while the pop-in animation is changing the scale.
        /// </summary>
        public void FaceHorizontal(float horizontal)
        {
            if (Mathf.Abs(horizontal) < 0.01f) return;
            float next = horizontal < 0f ? -1f : 1f;
            if (Mathf.Approximately(next, facingSign)) return;
            facingSign = next;
            var scale = transform.localScale;
            transform.localScale = new Vector3(Mathf.Abs(scale.x) * facingSign, scale.y, scale.z);
        }

        public float FacingSign => facingSign;

        Vector3 FacingScale(float multiplier = 1f) =>
            new Vector3(fullScale.x * facingSign * multiplier,
                fullScale.y * multiplier, fullScale.z * multiplier);

        public Vector2 Size { get; private set; }
        public Texture2D Texture => texture;

        /// <summary>
        /// Builds a cutout standing on <paramref name="anchor"/>, fitted inside <paramref name="span"/> (width, height)
        /// with the drawing's aspect ratio kept. Faces the camera's yaw when <paramref name="faceCamera"/> is set,
        /// otherwise the anchor's forward.
        /// </summary>
        public static Cutout Create(Texture2D texture, bool ownsTexture, Material baseMaterial, Transform anchor,
            Vector2 span, bool faceCamera, Transform parent)
        {
            var size = InventionPlacement.FitInside(texture.width, texture.height, span);
            var yaw = anchor.eulerAngles.y;
            if (faceCamera && Camera.main != null) yaw = Camera.main.transform.eulerAngles.y;
            return Create(texture, ownsTexture, baseMaterial, anchor.position, yaw, size, parent);
        }

        /// <summary>Builds a cutout of exactly <paramref name="size"/> (stretching the drawing if needed) standing on <paramref name="bottomCentre"/>.</summary>
        public static Cutout Create(Texture2D texture, bool ownsTexture, Material baseMaterial, Vector3 bottomCentre,
            float yaw, Vector2 size, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Cutout";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);

            var cutout = go.AddComponent<Cutout>();
            cutout.texture = texture;
            cutout.ownsTexture = ownsTexture;
            cutout.material = new Material(baseMaterial);
            cutout.material.SetTexture(BaseMap, texture);
            cutout.material.SetTexture(MainTex, texture);
            go.GetComponent<MeshRenderer>().sharedMaterial = cutout.material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            cutout.Size = size;
            go.transform.SetPositionAndRotation(bottomCentre + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, yaw, 0f));
            cutout.fullScale = new Vector3(size.x, size.y, 1f);
            go.transform.localScale = Vector3.zero;
            cutout.StartCoroutine(cutout.PopIn());
            return cutout;
        }

        IEnumerator PopIn()
        {
            const float duration = 0.35f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = t / duration;
                // Ease-out-back for a little paper "pop".
                var s = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);
                transform.localScale = FacingScale(s);
                yield return null;
            }
            transform.localScale = FacingScale();
        }

        /// <summary>Stomped but alive: flatten like paper, then spring back.</summary>
        public void PlaySquish() => StartCoroutine(Squish());

        IEnumerator Squish()
        {
            const float duration = 0.4f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                // Damped wobble: flat and wide first, a small overshoot, then rest.
                var w = Mathf.Cos(t / duration * Mathf.PI * 2.5f) * (1f - t / duration);
                transform.localScale = new Vector3(fullScale.x * facingSign * (1f + .3f * w),
                    fullScale.y * (1f - .45f * w), fullScale.z);
                yield return null;
            }
            transform.localScale = FacingScale();
        }

        public void PlayCatReaction() => StartCoroutine(Hop());

        IEnumerator Hop()
        {
            var start = transform.position;
            var startRot = transform.rotation;
            const float duration = 0.8f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = t / duration;
                transform.position = start + Vector3.up * (Mathf.Sin(k * Mathf.PI * 2f) * 0.25f + Mathf.Sin(k * Mathf.PI) * 0.6f);
                transform.rotation = startRot * Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 4f) * 12f);
                yield return null;
            }
            transform.SetPositionAndRotation(start, startRot);
        }

        /// <summary>Slides the cutout to a point while shrinking, then destroys it (the villager "takes" it).</summary>
        public void HandTo(Vector3 target, float duration = 0.6f)
        {
            StopAllCoroutines();
            transform.localScale = FacingScale();
            StartCoroutine(Hand(target, duration));
        }

        IEnumerator Hand(Vector3 target, float duration)
        {
            var start = transform.position;
            var startScale = transform.localScale;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = t / duration;
                transform.position = Vector3.Lerp(start, target, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.5f;
                transform.localScale = Vector3.Lerp(startScale, startScale * 0.3f, k);
                yield return null;
            }
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (ownsTexture && texture != null) Destroy(texture);
        }
    }
}
