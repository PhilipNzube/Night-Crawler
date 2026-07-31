using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class GirlShadowTeleportNet : NetworkBehaviour
{
    // SOLID: Drag your "Teleport_Standard" Scriptable Object here
    public AbilityData teleportData; 
    public LayerMask explorerLayer;
    private GirlStealth _stealth;

    void Awake()
    {
        _stealth = GetComponent<GirlStealth>();
    }

    void Update()
    {
        if (!IsOwner) return;
        if (Keyboard.current.tKey.wasPressedThisFrame) TryAutoTeleport();
    }

    private void TryAutoTeleport()
    {
        // ABILITY LOCK: Cannot teleport while invisible!
        if (_stealth != null && _stealth.IsStealthActive.Value)
        {
            Debug.Log("[ABILITIES] You cannot teleport while invisible! Turn off stealth first.");
            return;
        }

        // Use the DATA from the Scriptable Object
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, teleportData.range, explorerLayer);
        
        Transform target = FindClosest(hitColliders);

        if (target != null)
        {
            Vector3 targetPos = target.position - (target.forward * teleportData.offset);
            targetPos.y = target.position.y;
            TeleportServerRpc(targetPos, target.rotation);
        }
    }

    private Transform FindClosest(Collider[] hits) 
    { 
        Transform closest = null;
        float minDistance = Mathf.Infinity;
        Vector3 currentPos = transform.position;
        foreach (Collider hit in hits)
        {
            float dist = Vector3.Distance(hit.transform.position, currentPos);
            if (dist < minDistance)
            {
                closest = hit.transform;
                minDistance = dist;
            }
        }
        return closest;
    }

    [ServerRpc]
    private void TeleportServerRpc(Vector3 pos, Quaternion rot) 
    { 
        TeleportClientRpc(pos, rot);
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 pos, Quaternion rot)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        
        transform.position = pos;
        transform.rotation = rot;
        
        if (cc != null) cc.enabled = true;
    }
}