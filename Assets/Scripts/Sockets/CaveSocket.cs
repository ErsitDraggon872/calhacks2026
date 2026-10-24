using System.Collections;
using UnityEngine;

namespace CalHacks
{
    /// <summary>Dark cave: a "light" drawing becomes a light source, lifts the darkness and opens the entrance.</summary>
    public class CaveSocket : PuzzleSocket
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Renderer darkness;
        [Tooltip("Solid collider at the entrance that stops the explorer until the cave is lit.")]
        [SerializeField] Collider blocker;
        [SerializeField] Color lightColor = new Color(1f, 0.85f, 0.55f);
        [SerializeField] float lightRange = 14f;
        [SerializeField] float lightIntensity = 6f;
        [SerializeField] string wrongLine = "That doesn't help. It's still too dark in there.";

        Material darknessMaterial;
        Color darknessColor;
        Light spawnedLight;

        public override SocketType Type => SocketType.Light;

        protected override void Awake()
        {
            base.Awake();
            if (darkness != null)
            {
                darknessMaterial = darkness.material;
                darknessColor = darknessMaterial.GetColor(BaseColor);
            }
        }

        protected override void OnSolved()
        {
            if (blocker != null) blocker.enabled = false;

            var lightGo = new GameObject("CutoutLight");
            lightGo.transform.SetParent(transform, false);
            var source = CurrentCutout != null ? CurrentCutout.transform.position : anchor.position + Vector3.up;
            lightGo.transform.position = source + Vector3.up * 0.5f;
            spawnedLight = lightGo.AddComponent<Light>();
            spawnedLight.type = LightType.Point;
            spawnedLight.color = lightColor;
            spawnedLight.range = lightRange;
            spawnedLight.intensity = lightIntensity;
            spawnedLight.shadows = LightShadows.None;

            if (darkness != null) StartCoroutine(FadeDarkness());
        }

        IEnumerator FadeDarkness()
        {
            const float duration = 1.2f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var c = darknessColor;
                c.a = Mathf.Lerp(darknessColor.a, 0f, t / duration);
                darknessMaterial.SetColor(BaseColor, c);
                yield return null;
            }
            darkness.gameObject.SetActive(false);
        }

        protected override void OnWrongLabel(string label) => Subtitles.Show(wrongLine);

        protected override void OnReset()
        {
            if (blocker != null) blocker.enabled = true;
            if (spawnedLight != null) Destroy(spawnedLight.gameObject);
            spawnedLight = null;
            if (darkness != null)
            {
                darkness.gameObject.SetActive(true);
                darknessMaterial.SetColor(BaseColor, darknessColor);
            }
        }

        void OnDestroy()
        {
            if (darknessMaterial != null) Destroy(darknessMaterial);
        }
    }
}
