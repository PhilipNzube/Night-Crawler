using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Orchestrates the Vengeful Spirit reveal and post-reveal routing.
///
/// Responsibilities:
///   1. BeginReveal() — called by the server when the host starts the match.
///      Triggers a fresh random spirit selection, then broadcasts the reveal RPC.
///   2. All clients play the slot-machine spin via GirlRevealUI.
///   3. After the spin, each client is locally routed:
///        • Girl client  → GirlPlayerScreen (dancing, waits for READY).
///        • Investigators → investigatorFlow (CharacterSelectUI white room).
///   4. Tracks ready signals from investigators (after squad screen) AND the girl
///      (after pressing READY). When all are done, the server loads the GameScene.
///
/// OCP: Scene loading strategy is isolated to LoadGameScene() — swap it out
///      without touching reveal logic.
///
/// Setup:
///   Attach to a persistent NetworkObject in the LobbyScene (e.g. GameFlowManager).
///   Wire revealUI, girlFlow, and investigatorFlow in the Inspector.
/// </summary>
public class GirlRevealManager : NetworkBehaviour
{
    public static GirlRevealManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------
    [Header("Scene Loading")]
    [Tooltip("Name of the Game Scene to load once all players are ready.")]
    public string gameSceneName = "GameScene";

    [Header("Reveal UI")]
    [Tooltip("GirlRevealUI component in the LobbyScene. Drives the slot-machine animation.")]
    public GirlRevealUI revealUI;

    [Header("Post-Reveal Flow Roots")]
    [Tooltip("Root GameObject of the investigator flow (contains CharacterSelectUI etc.). " +
             "Disabled initially; enabled for non-girl players after reveal.")]
    public GameObject investigatorFlow;

    [Tooltip("Root GameObject of the girl player's exclusive flow (GirlPlayerScreen). " +
             "Disabled initially; enabled for the girl player after reveal.")]
    public GameObject girlFlow;

    [Header("Testing & Debugging")]
    [Tooltip("If true, the slot-machine spin UI runs even when testing alone (1 player connected). " +
             "Set to false if you want solo testing to jump straight to the screen.")]
    public bool enableSlotSpinInSoloTest = true;

    [Tooltip("DEV ONLY: When true, this client is always routed to the INVESTIGATOR flow " +
             "(Character Select screen) regardless of who is picked as the girl. " +
             "Use this to test character selection without needing a second player.")]
    public bool forceInvestigatorMode = false;

    // -------------------------------------------------------------------------
    //  Network State
    // -------------------------------------------------------------------------

    /// <summary>Synced girl client ID so all clients know who was selected.</summary>
    public NetworkVariable<ulong> revealedGirlClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private static ulong s_SavedGirlClientId = ulong.MaxValue;
    public static ulong SavedGirlClientId => s_SavedGirlClientId;

    // -------------------------------------------------------------------------
    //  Private State (Server-Only)
    // -------------------------------------------------------------------------
    private int  _expectedInvestigators  = 0;
    private int  _investigatorsReadyCount = 0;
    private bool _girlReady              = false;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Both flow roots should be hidden until the reveal routes players
        if (investigatorFlow != null) investigatorFlow.SetActive(false);
        if (girlFlow         != null) girlFlow.SetActive(false);
    }

    private static readonly Dictionary<ulong, string> s_RegisteredPlayerNames = new Dictionary<ulong, string>();

    public static string GetRegisteredPlayerName(ulong clientId)
    {
        if (s_RegisteredPlayerNames.TryGetValue(clientId, out string name) && !string.IsNullOrWhiteSpace(name) && !name.StartsWith("Player "))
        {
            return name;
        }

        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            string localName = PlayerNameManager.GetPlayerName();
            if (!string.IsNullOrWhiteSpace(localName)) return localName;
        }

        if (s_RegisteredPlayerNames.TryGetValue(clientId, out string fallback) && !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return $"Player {clientId % 1000}";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        SubmitLocalPlayerName();

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedToServer;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedToServer;
        }
    }

    private void OnClientConnectedToServer(ulong newClientId)
    {
        if (!IsServer) return;
        // Sync all known names to the new client
        foreach (var kvp in s_RegisteredPlayerNames)
        {
            SyncPlayerNameClientRpc(kvp.Key, new Unity.Collections.FixedString64Bytes(kvp.Value));
        }
    }

    public void SubmitLocalPlayerName()
    {
        if (NetworkManager.Singleton == null) return;
        ulong myId = NetworkManager.Singleton.LocalClientId;
        string myName = PlayerNameManager.GetPlayerName();
        if (string.IsNullOrWhiteSpace(myName)) myName = $"Player {myId % 1000}";

        s_RegisteredPlayerNames[myId] = myName;

        if (IsSpawned)
        {
            RegisterPlayerNameServerRpc(myId, new Unity.Collections.FixedString64Bytes(myName));
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RegisterPlayerNameServerRpc(ulong clientId, Unity.Collections.FixedString64Bytes name)
    {
        string nameStr = name.ToString();
        s_RegisteredPlayerNames[clientId] = nameStr;
        SyncPlayerNameClientRpc(clientId, name);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncPlayerNameClientRpc(ulong clientId, Unity.Collections.FixedString64Bytes name)
    {
        s_RegisteredPlayerNames[clientId] = name.ToString();
        Debug.Log($"[GirlRevealManager] Synchronized name: Client {clientId} -> '{name}'");
    }

    // =========================================================================
    //  Server API — called from LobbyUI.OnStartMatch()
    // =========================================================================

    /// <summary>
    /// Starts the full reveal sequence for all connected clients.
    /// Must be called on the server (IsServer guard is enforced internally).
    ///
    /// Performs a fresh spirit selection (so all currently connected players are
    /// eligible — avoids the early OnNetworkSpawn timing issue in CharacterSelectManager).
    /// </summary>
    public void BeginReveal()
    {
        if (!IsServer) return;

        // Re-select with the full current player list
        if (CharacterSelectManager.Instance != null)
            CharacterSelectManager.Instance.SelectRandomVengefulSpirit();

        ulong girlClientId = GetGirlClientId();
        revealedGirlClientId.Value = girlClientId;
        s_SavedGirlClientId = girlClientId;
        CharacterSelectManager.SaveVengefulSpiritRole(girlClientId);

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);

        int investigatorCount = clientIds.Count - 1; // everyone except girl
        _expectedInvestigators  = Mathf.Max(0, investigatorCount);
        _investigatorsReadyCount = 0;
        _girlReady               = false;

        Debug.Log($"[GirlRevealManager] Beginning reveal. Girl: {girlClientId}. Expecting {_expectedInvestigators} investigator(s).");

        // Start centralised ready-tracking so all clients see a live status panel
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.StartTracking(girlClientId, clientIds);

        // Solo testing handling
        if (clientIds.Count <= 1 && !enableSlotSpinInSoloTest)
        {
            _expectedInvestigators = 0;
            RoutePlayersRpc(girlClientId);
            return;
        }

        Unity.Collections.FixedString64Bytes[] nameArray = new Unity.Collections.FixedString64Bytes[clientIds.Count];
        for (int i = 0; i < clientIds.Count; i++)
        {
            string pName = GetRegisteredPlayerName(clientIds[i]);
            nameArray[i] = new Unity.Collections.FixedString64Bytes(pName);
        }

        StartRevealRpc(girlClientId, clientIds.ToArray(), nameArray);
    }

    // =========================================================================
    //  RPCs
    // =========================================================================

    /// <summary>
    /// Broadcasts to all clients to start the slot-machine spin animation.
    /// GirlRevealUI runs the coroutine, then calls back to route players locally.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void StartRevealRpc(ulong girlClientId, ulong[] clientIds, Unity.Collections.FixedString64Bytes[] synchedNames)
    {
        // Hide lobby UI on all clients
        if (LobbyUI.Instance != null)
            LobbyUI.Instance.HideLobbyUI();
        else
            FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include)?.HideLobbyUI();

        List<string> playerNames = new List<string>();
        for (int i = 0; i < clientIds.Length; i++)
        {
            string n = (synchedNames != null && i < synchedNames.Length)
                ? synchedNames[i].ToString()
                : GetRegisteredPlayerName(clientIds[i]);
            playerNames.Add(n);
            s_RegisteredPlayerNames[clientIds[i]] = n;
        }

        if (revealUI != null)
        {
            revealUI.StartSpin(girlClientId, playerNames.ToArray(), clientIds, OnLocalSpinComplete);
        }
        else
        {
            // No UI assigned — route immediately (editor/debug fallback)
            Debug.LogWarning("[GirlRevealManager] revealUI is null. Routing players without animation.");
            OnLocalSpinComplete(girlClientId);
        }
    }

    /// <summary>
    /// Skips the spin and routes players directly. Used for the solo / 1-player case.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void RoutePlayersRpc(ulong girlClientId)
    {
        if (LobbyUI.Instance != null)
            LobbyUI.Instance.HideLobbyUI();
        else
            FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include)?.HideLobbyUI();

        OnLocalSpinComplete(girlClientId);
    }

    /// <summary>
    /// Called by an investigator client when they have finished the squad showcase
    /// countdown and are ready to load the game.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportInvestigatorReadyServerRpc()
    {
        // Legacy path kept for SquadLineupDisplay compatibility.
        // PlayerReadyTracker.ReportInvestigatorConfirmedServerRpc() is now the
        // primary ready signal; this just keeps the old count in sync.
        _investigatorsReadyCount++;
        Debug.Log($"[GirlRevealManager] Investigator ready {_investigatorsReadyCount}/{_expectedInvestigators}.");
        CheckAllReady();
    }

    public void ReportGirlReady()
    {
        if (IsSpawned)
        {
            ReportGirlReadyServerRpc();
        }
        else
        {
            _girlReady = true;
            Debug.Log("[GirlRevealManager] Girl player ready (unspawned/local).");
            CheckAllReady();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportGirlReadyServerRpc()
    {
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.ReportGirlReady();

        _girlReady = true;
        Debug.Log("[GirlRevealManager] Girl player ready.");
        CheckAllReady();
    }

    /// <summary>
    /// Called by PlayerReadyTracker when ALL players (girl + all investigators) are ready.
    /// This is the definitive trigger for scene loading.
    /// </summary>
    public void OnAllTrackerPlayersReady()
    {
        if (!IsServer) return;
        Debug.Log("[GirlRevealManager] PlayerReadyTracker confirmed all ready — loading game scene.");
        LoadGameScene();
    }

    // =========================================================================
    //  Private — Local Routing (runs per-client after spin completes)
    // =========================================================================

    /// <summary>
    /// Called locally on each client when GirlRevealUI finishes its animation.
    /// Routes each player to their appropriate next screen without a broadcast RPC,
    /// since every client already knows the winner from the StartRevealRpc data.
    /// </summary>
    private void OnLocalSpinComplete(ulong girlClientId)
    {
        s_SavedGirlClientId = girlClientId;

        bool isGirl = NetworkManager.Singleton != null &&
                      NetworkManager.Singleton.LocalClientId == girlClientId;

        // Dev override: always go to investigator flow for testing character select
        if (forceInvestigatorMode)
        {
            Debug.Log("[GirlRevealManager] forceInvestigatorMode = true → routing to investigator flow regardless of girl selection.");

            // Clear the saved girl role so GameManager doesn't spawn Demon for this client
            // and the squad screen doesn't exclude the local player
            s_SavedGirlClientId = ulong.MaxValue;
            CharacterSelectManager.SaveVengefulSpiritRole(ulong.MaxValue);
            PersistentCharacterSelection.SetIsVengefulSpirit(false);

            if (girlFlow != null)
                girlFlow.SetActive(false);

            if (investigatorFlow != null)
                investigatorFlow.SetActive(true);
            else
                Debug.LogWarning("[GirlRevealManager] 'investigatorFlow' not assigned in Inspector.");
            return;
        }

        if (isGirl)
        {
            Debug.Log("[GirlRevealManager] Local client is the Vengeful Spirit → showing girl screen.");
            PersistentCharacterSelection.SetIsVengefulSpirit(true);

            if (investigatorFlow != null)
                investigatorFlow.SetActive(false);

            if (girlFlow != null)
            {
                girlFlow.SetActive(true);
            }
            else
            {
                Debug.LogWarning("[GirlRevealManager] WARNING: 'girlFlow' GameObject is NOT assigned in the Inspector on GirlRevealManager! " +
                                 "Drag your GirlPlayerScreen / GirlFlow panel into this slot.");
            }
        }
        else
        {
            Debug.Log("[GirlRevealManager] Local client is an investigator → showing character select.");
            PersistentCharacterSelection.SetIsVengefulSpirit(false);

            if (girlFlow != null)
                girlFlow.SetActive(false);

            if (investigatorFlow != null)
            {
                investigatorFlow.SetActive(true);
            }
            else
            {
                Debug.LogWarning("[GirlRevealManager] WARNING: 'investigatorFlow' GameObject is NOT assigned in the Inspector on GirlRevealManager! " +
                                 "Drag your CharacterSelectUI / InvestigatorFlow panel into this slot.");
            }
        }
    }

    // =========================================================================
    //  Private — Server Helpers
    // =========================================================================

    private void CheckAllReady()
    {
        if (!IsServer) return;

        bool investigatorsDone = (_investigatorsReadyCount >= _expectedInvestigators);
        // If there are no investigators (solo test with 1 player assigned girl), auto-pass
        bool girlDone = _girlReady || (_expectedInvestigators == 0);

        if (investigatorsDone && girlDone)
        {
            Debug.Log("[GirlRevealManager] All players ready — loading game scene.");
            LoadGameScene();
        }
    }

    private void LoadGameScene()
    {
        if (!IsServer) return;

        ShowLoadingScreenClientRpc();

        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(
                gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.LoadScene(gameSceneName);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameSceneName);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ShowLoadingScreenClientRpc()
    {
        if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.ShowLoadingScreen();
        }
    }

    private ulong GetGirlClientId()
    {
        if (CharacterSelectManager.Instance != null &&
            CharacterSelectManager.Instance.vengefulSpiritClientId.Value != 999)
        {
            return CharacterSelectManager.Instance.vengefulSpiritClientId.Value;
        }

        // Emergency fallback
        var ids = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        return ids.Count > 0 ? ids[Random.Range(0, ids.Count)] : 0UL;
    }

    private List<string> CollectPlayerNames(List<ulong> clientIds)
    {
        var names = new List<string>();
        foreach (ulong id in clientIds)
            names.Add(ResolvePlayerName(id));
        return names;
    }

    private string ResolvePlayerName(ulong clientId)
    {
        string registered = GetRegisteredPlayerName(clientId);
        if (!string.IsNullOrEmpty(registered) && !registered.StartsWith("Player "))
        {
            return registered;
        }

        NetworkObject netObj = null;

        // GetPlayerNetworkObject for remote clients only works on the server.
        // On clients, we can only safely fetch our own player object.
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                netObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            }
            else if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                netObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            }
        }

        if (netObj != null)
        {
            NetworkPlayerName nameComp = netObj.GetComponent<NetworkPlayerName>();
            if (nameComp != null) return nameComp.playerName.Value.ToString();
        }

        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
            return PlayerNameManager.GetPlayerName();

        return $"Player {clientId % 1000}";
    }
}
