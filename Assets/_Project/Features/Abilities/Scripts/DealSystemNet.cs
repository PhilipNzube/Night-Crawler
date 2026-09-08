using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Manages dark deals and pacts between the Vengeful Spirit (Girl) and Investigators.
/// The Girl selects an active player, configures or chooses a deal on her screen, and dispatches it.
/// The recipient sees the exact deal terms on their screen.
/// Upon acceptance:
/// - Recipient is granted a weapon (if not already holding one).
/// - A private chilling laughter audio plays strictly on the recipient's headphones/speakers.
/// - The Girl receives notification of pact sealed.
/// </summary>
public class DealSystemNet : NetworkBehaviour
{
    public static DealSystemNet Instance { get; private set; }

    [Header("Audio (Sinister Laughter)")]
    [Tooltip("Audio clip of sinister laughter played privately to the player who accepts the deal.")]
    public AudioClip sinisterLaughterClip;

    private AudioSource _audioSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 0f; // 2D purely local audio
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // =========================================================================
    //  Dispatch Deal
    // =========================================================================

    public void SendDeal(ulong targetClientId, string title, string terms, string reward, bool grantWeapon)
    {
        if (NetworkManager.Singleton == null) return;
        ulong senderId = NetworkManager.Singleton.LocalClientId;
        SendDealServerRpc(senderId, targetClientId, title, terms, reward, grantWeapon);
    }

    [Rpc(SendTo.Server)]
    private void SendDealServerRpc(ulong senderId, ulong targetClientId, string title, string terms, string reward, bool grantWeapon)
    {
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(targetClientId))
        {
            Debug.LogWarning($"[DealSystemNet] Target client {targetClientId} is not connected.");
            return;
        }

        // Forward to the specific target client
        ReceiveDealClientRpc(senderId, title, terms, reward, grantWeapon, 
            RpcTarget.Single(targetClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ReceiveDealClientRpc(ulong senderId, string title, string terms, string reward, bool grantWeapon, RpcParams rpcParams = default)
    {
        Debug.Log($"[DealSystemNet] Received pact proposal from client {senderId}: '{title}'");

        if (DealNotificationUI.Instance != null)
        {
            DealNotificationUI.Instance.DisplayDealOffer(senderId, title, terms, reward, grantWeapon);
        }
    }

    // =========================================================================
    //  Response: Accept / Decline
    // =========================================================================

    public void RespondToDeal(ulong girlClientId, bool accepted, bool grantWeapon)
    {
        if (NetworkManager.Singleton == null) return;
        ulong responderId = NetworkManager.Singleton.LocalClientId;
        RespondDealServerRpc(girlClientId, responderId, accepted, grantWeapon);
    }

    [Rpc(SendTo.Server)]
    private void RespondDealServerRpc(ulong girlClientId, ulong responderId, bool accepted, bool grantWeapon)
    {
        string responderName = PlayerNameManager.GetPlayerName(responderId);
        if (string.IsNullOrEmpty(responderName)) responderName = $"Player {responderId}";

        if (accepted)
        {
            // 1. Grant weapon if applicable
            if (grantWeapon && NetworkManager.Singleton.ConnectedClients.TryGetValue(responderId, out var client))
            {
                if (client.PlayerObject != null)
                {
                    var combat = client.PlayerObject.GetComponent<InvestigatorCombatNet>();
                    if (combat != null)
                    {
                        combat.GrantMeleeWeapon();
                    }
                }
            }

            // 2. Play private sinister laughter strictly on recipient client
            PlayPrivateLaughClientRpc(RpcTarget.Single(responderId, RpcTargetUse.Temp));

            // 3. Notify Girl client
            if (NetworkManager.Singleton.ConnectedClients.ContainsKey(girlClientId))
            {
                NotifyGirlDealResponseClientRpc(responderName, true, RpcTarget.Single(girlClientId, RpcTargetUse.Temp));
            }
        }
        else
        {
            // Notify Girl client of refusal
            if (NetworkManager.Singleton.ConnectedClients.ContainsKey(girlClientId))
            {
                NotifyGirlDealResponseClientRpc(responderName, false, RpcTarget.Single(girlClientId, RpcTargetUse.Temp));
            }
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void PlayPrivateLaughClientRpc(RpcParams rpcParams = default)
    {
        Debug.Log("[DealSystemNet] Pact sealed! Playing private sinister laugh.");
        if (_audioSource != null && sinisterLaughterClip != null)
        {
            _audioSource.PlayOneShot(sinisterLaughterClip, 1.0f);
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void NotifyGirlDealResponseClientRpc(string playerName, bool accepted, RpcParams rpcParams = default)
    {
        string msg = accepted 
            ? $"PACT FORGED: {playerName} accepted your terms!" 
            : $"PACT REJECTED: {playerName} refused the spirits.";

        Debug.Log($"[DealSystemNet] {msg}");
        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification(msg, 4f);
        }
    }
}
