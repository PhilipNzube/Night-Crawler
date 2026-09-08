using UnityEngine;
using System.Collections;
using Unity.Netcode;
using Unity.Cinemachine;
using StarterAssets;

/// <summary>
/// SOLID — SRP: Manages the Girl's possession ability, 5-minute total possession time pool,
/// target takeover, and forced ejection / time deduction when exorcised by the Cursed Priest.
/// </summary>
public class GirlPossession : NetworkBehaviour
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;
    public CinemachineCamera vcam;

    [Header("Possession Time Pool (5 Minutes Total)")]
    [Tooltip("Total pool of possession time in seconds across the match (default 300s = 5 minutes).")]
    public float maxPossessionTimePool = 300f;

    [Tooltip("Time deducted from her possession pool when exorcised by the Cursed Priest.")]
    public float exorcismPenaltySeconds = 30f;

    [Header("Network State")]
    public NetworkVariable<float> remainingPossessionTime = new NetworkVariable<float>(
        300f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isPossessing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("References")]
    private GirlMaterialController _matCtrl;
    private GirlStealth _stealthLogic;
    private ThirdPersonController _starterAssets;
    private CharacterController _controller;
    private Transform _girlCameraRoot;
    private IPossessable _currentTarget;

    public float RemainingPool => remainingPossessionTime.Value;
    public bool IsCurrentlyPossessing => isPossessing.Value;

    void Awake()
    {
        _matCtrl = GetComponent<GirlMaterialController>();
        _stealthLogic = GetComponent<GirlStealth>();
        _starterAssets = GetComponent<ThirdPersonController>();
        _controller = GetComponent<CharacterController>();
        _girlCameraRoot = transform.Find("PlayerCameraRoot");
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            remainingPossessionTime.Value = maxPossessionTimePool;
        }
    }

    void Update()
    {
        // Server drains possession pool while possessing
        if (IsServer && isPossessing.Value)
        {
            remainingPossessionTime.Value = Mathf.Max(0f, remainingPossessionTime.Value - Time.deltaTime);

            if (remainingPossessionTime.Value <= 0f)
            {
                // Time pool depleted -> auto eject
                EjectFromCurrentTarget();
            }
        }

        // ONLY the person controlling the Girl can trigger or release possession
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (!isPossessing.Value)
            {
                if (remainingPossessionTime.Value > 0f)
                {
                    TryPossess();
                }
                else
                {
                    if (NotificationManager.Instance != null)
                        NotificationManager.Instance.ShowNotification("Possession time bank depleted!", 2f);
                }
            }
            else
            {
                // Voluntarily release
                RequestReleaseServerRpc();
            }
        }
    }

    private void TryPossess()
    {
        Vector3 searchCenter = transform.position + Vector3.up;
        float range = stats != null ? stats.possessionRange : 5f;
        LayerMask mask = stats != null ? stats.possessionTargetLayer : ~0;
        Collider[] hits = Physics.OverlapSphere(searchCenter, range, mask);

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            NetworkObject targetNetObj = hit.GetComponentInParent<NetworkObject>();
            IPossessable target = hit.GetComponentInParent<IPossessable>();

            if (target != null && targetNetObj != null)
            {
                RequestPossessionServerRpc(targetNetObj.NetworkObjectId);
                StartCoroutine(PossessSequence(target));
                return;
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestPossessionServerRpc(ulong targetNetId)
    {
        isPossessing.Value = true;
        NotifyPossessionClientRpc(targetNetId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPossessionClientRpc(ulong targetNetId)
    {
        if (!IsOwner)
        {
            ToggleRenderers(false);
        }
    }

    private IEnumerator PossessSequence(IPossessable target)
    {
        _currentTarget = target;
        if (_matCtrl != null) _matCtrl.SetManifested(false);
        yield return new WaitForSeconds(0.2f);

        if (vcam != null)
        {
            vcam.Follow = target.GetCameraTarget();
            vcam.LookAt = target.GetCameraTarget();
        }

        target.Possess(this);

        _controller.enabled = false;
        if (_starterAssets != null) _starterAssets.enabled = false;
        ToggleRenderers(false);
    }

    [Rpc(SendTo.Server)]
    private void RequestReleaseServerRpc()
    {
        EjectFromCurrentTarget();
    }

    /// <summary>
    /// Forces the Girl out of the possessed body and penalizes her remaining possession pool.
    /// Called when the Cursed Priest casts Exorcism!
    /// </summary>
    public void ForceEjectByExorcism()
    {
        if (IsServer)
        {
            remainingPossessionTime.Value = Mathf.Max(0f, remainingPossessionTime.Value - exorcismPenaltySeconds);
            Debug.Log($"[GirlPossession] Exorcised by Priest! Deducted {exorcismPenaltySeconds}s. Remaining: {remainingPossessionTime.Value}s");
            EjectFromCurrentTarget();
            NotifyExorcisedClientRpc(exorcismPenaltySeconds);
        }
        else
        {
            ForceEjectServerRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void ForceEjectServerRpc()
    {
        ForceEjectByExorcism();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyExorcisedClientRpc(float penalty)
    {
        if (IsOwner && NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"EXORCISED BY PRIEST! Lost {penalty:0}s possession time!", 4f);
        }
    }

    private void EjectFromCurrentTarget()
    {
        if (!isPossessing.Value) return;
        isPossessing.Value = false;

        Vector3 returnPos = transform.position;
        if (_currentTarget != null)
        {
            Transform t = _currentTarget.GetCameraTarget();
            if (t != null) returnPos = t.position + Vector3.back * 1.5f;
            _currentTarget.Release();
            _currentTarget = null;
        }

        ReturnToSpiritFormClientRpc(returnPos);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ReturnToSpiritFormClientRpc(Vector3 returnPosition)
    {
        transform.position = returnPosition;
        ToggleRenderers(true);
        _controller.enabled = true;

        if (vcam != null)
        {
            vcam.Follow = _girlCameraRoot;
            vcam.LookAt = _girlCameraRoot;
            vcam.OnTargetObjectWarped(_girlCameraRoot, Vector3.zero);
        }

        if (_starterAssets != null) _starterAssets.enabled = true;

        if (_matCtrl != null)
        {
            _matCtrl.SetManifested(false);
        }
    }

    public void ReturnFromMonster(Vector3 monsterPosition)
    {
        ReturnToSpiritFormClientRpc(monsterPosition);
    }

    private void ToggleRenderers(bool isVisible)
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
    }
}