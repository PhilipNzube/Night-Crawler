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

    // -------------------------------------------------------------------------
    //  Network State
    // -------------------------------------------------------------------------

    /// <summary>Synced girl client ID so all clients know who was selected.</summary>
    public NetworkVariable<ulong> revealedGirlClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

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

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);

        int investigatorCount = clientIds.Count - 1; // everyone except girl
        _expectedInvestigators  = Mathf.Max(0, investigatorCount);
        _investigatorsReadyCount = 0;
        _girlReady               = false;

        Debug.Log($"[GirlRevealManager] Beginning reveal. Girl: {girlClientId}. Expecting {_expectedInvestigators} investigator(s).");

        // Solo edge-case: only 1 player, skip the spin
        if (clientIds.Count <= 1)
        {
            _expectedInvestigators = 0;
            RoutePlayersRpc(girlClientId);
            return;
        }

        StartRevealRpc(girlClientId, clientIds.ToArray());
    }

    // =========================================================================
    //  RPCs
    // =========================================================================

    /// <summary>
    /// Broadcasts to all clients to start the slot-machine spin animation.
    /// GirlRevealUI runs the coroutine, then calls back to route players locally.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void StartRevealRpc(ulong girlClientId, ulong[] clientIds)
    {
        List<string> playerNames = CollectPlayerNames(new List<ulong>(clientIds));

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
        OnLocalSpinComplete(girlClientId);
    }

    /// <summary>
    /// Called by an investigator client when they have finished the squad showcase
    /// countdown and are ready to load the game.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportInvestigatorReadyServerRpc()
    {
        _investigatorsReadyCount++;
        Debug.Log($"[GirlRevealManager] Investigator ready {_investigatorsReadyCount}/{_expectedInvestigators}.");
        CheckAllReady();
    }

    /// <summary>
    /// Called by the girl client when she presses the READY button on her screen.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportGirlReadyServerRpc()
    {
        _girlReady = true;
        Debug.Log("[GirlRevealManager] Girl player ready.");
        CheckAllReady();
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
        bool isGirl = NetworkManager.Singleton != null &&
                      NetworkManager.Singleton.LocalClientId == girlClientId;

        if (isGirl)
        {
            Debug.Log("[GirlRevealManager] Local client is the Vengeful Spirit → showing girl screen.");
            if (girlFlow != null) girlFlow.SetActive(true);
        }
        else
        {
            Debug.Log("[GirlRevealManager] Local client is an investigator → showing character select.");
            if (investigatorFlow != null) investigatorFlow.SetActive(true);
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
        if (NetworkManager.Singleton?.SpawnManager.GetPlayerNetworkObject(clientId) is NetworkObject netObj
            && netObj != null)
        {
            NetworkPlayerName nameComp = netObj.GetComponent<NetworkPlayerName>();
            if (nameComp != null) return nameComp.playerName.Value.ToString();
        }

        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
            return PlayerNameManager.GetPlayerName();

        return $"Player {clientId % 1000}";
    }
}
