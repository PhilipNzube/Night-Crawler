using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections;

public class GirlPuppetNet : NetworkBehaviour
{
    [Header("Settings")]
    public float possessionDuration = 20f;
    public float interactionRange = 3f;
    public LayerMask deadExplorerLayer;

    [Header("State")]
    private NetworkVariable<bool> _isInPuppetMode = new NetworkVariable<bool>(false);
    private NetworkVariable<float> _rotTimer = new NetworkVariable<float>(0f);
    private TargetHealth _occupiedCorpse;

    [Header("References")]
    public GameObject girlVisuals; 
    public TextMeshProUGUI rotTextSlot; // Drag your HUD text here
    private CharacterController _cc;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (rotTextSlot != null) rotTextSlot.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!IsOwner) return;

        if (!_isInPuppetMode.Value)
        {
            // 1. Try to enter a body (E Key)
            if (Keyboard.current.eKey.wasPressedThisFrame)
            {
                TryPossessCorpse();
            }
        }
        else
        {
            // 2. We are a Puppet! Update timer and check for Eruption
            UpdatePuppetState();

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                RequestEruptionServerRpc();
            }
        }
    }

    private void TryPossessCorpse()
    {
        Collider[] corpses = Physics.OverlapSphere(transform.position, interactionRange, deadExplorerLayer);
        foreach (var c in corpses)
        {
            if (c.TryGetComponent<TargetHealth>(out TargetHealth target))
            {
                if (target.isCorpse.Value && !target.isOccupied.Value)
                {
                    RequestPossessionServerRpc(target.NetworkObject.NetworkObjectId);
                    break;
                }
            }
        }
    }

    private void UpdatePuppetState()
    {
        if (_occupiedCorpse != null)
        {
            transform.position = _occupiedCorpse.transform.position;
            transform.rotation = _occupiedCorpse.transform.rotation;

            // Updated the HUD Rot Meter
            if (rotTextSlot != null)
            {
                rotTextSlot.text = "CORPSE DECAY: " + Mathf.Ceil(_rotTimer.Value).ToString() + "s";
            }
        }
    }

    [ServerRpc]
    private void RequestPossessionServerRpc(ulong corpseId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(corpseId, out NetworkObject corpseNet))
        {
            TargetHealth target = corpseNet.GetComponent<TargetHealth>();
            target.OnPossessed(); // Lock the body from others

            _occupiedCorpse = target;
            _isInPuppetMode.Value = true;
            _rotTimer.Value = possessionDuration;

            StartCoroutine(PuppetTimerRoutine());
            NotifyPuppetStateClientRpc(true, corpseId);
            
            Debug.Log($"[SERVER] Girl is now piloting Corpse {corpseId}");
        }
    }

    [ClientRpc]
    private void NotifyPuppetStateClientRpc(bool isHiding, ulong corpseId)
    {
        // 1. Hide/Show the Girl
        if (girlVisuals != null) girlVisuals.SetActive(!isHiding);
        
        // Hide/Show the HUD text for the Demon player
        if (IsOwner && rotTextSlot != null) rotTextSlot.gameObject.SetActive(isHiding);

        // Disable own Collision while in Puppet
        if (_cc != null) _cc.enabled = !isHiding;

        // 2. Identify the body we are inhabiting
        if (isHiding && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(corpseId, out NetworkObject corpseNet))
        {
            _occupiedCorpse = corpseNet.GetComponent<TargetHealth>();
        }
    }

    [ServerRpc]
    private void RequestEruptionServerRpc()
    {
        // Perform an AOE Shadow Burst!
        PerformEruptionLogic();

        _isInPuppetMode.Value = false;
        NotifyPuppetStateClientRpc(false, 0);
        
        // Destroy the corpse shell
        if (_occupiedCorpse != null)
        {
            _occupiedCorpse.GetComponent<NetworkObject>().Despawn();
        }
    }

    private void PerformEruptionLogic()
    {
        // Spawn the "Bitch Black" Void VFX at the corpse position
        GameObject voidObj = new GameObject("ShadowVoid");
        voidObj.transform.position = transform.position;
        voidObj.AddComponent<ShadowVoidFX>(); // We will create this next!
        
        // Damage logic for anyone nearby
        Collider[] victims = Physics.OverlapSphere(transform.position, 5f);
        foreach (var v in victims)
        {
            if (v.gameObject == gameObject) continue;
            if (v.TryGetComponent<TargetHealth>(out TargetHealth h))
            {
                h.TakeDamage(50f); // High ambush damage!
            }
        }
    }

    private IEnumerator PuppetTimerRoutine()
    {
        while (_rotTimer.Value > 0 && _isInPuppetMode.Value)
        {
            _rotTimer.Value -= Time.deltaTime;
            yield return null;
        }

        if (_isInPuppetMode.Value)
        {
            RequestEruptionServerRpc(); // Auto-erupt if time runs out
        }
    }
}
