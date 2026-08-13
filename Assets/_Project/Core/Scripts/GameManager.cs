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
    [Tooltip("Drag SpawnPoint GameObjects here for the Girl / Vengeful Spirit player.")]
    public List<Transform> girlSpawnPoints = new List<Transform>();

    [Tooltip("Drag SpawnPoint GameObjects here for Investigator players.")]
    public List<Transform> explorerSpawnPoints = new List<Transform>();

    // -------------------------------------------------------------------------
    //  Inspector Fields — Match Settings
    // -------------------------------------------------------------------------
    [Header("Match Settings")]
    [Tooltip("Minimum connected players required to start. Set to 1 for solo testing, or 2+ for multiplayer builds.")]
    public int minPlayers = 1;
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
        { WinReason.TeamWipe,   "Vengeful Spirit Wins! (No Survivors)"   },
        { WinReason.DemonSlain, "Investigators Win! (Vengeful Spirit Slain)" },
    };

    // -------------------------------------------------------------------------
    //  Win Reason Enum
    // -------------------------------------------------------------------------
    public enum WinReason { TeamWipe, DemonSlain }

    // =========================================================================
    //  Unity & Network Lifecycle
    // =========================================================================
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Pre-cache overlay so EndMatch doesn't need a scene search
        _cachedOverlay = FindFirstObjectByType<MatchResultOverlay>(FindObjectsInactive.Include);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && !_gameHasStarted)
        {
            AutoDiscoverSpawnPoints();
            StartGame();
        }
    }

    void Start()
    {
        // Auto-discover spawn points in current Game Scene if empty
        AutoDiscoverSpawnPoints();

        // Fallback start if NetworkManager is active as Server and OnNetworkSpawn hasn't fired yet
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !_gameHasStarted)
        {
            StartGame();
        }
    }

    /// <summary>
    /// Searches the current scene for SpawnPoint components if spawn point lists are empty.
    /// </summary>
    public void AutoDiscoverSpawnPoints()
    {
        if ((girlSpawnPoints == null || girlSpawnPoints.Count == 0) ||
            (explorerSpawnPoints == null || explorerSpawnPoints.Count == 0))
        {
            SpawnPoint[] foundPoints = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
            if (girlSpawnPoints == null) girlSpawnPoints = new List<Transform>();
            if (explorerSpawnPoints == null) explorerSpawnPoints = new List<Transform>();

            foreach (var sp in foundPoints)
            {
                if (sp == null) continue;
                if (sp.spawnType == SpawnPointType.VengefulSpirit)
                {
                    if (!girlSpawnPoints.Contains(sp.transform))
                        girlSpawnPoints.Add(sp.transform);
                }
                else
                {
                    if (!explorerSpawnPoints.Contains(sp.transform))
                        explorerSpawnPoints.Add(sp.transform);
                }
            }
        }
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

    /// <summary>Triggers player spawning when the Game Scene loads for all connected clients.</summary>
    public void StartGame()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || _gameHasStarted) return;

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        int required = Mathf.Max(1, minPlayers);
        if (clientIds.Count < required)
        {
            Debug.LogWarning($"[GameManager] Cannot start match: Connected players ({clientIds.Count}) < Required ({required}). Change 'Min Players' in Inspector if you want solo testing.");
            return;
        }

        _gameHasStarted = true;
        AutoDiscoverSpawnPoints();

        // 1. Check synced Netcode role selection from lobby
        ulong girlClientId = 999;
        if (CharacterSelectManager.Instance != null && CharacterSelectManager.Instance.roleSelectionDone.Value)
        {
            girlClientId = CharacterSelectManager.Instance.vengefulSpiritClientId.Value;
        }

        // 2. Check forceInvestigatorMode override (for dev testing)
        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;

        foreach (ulong clientId in clientIds)
        {
            bool isGirl = (clientId == girlClientId) && !forceInvestigator;
            SpawnPlayerRole(clientId, isGirl);
        }
    }

    // =========================================================================
    //  Private Spawn Helpers
    // =========================================================================
    private void SpawnPlayerRole(ulong clientId, bool isGirl)
    {
        GameObject prefabToSpawn = isGirl ? girlPrefab : GetInvestigatorPrefabForClient(clientId);
        if (prefabToSpawn == null) return;

        GetSpawnTransform(isGirl, out Vector3 spawnPos, out Quaternion spawnRot);

        GameObject playerInstance = Instantiate(prefabToSpawn, spawnPos, spawnRot);

        // Ensure CharacterController (if attached) doesn't override the exact spawn position/rotation
        if (playerInstance.TryGetComponent<CharacterController>(out var cc))
        {
            cc.enabled = false;
            playerInstance.transform.position = spawnPos;
            playerInstance.transform.rotation = spawnRot;
            cc.enabled = true;
        }

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.SpawnAsPlayerObject(clientId);
            RegisterPlayer(netObj, isGirl);
        }
    }

    private GameObject GetInvestigatorPrefabForClient(ulong clientId)
    {
        int selectedIndex = 0;
        if (CharacterSelectManager.Instance != null)
            selectedIndex = CharacterSelectManager.Instance.GetSelectedCharacterIndex(clientId);

        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            int localSaved = PersistentCharacterSelection.GetSelectedCharacterIndex();
            if (localSaved >= 0) selectedIndex = localSaved;
        }

        // 1. Try explorerPrefabs by index
        if (explorerPrefabs != null && selectedIndex >= 0 && selectedIndex < explorerPrefabs.Count && explorerPrefabs[selectedIndex] != null)
            return explorerPrefabs[selectedIndex];

        // 2. Try CharacterSelectManager availableCharacters
        if (CharacterSelectManager.Instance != null)
        {
            GameObject mgrPrefab = CharacterSelectManager.Instance.GetInvestigatorPrefab(selectedIndex);
            if (mgrPrefab != null) return mgrPrefab;
        }

        // 3. Fallback
        return GetRandomExplorerPrefab();
    }

    /// <summary>
    /// Returns the exact world-space position and rotation of a spawn point.
    /// Girl: pick from girlSpawnPoints (fallback: origin + spawnHeight).
    /// Investigator: pick from explorerSpawnPoints.
    /// </summary>
    private void GetSpawnTransform(bool isGirl, out Vector3 position, out Quaternion rotation)
    {
        if (isGirl)
        {
            if (girlSpawnPoints != null && girlSpawnPoints.Count > 0)
            {
                List<Transform> available = new List<Transform>(girlSpawnPoints);
                available.RemoveAll(t => t == null);
                if (available.Count > 0)
                {
                    Transform pt = available[Random.Range(0, available.Count)];
                    position = pt.position;
                    rotation = pt.rotation;
                    return;
                }
            }
            position = new Vector3(0f, spawnHeight, 0f);
            rotation = Quaternion.identity;
        }
        else
        {
            if (explorerSpawnPoints != null && explorerSpawnPoints.Count > 0)
            {
                List<Transform> available = new List<Transform>(explorerSpawnPoints);
                available.RemoveAll(t => t == null);
                if (available.Count > 0)
                {
                    Transform chosenPoint = available[Random.Range(0, available.Count)];
                    position = chosenPoint.position;
                    rotation = chosenPoint.rotation;
                    return;
                }
            }
            position = new Vector3(Random.Range(-5f, 5f), 1f, Random.Range(-5f, 5f));
            rotation = Quaternion.identity;
        }
    }

    public GameObject GetRandomExplorerPrefab()
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