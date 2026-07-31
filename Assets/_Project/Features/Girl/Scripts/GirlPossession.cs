using UnityEngine;
using System.Collections;
using Unity.Netcode; // Essential for Multiplayer
using Unity.Cinemachine;
using StarterAssets;

public class GirlPossession : NetworkBehaviour
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;
    
    public CinemachineCamera vcam;

    [Header("References")]
    private GirlMaterialController _matCtrl;
    private GirlStealth _stealthLogic;
    private ThirdPersonController _starterAssets;
    private CharacterController _controller;
    private Transform _girlCameraRoot;

    void Awake()
    {
        _matCtrl = GetComponent<GirlMaterialController>();
        _stealthLogic = GetComponent<GirlStealth>();
        _starterAssets = GetComponent<ThirdPersonController>();
        _controller = GetComponent<CharacterController>();
        _girlCameraRoot = transform.Find("PlayerCameraRoot");
    }

    void Update()
    {
        // ONLY the person controlling the Girl can trigger possession
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            TryPossess();
        }
    }

    private void TryPossess()
    {
        Vector3 searchCenter = transform.position + Vector3.up;
        Collider[] hits = Physics.OverlapSphere(searchCenter, stats.possessionRange, stats.possessionTargetLayer);

        foreach (var hit in hits)
        {
            // We find the Monster's NetworkObject to identify it across the net
            NetworkObject monsterNetObj = hit.GetComponentInParent<NetworkObject>();
            IPossessable target = hit.GetComponentInParent<IPossessable>();
            
            if (target != null && monsterNetObj != null)
            {
                // Tell the Server we want to possess this specific Monster ID
                RequestPossessionServerRpc(monsterNetObj.NetworkObjectId);
                StartCoroutine(PossessSequence(target));
                return;
            }
        }
    }

    [ServerRpc]
    private void RequestPossessionServerRpc(ulong monsterId)
    {
        // The Server tells everyone else to hide the Girl
        NotifyPossessionClientRpc(monsterId);
    }

    [ClientRpc]
    private void NotifyPossessionClientRpc(ulong monsterId)
    {
        // If I'm NOT the owner, I just hide the Girl model
        if (!IsOwner)
        {
            if (_matCtrl != null) _matCtrl.RequestAlpha(0f, 0.3f);
            // We don't disable the object on other screens to keep the NetworkObject alive
            // We just hide the visuals
            ToggleRenderers(false);
        }
    }

    private IEnumerator PossessSequence(IPossessable target)
    {
        if (_matCtrl != null) _matCtrl.RequestAlpha(0f, 0.3f);
        yield return new WaitForSeconds(0.3f);

        if (vcam != null)
        {
            vcam.Follow = target.GetCameraTarget();
            vcam.LookAt = target.GetCameraTarget();
        }

        target.Possess(this);

        // Instead of SetActive(false), we disable components to keep the NetworkObject active
        _controller.enabled = false;
        _starterAssets.enabled = false;
        ToggleRenderers(false);
    }

    public void ReturnFromMonster(Vector3 monsterPosition)
    {
        ToggleRenderers(true);
        transform.position = monsterPosition;
        _controller.enabled = true;

        float targetAlpha = 1.0f;
        if (_stealthLogic != null && _stealthLogic.IsStealthActive.Value && stats != null) // Check the NetworkVariable
        {
            targetAlpha = stats.stealthAlpha;
        }

        if (_matCtrl != null)
        {
            _matCtrl.SetAlphaInstant(0f);
            _matCtrl.RequestAlpha(targetAlpha, 0.5f);
        }

        if (vcam != null)
        {
            vcam.Follow = _girlCameraRoot;
            vcam.LookAt = _girlCameraRoot;
            vcam.OnTargetObjectWarped(_girlCameraRoot, Vector3.zero);
        }

        _starterAssets.enabled = true;
        transform.rotation = Quaternion.Euler(0, transform.rotation.eulerAngles.y, 0);
        
        // Tell the server we are back so it can un-hide us for others
        RequestReturnServerRpc();
    }

    [ServerRpc]
    private void RequestReturnServerRpc() => NotifyReturnClientRpc();

    [ClientRpc]
    private void NotifyReturnClientRpc()
    {
        if (!IsOwner) ToggleRenderers(true);
    }

    private void ToggleRenderers(bool isVisible)
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
    }
}