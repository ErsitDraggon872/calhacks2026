using UnityEngine;

namespace CalHacks
{
    /// <summary>Trigger that becomes the respawn point when the explorer walks through it.</summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Transform respawnPoint;
        [SerializeField] Renderer flag;
        [SerializeField] Color activeColor = new Color(0.3f, 0.85f, 0.4f);

        Color idleColor;
        MaterialPropertyBlock block;

        public Vector3 RespawnPosition => (respawnPoint != null ? respawnPoint : transform).position;
        public Quaternion RespawnRotation => (respawnPoint != null ? respawnPoint : transform).rotation;

        void Awake()
        {
            if (flag != null && flag.sharedMaterial != null) idleColor = flag.sharedMaterial.GetColor(BaseColor);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null || RunManager.Instance == null) return;
            RunManager.Instance.ReachCheckpoint(this);
        }

        public void SetReached(bool reached)
        {
            if (flag == null) return;
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColor, reached ? activeColor : idleColor);
            flag.SetPropertyBlock(block);
        }
    }
}
