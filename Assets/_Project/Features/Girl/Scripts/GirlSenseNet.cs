using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class GirlSenseNet : NetworkBehaviour
{
    [Header("Detection Settings")]
    public float senseRadius = 50f;
    public float cooldown = 15f;
    public float highlightDuration = 3f;

    private float _nextSenseTime = 0f;

    void Update()
    {
        if (!IsOwner) return;

        // Press 'V' to activate "Shadow Sense"
        if (Input.GetKeyDown(KeyCode.V) && Time.time >= _nextSenseTime)
        {
            _nextSenseTime = Time.time + cooldown;
            RequestSenseServerRpc();
        }
    }

    [ServerRpc]
    private void RequestSenseServerRpc()
    {
        // 1. Find all Explorers in the radius
        Collider[] victims = Physics.OverlapSphere(transform.position, senseRadius);
        List<ulong> victimNetIds = new List<ulong>();

        foreach (var v in victims)
        {
            // Only detect "Explorer" objects
            if (v.CompareTag("Player") && v.name.Contains("Explorer"))
            {
                NetworkObject netObj = v.GetComponent<NetworkObject>();
                if (netObj != null) victimNetIds.Add(netObj.NetworkObjectId);
            }
        }

        // 2. Tell the Girl (this client) where they are!
        HighlightSurvivorsClientRpc(victimNetIds.ToArray());
    }

    [ClientRpc]
    private void HighlightSurvivorsClientRpc(ulong[] victimIds)
    {
        if (!IsOwner) return; // Only the Girl sees the pings!

        Debug.Log($"[GIRL SENSE] Detected {victimIds.Length} Survivors!");

        foreach (ulong id in victimIds)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject victim))
            {
                StartCoroutine(PingSurvivor(victim.transform));
            }
        }
    }

    private IEnumerator<WaitForSeconds> PingSurvivor(Transform survivor)
    {
        // Simple visual feedback: Spawn a "Shadow Flare" above them
        GameObject ping = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ping.transform.position = survivor.position + Vector3.up * 3f;
        ping.transform.localScale = Vector3.one * 0.5f;
        
        // Make it look spooky
        Renderer r = ping.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Unlit/Color"));
        r.material.color = Color.black;

        // Destroy after duration
        Object.Destroy(ping, highlightDuration);

        yield return new WaitForSeconds(highlightDuration);
    }
}
