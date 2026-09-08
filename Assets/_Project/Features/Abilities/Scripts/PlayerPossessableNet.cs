using UnityEngine;
using Unity.Netcode;
using StarterAssets;

/// <summary>
/// SOLID — SRP: Enables any player investigator to be possessed by the Girl.
/// Synchronizes possession status across the network. When possessed, victim screen
/// blacks out with "Let me take the wheel for a sec☠️" and local controls are locked.
/// </summary>
public class PlayerPossessableNet : NetworkBehaviour, IPossessable
{
    [Header("Camera Target")]
    public Transform cameraTarget;

    [Header("Network State")]
    public NetworkVariable<bool> isPossessed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> possessingClientId = new NetworkVariable<ulong>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private CharacterController _characterController;
    private ThirdPersonController _thirdPersonController;
    private InvestigatorCombatNet _combatNet;
    private GirlPossession _activeGirlRef;

    public bool IsPossessed => isPossessed.Value;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _thirdPersonController = GetComponent<ThirdPersonController>();
        _combatNet = GetComponent<InvestigatorCombatNet>();
    }

    public override void OnNetworkSpawn()
    {
        isPossessed.OnValueChanged += HandlePossessionChanged;
    }

    public override void OnNetworkDespawn()
    {
        isPossessed.OnValueChanged -= HandlePossessionChanged;
    }

    private void HandlePossessionChanged(bool previous, bool current)
    {
        // If this client owns this investigator character, apply victim blackout and lock controls
        if (IsOwner)
        {
            if (current)
            {
                // Lock victim inputs
                if (_thirdPersonController != null) _thirdPersonController.enabled = false;
                if (_combatNet != null) _combatNet.enabled = false;

                if (PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(true);
                }
            }
            else
            {
                // Restore victim inputs
                if (_thirdPersonController != null) _thirdPersonController.enabled = true;
                if (_combatNet != null) _combatNet.enabled = true;

                if (PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(false);
                }
            }
        }
    }

    // =========================================================================
    //  IPossessable Implementation
    // =========================================================================

    public void Possess(GirlPossession girl)
    {
        _activeGirlRef = girl;
        if (IsServer)
        {
            isPossessed.Value = true;
            possessingClientId.Value = girl.OwnerClientId;
        }
    }

    public void Release()
    {
        if (IsServer)
        {
            isPossessed.Value = false;
            possessingClientId.Value = 0;
        }

        _activeGirlRef = null;
    }

    public void OnPossess(ulong clientId)
    {
        if (IsServer)
        {
            isPossessed.Value = true;
            possessingClientId.Value = clientId;
        }
    }

    public void OnRelease()
    {
        Release();
    }

    public Transform GetCameraTarget()
    {
        if (cameraTarget != null) return cameraTarget;
        Transform camRoot = transform.Find("PlayerCameraRoot");
        return camRoot != null ? camRoot : transform;
    }

    /// <summary>
    /// Forcibly ejects the possessing spirit (e.g. via Priest Exorcism).
    /// </summary>
    public void ForceExorcise()
    {
        if (_activeGirlRef != null)
        {
            _activeGirlRef.ForceEjectByExorcism();
        }
        Release();
    }
}
