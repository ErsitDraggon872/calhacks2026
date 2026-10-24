using UnityEngine;

namespace CalHacks
{
    /// <summary>Reaching this trigger ends the run and shows the completion time.</summary>
    [RequireComponent(typeof(Collider))]
    public class ExitZone : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null || RunManager.Instance == null) return;
            RunManager.Instance.Win();
        }
    }
}
