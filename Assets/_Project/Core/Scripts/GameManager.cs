using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Orchestrates the match lifecycle (player spawning, death routing,
/// win condition evaluation, and taunt broadcasting).
/// Timer and monster spawning have been intentionally removed.
/// 
/// OCP: Win-condition messages are driven by a dictionary — add new WinReasons
/// without touching EndMatch logic.
/// 
/// DIP: Interacts with GirlStealth and MonsterAI through their public APIs,
/// not through internal implementation details.
/// </summary>
public class GameManager : NetworkBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector Fields — Prefabs
    // -------------------------------------------------------------------------
    [Header("Player Prefabs")]
    public GameObject girlPrefab;
    public List<GameObject> explorerPrefabs;

    // -------------------------------------------------------------------------
    //  Inspector Fields — Spawn Points
    // -------------------------------------------------------------------------
    [Header("Spawn Points")]
    [Tooltip("Drag SpawnPoint GameObjects here for the Girl / Demon player.")]
    public List<Transform> girlSpawnPoints = new List<Transform>();

    [Tooltip("Drag SpawnPoint GameObjects here for Explorer players.")]
    public List<Transform> explorerSpawnPoints = new List<Transform>();

    // -------------------------------------------------------------------------
    //  Inspector Fields — Match Settings
    // -------------------------------------------------------------------------
    [Header("Match Settings")]
    public int minPlayers = 2;
    public float spawnHeight = 50f;

    // -------------------------------------------------------------------------
    //  Network Variables (Runtime State, Synced)
    // -------------------------------------------------------------------------
    [Header("Runtime State (Synced)")]
    public NetworkVariable<bool> gameEnded = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // -------------------------------------------------------------------------
    //  Singleton
    // -------------------------------------------------------------------------
    public static GameManager Instance { get; private set; }
    public static GameManager Singleton => Instance;

    // -------------------------------------------------------------------------
    //  Public State Accessors
    // -------------------------------------------------------------------------
    public int  CurrentPlayerCount => NetworkManager.Singleton.ConnectedClientsIds.Count;
    public bool HasGameStarted     => _gameHasStarted;
    public Transform GirlTransform => _girlPlayer != null ? _girlPlayer.transform : null;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private bool _gameHasStarted = false;
    private NetworkObject _girlPlayer;
    private List<NetworkObject> _aliveExplorers   = new List<NetworkObject>();
    private List<GirlStealth>   _allGirlComponents = new List<GirlStealth>();
    private MatchResultOverlay  _cachedOverlay;

    // OCP: Extend win messages here without touching EndMatch logic.
    private static readonly Dictionary<WinReason, string> WinMessages = new Dictionary<WinReason, string>
    {
        { WinReason.TeamWipe,   "Demon Wins! (No Survivors)"   },
        { WinReason.DemonSlain, "Explorers Win! (Demon Slain)" },
    };

    // -------------------------------------------------------------------------
    //  Win Reason Enum
    // -------------------------------------------------------------------------
    public enum WinReason { TeamWipe, DemonSlain }

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Pre-cache overlay so EndMatch doesn't need a scene search
        _cachedOverlay = FindFirstObjectByType<MatchResultOverlay>(FindObjectsInactive.Include);
    }

    // =========================================================================
    //  Player Registration
    // =========================================================================

    /// <summary>
    /// Called by the server when a player prefab spawns to keep internal lists in sync.
    /// </summary>
    public void RegisterPlayer(NetworkObject player, bool isGirl)
    {
        if (isGirl)
        {
            _girlPlayer = player;
            if (player.TryGetComponent<GirlStealth>(out var stealth))
            {
                if (!_allGirlComponents.Contains(stealth))
                    _allGirlComponents.Add(stealth);
            }
        }
        else
        {
            if (!_aliveExplorers.Contains(player))
                _aliveExplorers.Add(player);
        }
    }

    /// <summary>Returns all active demon components (used by taunt system).</summary>
    public List<GirlStealth> GetAllDemons() => _allGirlComponents;

    // =========================================================================
    //  Match Start
    // =========================================================================

    /// <summary>The Host calls this via the Lobby UI when the lobby is ready.</summary>
    public void StartGame()
    {
        if (!IsServer || _gameHasStarted) return;

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        if (clientIds.Count < minPlayers) return;

        _gameHasStarted = true;

        // Pick a random client to be the Girl / Demon
        int  randomGirlIndex = Random.Range(0, clientIds.Count);
        ulong girlClientId   = clientIds[randomGirlIndex];

        foreach (ulong clientId in clientIds)
        {
            SpawnPlayerRole(clientId, clientId == girlClientId);
        }

        // NOTE: Monster spawning has been intentionally disabled.
        // To re-enable, attach a separate MonsterSpawner component to the scene.
    }

    // =========================================================================
    //  Private Spawn Helpers
    // =========================================================================
    private void SpawnPlayerRole(ulong clientId, bool isGirl)
    {
        GameObject prefabToSpawn = isGirl ? girlPrefab : GetRandomExplorerPrefab();
        if (prefabToSpawn == null) return;

        Vector3 spawnPos = GetSpawnPosition(isGirl);
        GameObject playerInstance = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.SpawnAsPlayerObject(clientId);
            RegisterPlayer(netObj, isGirl);
        }
    }

    /// <summary>
    /// Returns a world-space spawn position.
    /// Girl: random pick from girlSpawnPoints (fallback: origin + spawnHeight).
    /// Explorer: random pick from explorerSpawnPoints (fallback: random XZ offset).
    /// </summary>
    private Vector3 GetSpawnPosition(bool isGirl)
    {
        if (isGirl)
        {
            if (girlSpawnPoints != null && girlSpawnPoints.Count > 0)
            {
                Transform pt = girlSpawnPoints[Random.Range(0, girlSpawnPoints.Count)];
                if (pt != null) return pt.position;
            }
            return new Vector3(0f, spawnHeight, 0f);
        }
        else
        {
            if (explorerSpawnPoints != null && explorerSpawnPoints.Count > 0)
            {
                List<Transform> available = new List<Transform>(explorerSpawnPoints);
                available.RemoveAll(t => t == null);
                if (available.Count > 0)
                    return available[Random.Range(0, available.Count)].position;
            }
            return new Vector3(Random.Range(-10f, 10f), spawnHeight, Random.Range(-10f, 10f));
        }
    }

    private GameObject GetRandomExplorerPrefab()
    {
        if (explorerPrefabs == null || explorerPrefabs.Count == 0) return null;
        return explorerPrefabs[Random.Range(0, explorerPrefabs.Count)];
    }

    // =========================================================================
    //  Death & Win Conditions
    // =========================================================================

    /// <summary>Called by TargetHealth on the Server when an entity reaches 0 HP.</summary>
    public void OnEntityDeath(NetworkObject victim)
    {
        if (!IsServer || gameEnded.Value) return;

        if (victim == _girlPlayer)
        {
            EndMatch(WinReason.DemonSlain);
        }
        else if (_aliveExplorers.Contains(victim))
        {
            _aliveExplorers.Remove(victim);
            if (_aliveExplorers.Count == 0)
                EndMatch(WinReason.TeamWipe);
        }
    }

    private void EndMatch(WinReason reason)
    {
        gameEnded.Value = true;

        // OCP: message looked up from dictionary — no switch/case to modify
        string resultMessage = WinMessages.TryGetValue(reason, out string msg) ? msg : "Match Over";

        EndMatchClientRpc(resultMessage);
        Invoke(nameof(ResetMatch), 10f);
    }

    [ClientRpc]
    private void EndMatchClientRpc(string resultMessage)
    {
        if (_cachedOverlay == null)
            _cachedOverlay = FindFirstObjectByType<MatchResultOverlay>(FindObjectsInactive.Include);

        _cachedOverlay?.ShowResultDirectly(resultMessage);
    }

    private void ResetMatch()
    {
        if (!IsServer) return;
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    // =========================================================================
    //  Taunt Broadcast (Global Handshake)
    // =========================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void BroadcastTauntServerRpc(ulong demonId, int type)
    {
        foreach (var girl in _allGirlComponents)
        {
            if (girl != null && girl.NetworkObjectId == demonId)
            {
                if (girl.CanTaunt())
                {
                    girl.ResetTauntCooldown();
                    BroadcastTauntClientRpc(demonId, type);
                }
                break;
            }
        }
    }

    [ClientRpc]
    private void BroadcastTauntClientRpc(ulong demonId, int type)
    {
        foreach (var girl in _allGirlComponents)
        {
            if (girl != null && girl.NetworkObjectId == demonId)
            {
                girl.PlayLocalTaunt(type);
                break;
            }
        }
    }
}