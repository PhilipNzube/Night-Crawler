using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using NightCrawler.Economy;

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
    private const string MSG_NOTIFY_OUTCOME = "NC_Deal_NotifyOutcome";

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
    private readonly HashSet<ulong> _pendingTargetClientIds = new HashSet<ulong>();
    private readonly HashSet<ulong> _activeDealClientIds = new HashSet<ulong>();

    public bool HasPendingOffer(ulong clientId)
    {
        return _pendingTargetClientIds.Contains(clientId);
    }

    public bool HasActiveDeal(ulong clientId)
    {
        if (_activeDealClientIds.Contains(clientId)) return true;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            var pNet = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (pNet == null && NetworkManager.Singleton.ConnectedClients != null &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                pNet = client.PlayerObject;
            }
            if (pNet == null && NetworkManager.Singleton.SpawnManager.SpawnedObjects != null)
            {
                foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
                {
                    if (netObj != null && netObj.OwnerClientId == clientId &&
                        (netObj.IsPlayerObject || netObj.GetComponent<NetworkPlayerName>() != null || netObj.CompareTag("Player")))
                    {
                        pNet = netObj;
                        break;
                    }
                }
            }

            if (pNet != null && pNet.TryGetComponent<PlayerPossessableNet>(out var possessable) && possessable.hasActiveDeal.Value)
            {
                return true;
            }
        }
        return false;
    }

    public void RegisterActiveDeal(ulong clientId)
    {
        _activeDealClientIds.Add(clientId);
    }

    public void ClearActiveDeal(ulong clientId)
    {
        _activeDealClientIds.Remove(clientId);
    }

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
        _audioSource.volume = GameSettingsManager.UIVolumeVal;
        GameSettingsManager.OnUIVolumeChanged += HandleUIVolumeChanged;
    }

    private void HandleUIVolumeChanged(float vol)
    {
        if (_audioSource != null) _audioSource.volume = vol;
    }

    private void OnDestroy()
    {
        GameSettingsManager.OnUIVolumeChanged -= HandleUIVolumeChanged;
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
        cm.RegisterNamedMessageHandler(MSG_NOTIFY_OUTCOME, OnClientReceivedNotifyOutcome);

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
        cm.UnregisterNamedMessageHandler(MSG_NOTIFY_OUTCOME);

        _isRegistered = false;
    }

    // =========================================================================
    //  Dispatch Deal
    // =========================================================================

    public void SendDeal(ulong targetClientId, string title, string terms, string reward, bool grantWeapon, int timeLimitSeconds = 120, int penaltyCredits = 15, ulong markClientId = ulong.MaxValue, string markPlayerName = "")
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[DealSystemNet] NetworkManager is not connected.");
            return;
        }

        if (!_isRegistered)
        {
            RegisterMessages();
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        Debug.Log($"[DealSystemNet] Sending deal '{title}' to client {targetClientId} (time={timeLimitSeconds}s, penalty={penaltyCredits}, mark={markPlayerName})");

        _pendingTargetClientIds.Add(targetClientId);

        if (NetworkManager.Singleton.IsServer)
        {
            // Server (Host) sends offer directly
            DeliverOfferToTarget(localId, targetClientId, title, terms, reward, grantWeapon, timeLimitSeconds, penaltyCredits, markClientId, markPlayerName);
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
            writer.WriteValueSafe(timeLimitSeconds);
            writer.WriteValueSafe(penaltyCredits);
            writer.WriteValueSafe(markClientId);
            writer.WriteValueSafe(markPlayerName ?? "");

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_SEND_OFFER, NetworkManager.ServerClientId, writer);
        }
    }

    private void DeliverOfferToTarget(ulong senderId, ulong targetClientId, string title, string terms, string reward, bool grantWeapon, int timeLimitSeconds = 120, int penaltyCredits = 15, ulong markClientId = ulong.MaxValue, string markPlayerName = "")
    {
        // Suppress deal delivery if target player is dead
        NetworkObject targetObj = null;
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (NetworkManager.Singleton.ConnectedClients != null &&
                    NetworkManager.Singleton.ConnectedClients.TryGetValue(targetClientId, out var client))
                {
                    targetObj = client.PlayerObject;
                }
                if (targetObj == null && NetworkManager.Singleton.SpawnManager != null)
                {
                    targetObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(targetClientId);
                }
            }
            else
            {
                if (targetClientId == NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.SpawnManager != null)
                {
                    targetObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                }
            }

            if (targetObj == null && NetworkManager.Singleton.SpawnManager != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects != null)
            {
                foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
                {
                    if (netObj != null && netObj.OwnerClientId == targetClientId &&
                        (netObj.IsPlayerObject || netObj.GetComponent<NetworkPlayerName>() != null || netObj.CompareTag("Player")))
                    {
                        targetObj = netObj;
                        break;
                    }
                }
            }
        }

        if (targetObj != null)
        {
            if ((targetObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) ||
                (targetObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead))
            {
                Debug.Log($"[DealSystemNet] Target client {targetClientId} is dead; suppressing deal offer delivery.");
                return;
            }
        }

        if (NetworkManager.Singleton != null && targetClientId == NetworkManager.Singleton.LocalClientId)
        {
            var notif = DealNotificationUI.Instance ?? FindFirstObjectByType<DealNotificationUI>(FindObjectsInactive.Include);
            if (notif != null)
            {
                notif.gameObject.SetActive(true);
                notif.DisplayDealOffer(senderId, title, terms, reward, grantWeapon, timeLimitSeconds, penaltyCredits, markClientId, markPlayerName);
            }
            else
            {
                Debug.LogError("[DealSystemNet] DealNotificationUI not found when delivering to local player!");
            }
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer &&
                 (NetworkManager.Singleton.ConnectedClients == null || NetworkManager.Singleton.ConnectedClients.ContainsKey(targetClientId)))
        {
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteValueSafe(senderId);
            writer.WriteValueSafe(title ?? "");
            writer.WriteValueSafe(terms ?? "");
            writer.WriteValueSafe(reward ?? "");
            writer.WriteValueSafe(grantWeapon);
            writer.WriteValueSafe(timeLimitSeconds);
            writer.WriteValueSafe(penaltyCredits);
            writer.WriteValueSafe(markClientId);
            writer.WriteValueSafe(markPlayerName ?? "");

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_DELIVER_OFFER, targetClientId, writer);
            Debug.Log($"[DealSystemNet] Sent MSG_DELIVER_OFFER to client {targetClientId}");
        }
        else
        {
            Debug.LogWarning($"[DealSystemNet] Target client {targetClientId} is not connected or cannot receive offer.");
        }
    }

    private void OnServerReceivedSendOffer(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong targetClientId);
        reader.ReadValueSafe(out string title);
        reader.ReadValueSafe(out string terms);
        reader.ReadValueSafe(out string reward);
        reader.ReadValueSafe(out bool grantWeapon);
        reader.ReadValueSafe(out int timeLimitSeconds);
        reader.ReadValueSafe(out int penaltyCredits);
        ulong markClientId = ulong.MaxValue;
        string markPlayerName = "";
        if (reader.Length > reader.Position)
        {
            reader.ReadValueSafe(out markClientId);
            reader.ReadValueSafe(out markPlayerName);
        }

        DeliverOfferToTarget(senderClientId, targetClientId, title, terms, reward, grantWeapon, timeLimitSeconds, penaltyCredits, markClientId, markPlayerName);
    }

    private void OnClientReceivedDeliverOffer(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong girlSenderId);
        reader.ReadValueSafe(out string title);
        reader.ReadValueSafe(out string terms);
        reader.ReadValueSafe(out string reward);
        reader.ReadValueSafe(out bool grantWeapon);
        reader.ReadValueSafe(out int timeLimitSeconds);
        reader.ReadValueSafe(out int penaltyCredits);
        ulong markClientId = ulong.MaxValue;
        string markPlayerName = "";
        if (reader.Length > reader.Position)
        {
            reader.ReadValueSafe(out markClientId);
            reader.ReadValueSafe(out markPlayerName);
        }

        Debug.Log($"[DealSystemNet] Received deal offer from {girlSenderId}: '{title}' (time={timeLimitSeconds}s, mark={markPlayerName})");
        var notif = DealNotificationUI.Instance ?? FindFirstObjectByType<DealNotificationUI>(FindObjectsInactive.Include);
        if (notif != null)
        {
            notif.gameObject.SetActive(true);
            notif.DisplayDealOffer(girlSenderId, title, terms, reward, grantWeapon, timeLimitSeconds, penaltyCredits, markClientId, markPlayerName);
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

    public void HandleDealResponseOnServer(ulong responderId, ulong girlClientId, bool accepted, bool grantWeapon)
    {
        _pendingTargetClientIds.Remove(responderId);

        string responderName = PlayerNameManager.GetPlayerName(responderId);
        if (string.IsNullOrEmpty(responderName)) responderName = $"Player {responderId}";

        if (accepted)
        {
            _activeDealClientIds.Add(responderId);

            // 1. Grant weapon if applicable (flagged as DealGranted so it cannot harm monsters)
            if (grantWeapon && NetworkManager.Singleton.ConnectedClients.TryGetValue(responderId, out var client))
            {
                if (client.PlayerObject != null)
                {
                    var combat = client.PlayerObject.GetComponent<InvestigatorCombatNet>();
                    if (combat != null)
                    {
                        combat.GrantMeleeWeapon(true);
                    }
                }
            }

            // Register traitor with MatchEconomyManager
            if (NightCrawler.Economy.MatchEconomyManager.Instance != null)
            {
                NightCrawler.Economy.MatchEconomyManager.Instance.TagPlayerAcceptedDeal(responderId);
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
                NotifyGirlDealResult(responderName, true, responderId);
            }
            else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(girlClientId))
            {
                using var notifyWriter = new FastBufferWriter(256, Allocator.Temp);
                notifyWriter.WriteValueSafe(responderName);
                notifyWriter.WriteValueSafe(true);
                notifyWriter.WriteValueSafe(responderId);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_NOTIFY_GIRL, girlClientId, notifyWriter);
            }
        }
        else
        {
            // Notify Girl client of refusal
            if (girlClientId == NetworkManager.Singleton.LocalClientId)
            {
                NotifyGirlDealResult(responderName, false, responderId);
            }
            else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(girlClientId))
            {
                using var notifyWriter = new FastBufferWriter(256, Allocator.Temp);
                notifyWriter.WriteValueSafe(responderName);
                notifyWriter.WriteValueSafe(false);
                notifyWriter.WriteValueSafe(responderId);
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
            _audioSource.PlayOneShot(clipToPlay, GameSettingsManager.UIVolumeVal);
        }
    }

    private void OnClientReceivedNotifyGirl(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string responderName);
        reader.ReadValueSafe(out bool accepted);
        ulong respId = 0;
        if (reader.Length > reader.Position)
        {
            reader.ReadValueSafe(out respId);
            _pendingTargetClientIds.Remove(respId);
        }

        NotifyGirlDealResult(responderName, accepted, respId);
    }

    private void NotifyGirlDealResult(string responderName, bool accepted, ulong responderId = 0)
    {
        if (responderId != 0)
        {
            _pendingTargetClientIds.Remove(responderId);
            if (accepted)
            {
                _activeDealClientIds.Add(responderId);
            }
        }
        Debug.Log($"[DealSystemNet] Deal response from {responderName}: accepted={accepted}");
        string msg = accepted 
            ? $"{responderName} accepted your dark deal!" 
            : $"{responderName} rejected your dark deal.";

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification(msg, 4f);
        }
        else if (DeathUI.Instance != null)
        {
            DeathUI.Instance.PostDealResponse(responderName, accepted);
        }
    }

    /// <summary>
    /// Broadcasts deal completion or failure outcome to the Girl who dispatched it.
    /// </summary>
    public void ReportDealOutcome(ulong girlClientId, bool success, string dealTitle, int amount)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        _activeDealClientIds.Remove(localId);

        string playerName = PlayerNameManager.GetPlayerName(localId);
        if (string.IsNullOrEmpty(playerName)) playerName = $"Investigator {localId}";

        if (girlClientId == localId)
        {
            HandleDealOutcomeOnGirl(playerName, success, dealTitle, amount, localId);
            return;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            if (NetworkManager.Singleton.ConnectedClients.ContainsKey(girlClientId))
            {
                using var writer = new FastBufferWriter(256, Allocator.Temp);
                writer.WriteValueSafe(playerName);
                writer.WriteValueSafe(success);
                writer.WriteValueSafe(dealTitle ?? "");
                writer.WriteValueSafe(amount);
                writer.WriteValueSafe(localId);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_NOTIFY_OUTCOME, girlClientId, writer);
            }
        }
        else
        {
            // Route through server to girlClientId
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteValueSafe(playerName);
            writer.WriteValueSafe(success);
            writer.WriteValueSafe(dealTitle ?? "");
            writer.WriteValueSafe(amount);
            writer.WriteValueSafe(girlClientId);
            writer.WriteValueSafe(localId);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_NOTIFY_OUTCOME, NetworkManager.ServerClientId, writer);
        }
    }

    private void OnClientReceivedNotifyOutcome(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string playerName);
        reader.ReadValueSafe(out bool success);
        reader.ReadValueSafe(out string dealTitle);
        reader.ReadValueSafe(out int amount);

        ulong responderClientId = 0;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && reader.Length > reader.Position)
        {
            reader.ReadValueSafe(out ulong targetGirlId);
            if (reader.Length > reader.Position)
            {
                reader.ReadValueSafe(out responderClientId);
            }
            if (targetGirlId != NetworkManager.Singleton.LocalClientId)
            {
                // Relay to Girl
                if (NetworkManager.Singleton.ConnectedClients.ContainsKey(targetGirlId))
                {
                    using var forward = new FastBufferWriter(256, Allocator.Temp);
                    forward.WriteValueSafe(playerName);
                    forward.WriteValueSafe(success);
                    forward.WriteValueSafe(dealTitle);
                    forward.WriteValueSafe(amount);
                    forward.WriteValueSafe(responderClientId);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_NOTIFY_OUTCOME, targetGirlId, forward);
                }
                return;
            }
        }
        else if (reader.Length > reader.Position)
        {
            reader.ReadValueSafe(out responderClientId);
        }

        HandleDealOutcomeOnGirl(playerName, success, dealTitle, amount, responderClientId);
    }

    private void HandleDealOutcomeOnGirl(string playerName, bool success, string dealTitle, int amount, ulong responderClientId = 0)
    {
        if (responderClientId != 0)
        {
            _activeDealClientIds.Remove(responderClientId);
        }

        string status = success ? "COMPLETED" : "FAILED";
        string msg = success
            ? $"DEAL {status}: {playerName} fulfilled '{dealTitle}'!"
            : $"DEAL {status}: {playerName} failed '{dealTitle}'. Penalty collected: {amount} {CurrencyConfig.CurrencyPlural}.";

        Debug.Log($"[DealSystemNet] Girl notified of deal outcome: {msg}");

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification(msg, 4.5f);
        }
        else if (DeathUI.Instance != null)
        {
            DeathUI.Instance.PostDealResponse(playerName, success);
        }
    }
}
