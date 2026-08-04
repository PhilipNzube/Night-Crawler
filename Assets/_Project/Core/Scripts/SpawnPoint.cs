using UnityEngine;

public enum SpawnPointType
{
    Investigator,
    VengefulSpirit
}

/// <summary>
/// Place this component on an empty GameObject in the Game Scene to mark it as a spawn location.
/// Automatically discovered by GameManager when the Game Scene loads if not assigned manually in Inspector.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Header("Spawn Type")]
    public SpawnPointType spawnType = SpawnPointType.Investigator;

    [Header("Gizmo Visualization")]
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0.5f, 0.8f);
    [SerializeField] private float gizmoRadius = 0.5f;

    private void OnDrawGizmos()
    {
        Gizmos.color = (spawnType == SpawnPointType.VengefulSpirit) ? Color.purple : gizmoColor;
        Gizmos.DrawSphere(transform.position, gizmoRadius);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius * 1.5f);

        // Draw a small forward arrow so you can see orientation
        Gizmos.color = Color.white;
        Gizmos.DrawRay(transform.position, transform.forward * gizmoRadius * 2f);
    }
}
