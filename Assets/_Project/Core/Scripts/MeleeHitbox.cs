using UnityEngine;

public class MeleeHitbox : MonoBehaviour
{
    public EntityStats stats; // Drag MonsterStats here

    private void OnTriggerEnter(Collider other)
    {
        if (stats == null) return;

        // Use bitwise check to see if the object hit is on the 'Hittable' layer
        if (((1 << other.gameObject.layer) & stats.attackTargetLayer) != 0)
        {
            if (other.TryGetComponent<TargetHealth>(out TargetHealth health))
            {
                if (stats != null)
                {
                    health.TakeDamage(stats.damageAmount, false); // Monsters do PHYSICAL damage
                }
            }
        }
    }
}