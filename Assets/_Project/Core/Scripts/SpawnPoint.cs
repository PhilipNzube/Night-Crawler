using UnityEngine;

/// <summary>
/// Place this component on an empty GameObject in the scene to mark it as a spawn location.
/// Drag the GameObject into the matching list on GameManager (girlSpawnPoints or explorerSpawnPoints).
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("Draw a colored gizmo so you can see the spawn point in the Scene view.")]
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0.5f, 0.8f);
    [SerializeField] private float gizmoRadius = 0.5f;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, gizmoRadius);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius * 1.5f);

        // Draw a small forward arrow so you can see orientation
        Gizmos.color = Color.white;
        Gizmos.DrawRay(transform.position, transform.forward * gizmoRadius * 2f);
    }
}
