using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// SOLID — SRP: Manages dark deals and pacts between the Vengeful Spirit (Girl) and Investigators.
/// The Girl selects an active player, configures or chooses a deal on her screen, and dispatches it.
/// The recipient sees the exact deal terms on their screen.
/// Upon acceptance:
/// - Recipient is granted a weapon (if configured).
/// - A private chilling laughter audio plays strictly on the recipient's headphones/speakers.
/// - The Girl receives notification of pact sealed.
/// 
/// Uses CustomMessagingManager for guaranteed cross-network delivery without requiring scene NetworkObjects.
/// </summary>
public class DealSystemNet : MonoBehaviour
{
    private const string MSG_SEND_OFFER = "NC_Deal_SendOffer";
    private const string MSG_DELIVER_OFFER = "NC_Deal_DeliverOffer";
    private const string MSG_RESPOND = "NC_Deal_Respond";
    private const string MSG_PLAY_LAUGH = "NC_Deal_PlayLaugh";
    private const string MSG_NOTIFY_GIRL = "NC_Deal_NotifyGirl";

    private static DealSystemNet _instance;
    public static DealSystemNet Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<DealSystemNet>();
                if (_instance == null)
                {
                    var go = new GameObject("DealSystemNet");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<DealSystemNet>();
                }
            }
            return _instance;
        }
    }

    [Header("Audio (Sinister Laughter)")]
    [Tooltip("Audio clip of sinister laughter played privately to the player who accepts the deal.")]
    public AudioClip sinisterLaughterClip;

    private AudioSource _audioSource;
    private bool _isRegistered = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInit()
    {
        var _ = Instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 0f; // 2D purely local audio
        }
    }

    private void OnDestroy()
    {
        UnregisterMessages();
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (!_isRegistered && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            RegisterMessages();
        }
        else if (_isRegistered && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            _isRegistered = false;
        }
    }

    private void RegisterMessages()
    {
        if (_isRegistered || NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null) return;

        var cm = NetworkManager.Singleton.CustomMessagingManager;
        cm.RegisterNamedMessageHandler(MSG_SEND_OFFER, OnServerReceivedSendOffer);
        cm.RegisterNamedMessageHandler(MSG_DELIVER_OFFER, OnClientReceivedDeliverOffer);
        cm.RegisterNamedMessageHandler(MSG_RESPOND, OnServerReceivedRespond);
        cm.RegisterNamedMessageHandler(MSG_PLAY_LAUGH, OnClientReceivedPlayLaugh);
        cm.RegisterNamedMessageHandler(MSG_NOTIFY_GIRL, OnClientReceivedNotifyGirl);

        _isRegistered = true;
        Debug.Log("[DealSystemNet] Registered CustomMessagingManager deal handlers successfully.");
    }

    private void UnregisterMessages()
    {
        if (!_isRegistered || NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null) return;

        var cm = NetworkManager.Singleton.CustomMessagingManager;
        cm.UnregisterNamedMessageHandler(MSG_SEND_OFFER);
        cm.UnregisterNamedMessageHandler(MSG_DELIVER_OFFER);
        cm.UnregisterNamedMessageHandler(MSG_RESPOND);
        cm.UnregisterNamedMessageHandler(MSG_PLAY_LAUGH);
        cm.UnregisterNamedMessageHandler(MSG_NOTIFY_GIRL);

        _isRegistered = false;
    }

    // =========================================================================
    //  Dispatch Deal
    // =========================================================================

    public void SendDeal(ulong targetClientId, string title, string terms, string reward, bool grantWeapon)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[DealSystemNet] NetworkManager is not connected.");
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        Debug.Log($"[DealSystemNet] Sending deal '{title}' to client {targetClientId} from sender {localId}");

        if (NetworkManager.Singleton.IsServer)
        {
            // Server (Host) sends offer directly
            DeliverOfferToTarget(localId, targetClientId, title, terms, reward, grantWeapon);
        }
        else
        {
            // Client sends offer -> route through server
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteValueSafe(targetClientId);
            writer.WriteValueSafe(title ?? "");
            writer.WriteValueSafe(terms ?? "");
            writer.WriteValueSafe(reward ?? "");
            writer.WriteValueSafe(grantWeapon);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_SEND_OFFER, NetworkManager.ServerClientId, writer);
        }
    }

    private void DeliverOfferToTarget(ulong senderId, ulong targetClientId, string title, string terms, string reward, bool grantWeapon)
    {
        if (targetClientId == NetworkManager.Singleton.LocalClientId)
        {
            var notif = DealNotificationUI.Instance ?? FindFirstObjectByType<DealNotificationUI>(FindObjectsInactive.Include);
            if (notif != null)
            {
                notif.gameObject.SetActive(true);
                notif.DisplayDealOffer(senderId, title, terms, reward, grantWeapon);
            }
            else
            {
                Debug.LogError("[DealSystemNet] DealNotificationUI not found when delivering to local player!");
            }
        }
        else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(targetClientId))
        {
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteValueSafe(senderId);
            writer.WriteValueSafe(title ?? "");
            writer.WriteValueSafe(terms ?? "");
            writer.WriteValueSafe(reward ?? "");
            writer.WriteValueSafe(grantWeapon);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_DELIVER_OFFER, targetClientId, writer);
            Debug.Log($"[DealSystemNet] Sent MSG_DELIVER_OFFER to client {targetClientId}");
        }
        else
        {
            Debug.LogWarning($"[DealSystemNet] Target client {targetClientId} is not connected.");
        }
    }

    private void OnServerReceivedSendOffer(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong targetClientId);
        reader.ReadValueSafe(out string title);
        reader.ReadValueSafe(out string terms);
        reader.ReadValueSafe(out string reward);
        reader.ReadValueSafe(out bool grantWeapon);

        DeliverOfferToTarget(senderClientId, targetClientId, title, terms, reward, grantWeapon);
    }

    private void OnClientReceivedDeliverOffer(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong girlSenderId);
        reader.ReadValueSafe(out string title);
        reader.ReadValueSafe(out string terms);
        reader.ReadValueSafe(out string reward);
        reader.ReadValueSafe(out bool grantWeapon);

        Debug.Log($"[DealSystemNet] Received deal offer from {girlSenderId}: '{title}'");
        var notif = DealNotificationUI.Instance ?? FindFirstObjectByType<DealNotificationUI>(FindObjectsInactive.Include);
        if (notif != null)
        {
            notif.gameObject.SetActive(true);
            notif.DisplayDealOffer(girlSenderId, title, terms, reward, grantWeapon);
        }
        else
        {
            Debug.LogError("[DealSystemNet] DealNotificationUI could not be found anywhere in the scene!");
        }
    }

    // =========================================================================
    //  Response: Accept / Decline
    // =========================================================================

    public void RespondToDeal(ulong girlClientId, bool accepted, bool grantWeapon)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        ulong responderId = NetworkManager.Singleton.LocalClientId;
        Debug.Log($"[DealSystemNet] Responding to deal from Girl {girlClientId}: accepted={accepted}");

        if (NetworkManager.Singleton.IsServer)
        {
            HandleDealResponseOnServer(responderId, girlClientId, accepted, grantWeapon);
        }
        else
        {
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteValueSafe(girlClientId);
            writer.WriteValueSafe(accepted);
            writer.WriteValueSafe(grantWeapon);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_RESPOND, NetworkManager.ServerClientId, writer);
        }
    }

    private void OnServerReceivedRespond(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong girlClientId);
        reader.ReadValueSafe(out bool accepted);
        reader.ReadValueSafe(out bool grantWeapon);

        HandleDealResponseOnServer(senderClientId, girlClientId, accepted, grantWeapon);
    }

    private void HandleDealResponseOnServer(ulong responderId, ulong girlClientId, bool accepted, bool grantWeapon)
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
            if (responderId == NetworkManager.Singleton.LocalClientId)
            {
                PlaySinisterLaugh();
            }
            else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(responderId))
            {
                using var laughWriter = new FastBufferWriter(16, Allocator.Temp);
                laughWriter.WriteValueSafe(true);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_PLAY_LAUGH, responderId, laughWriter);
            }

            // 3. Notify Girl client
            if (girlClientId == NetworkManager.Singleton.LocalClientId)
            {
                NotifyGirlDealResult(responderName, true);
            }
            else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(girlClientId))
            {
                using var notifyWriter = new FastBufferWriter(256, Allocator.Temp);
                notifyWriter.WriteValueSafe(responderName);
                notifyWriter.WriteValueSafe(true);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_NOTIFY_GIRL, girlClientId, notifyWriter);
            }
        }
        else
        {
            // Notify Girl client of refusal
            if (girlClientId == NetworkManager.Singleton.LocalClientId)
            {
                NotifyGirlDealResult(responderName, false);
            }
            else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(girlClientId))
            {
                using var notifyWriter = new FastBufferWriter(256, Allocator.Temp);
                notifyWriter.WriteValueSafe(responderName);
                notifyWriter.WriteValueSafe(false);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_NOTIFY_GIRL, girlClientId, notifyWriter);
            }
        }
    }

    private void OnClientReceivedPlayLaugh(ulong senderClientId, FastBufferReader reader)
    {
        PlaySinisterLaugh();
    }

    private void PlaySinisterLaugh()
    {
        Debug.Log("[DealSystemNet] Pact sealed! Playing private sinister laugh.");
        if (_audioSource == null) return;

        AudioClip clipToPlay = sinisterLaughterClip;
        if (clipToPlay == null)
        {
            var allClips = Resources.FindObjectsOfTypeAll<AudioClip>();
            foreach (var c in allClips)
            {
                if (c != null && (c.name.Contains("Witch") || c.name.Contains("Laugh")))
                {
                    clipToPlay = c;
                    break;
                }
            }
        }

        if (clipToPlay != null)
        {
            _audioSource.PlayOneShot(clipToPlay, 1.0f);
        }
    }

    private void OnClientReceivedNotifyGirl(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string responderName);
        reader.ReadValueSafe(out bool accepted);

        NotifyGirlDealResult(responderName, accepted);
    }

    private void NotifyGirlDealResult(string responderName, bool accepted)
    {
        Debug.Log($"[DealSystemNet] Pact response from {responderName}: accepted={accepted}");
        if (DeathUI.Instance != null)
        {
            DeathUI.Instance.PostDealResponse(responderName, accepted);
        }
    }
}
