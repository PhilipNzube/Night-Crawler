using UnityEngine;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// SOLID — SRP: Marks a valid world-space location where the Vengeful Spirit (Girl)
    /// can awaken/rise monsters from the dead.
    /// Place these in dark tunnels, collapsed shafts, or bone piles across GameScene.
    /// </summary>
    public class DeadSpawnPoint : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("Optional identifier or description for this spawn point in the mine.")]
        public string locationName = "Mine Shaft";

        [Tooltip("Weight/priority for random selection (higher = more frequent).")]
        public float weight = 1.0f;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.85f, 0.15f, 0.15f, 0.75f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.6f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, transform.forward * 1.2f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.5f, 0.7f);
        }
    }
}
