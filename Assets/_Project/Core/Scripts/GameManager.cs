using Unity.Netcode;
using UnityEngine;
using Unity.Netcode.Components;
using System.Collections.Generic;

public class GameManager : NetworkBehaviour
{
    public GameObject girlPrefab;
    public List<GameObject> explorerPrefabs;
    
    [Header("Monster Settings")]
    public List<GameObject> monsterPrefabs;
    public int monsterCount = 3;

    [Header("Spawn Points")]
    [Tooltip("Drag SpawnPoint GameObjects here for the Girl / Demon player.")]
    public List<Transform> girlSpawnPoints = new List<Transform>();
    [Tooltip("Drag SpawnPoint GameObjects here for Explorer players. One is picked randomly per Explorer.")]
    public List<Transform> explorerSpawnPoints = new List<Transform>();

    [Header("Match Settings")]
    public int minPlayers = 2; 
    public float spawnHeight = 50f;
    public float matchDuration = 900f; // 15 minutes
    
    [Header("Runtime State (Synced)")]
    public NetworkVariable<float> matchTimer = new NetworkVariable<float>(900f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> gameEnded = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static GameManager Instance { get; private set; }
    public static GameManager Singleton => Instance;

    private bool _gameHasStarted = false;
    private List<NetworkObject> _aliveExplorers = new List<NetworkObject>();
    private List<MonsterAI> _activeMonsters = new List<MonsterAI>();
    private NetworkObject _girlPlayer;
    private List<GirlStealth> _allGirlComponents = new List<GirlStealth>();
    private MatchResultOverlay _cachedOverlay;
    
    public Transform GirlTransform => _girlPlayer != null ? _girlPlayer.transform : null;

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
        
        // --- OPTIMIZED: Pre-cache overlay if it exists in the scene ---
        _cachedOverlay = FindObjectOfType<MatchResultOverlay>(true);
    }
    
    // Call this whenever a player spawns to keep our list in sync
    public void RegisterPlayer(NetworkObject player, bool isGirl)
    {
        if (isGirl)
        {
            _girlPlayer = player;
            if (player.TryGetComponent<GirlStealth>(out var stealth))
            {
                if (!_allGirlComponents.Contains(stealth)) _allGirlComponents.Add(stealth);
            }
        }
        else
        {
            if (!_aliveExplorers.Contains(player)) _aliveExplorers.Add(player);
        }
    }

    // --- ACCESSIBILITY HELPER ---
    public List<GirlStealth> GetAllDemons() => _allGirlComponents;

    void Update()
    {
        // --- GRACEFUL SHUTDOWN GUARD ---
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (!IsServer || !_gameHasStarted || gameEnded.Value) return;

        // 1. Tick match timer
        matchTimer.Value -= Time.deltaTime;
        if (matchTimer.Value <= 0)
        {
            matchTimer.Value = 0;
            EndMatch(WinReason.TimeOut);
        }
    }

    public enum WinReason { TimeOut, TeamWipe, DemonSlain }

    public int CurrentPlayerCount => NetworkManager.Singleton.ConnectedClientsIds.Count;
    public bool HasGameStarted => _gameHasStarted;

    // The Host can call this via a UI Button when the lobby is full
    public void StartGame()
    {
        if (!IsServer || _gameHasStarted) return;

        _gameHasStarted = true;
        
        List<ulong> clientIds = new List<ulong>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsIds)
        {
            clientIds.Add(client);
        }

        if (clientIds.Count < minPlayers) return;

        // Pick a random client to be the Girl
        int randomGirlIndex = Random.Range(0, clientIds.Count);
        ulong girlClientId = clientIds[randomGirlIndex];

        // 1. Spawn roles for every human who joined the lobby
        foreach (ulong clientId in clientIds)
        {
            SpawnPlayerRole(clientId, clientId == girlClientId);
        }

        // --- NEW: SPAWN THE AI MONSTERS ---
        SpawnMonsters();
    }

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
    /// For the Girl, picks randomly from girlSpawnPoints (or falls back to origin + spawnHeight).
    /// For Explorers, picks a UNIQUE random point from explorerSpawnPoints where possible
    /// (or falls back to a random XZ offset + spawnHeight).
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
            // Fallback
            return new Vector3(0f, spawnHeight, 0f);
        }
        else
        {
            if (explorerSpawnPoints != null && explorerSpawnPoints.Count > 0)
            {
                // Shuffle a copy so each Explorer gets a different point if possible
                List<Transform> available = new List<Transform>(explorerSpawnPoints);
                available.RemoveAll(t => t == null);
                if (available.Count > 0)
                {
                    int index = Random.Range(0, available.Count);
                    return available[index].position;
                }
            }
            // Fallback
            return new Vector3(Random.Range(-10f, 10f), spawnHeight, Random.Range(-10f, 10f));
        }
    }

    // Called by TargetHealth when someone dies on the Server
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
            {
                EndMatch(WinReason.TeamWipe);
            }
        }
    }

    private void EndMatch(WinReason reason)
    {
        gameEnded.Value = true;
        string resultMessage = "";

        switch (reason)
        {
            case WinReason.TimeOut: resultMessage = "Explorers Survived! (Time Ran Out)"; break;
            case WinReason.TeamWipe: resultMessage = "Demon Wins! (No Survivors)"; break;
            case WinReason.DemonSlain: resultMessage = "Explorers Win! (Demon Slain)"; break;
        }

        EndMatchClientRpc(resultMessage);
        Invoke(nameof(ResetMatch), 10f);
    }

    [ClientRpc]
    private void EndMatchClientRpc(string resultMessage)
    {
        // --- OPTIMIZED: Use cached overlay instead of finding it ---
        if (_cachedOverlay == null) _cachedOverlay = FindObjectOfType<MatchResultOverlay>(true);
        
        if (_cachedOverlay != null)
        {
            _cachedOverlay.ShowResultDirectly(resultMessage);
        }
    }

    // --- GLOBAL HANDSHAKE: BROADCAST TAUNTS ---
    [ServerRpc(RequireOwnership = false)]
    public void BroadcastTauntServerRpc(ulong demonId, int type)
    {
        // --- OPTIMIZED: Use cached girl sequence ---
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
        // --- OPTIMIZED: Use cached girl sequence ---
        foreach (var girl in _allGirlComponents)
        {
            if (girl != null && girl.NetworkObjectId == demonId)
            {
                girl.PlayLocalTaunt(type);
                break;
            }
        }
    }

    private void ResetMatch()
    {
        if (!IsServer) return;
        // For now, let's just reload the scene or simple reset
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
    private void SpawnMonsters()
    {
        if (monsterPrefabs == null || monsterPrefabs.Count == 0)
        {
            Debug.LogWarning("[SERVER] No Monster Prefabs assigned! Skipping monster spawn.");
            return;
        }

        for (int i = 0; i < monsterCount; i++)
        {
            // Pick a random monster type!
            GameObject randomMonsterPrefab = monsterPrefabs[Random.Range(0, monsterPrefabs.Count)];
            if (randomMonsterPrefab == null) continue;

            // Spawn them in a radius of 20 meters from the center, high in the sky!
            Vector2 randomCircle = Random.insideUnitCircle * 20f;
            Vector3 spawnPos = new Vector3(randomCircle.x, spawnHeight, randomCircle.y);

            GameObject monsterObj = Instantiate(randomMonsterPrefab, spawnPos, Quaternion.identity);
            NetworkObject netObj = monsterObj.GetComponent<NetworkObject>();

            if (netObj != null)
            {
                netObj.Spawn(); // Simple Spawn() for non-player AI
                
                // Track for command system
                if (monsterObj.TryGetComponent<MonsterAI>(out MonsterAI ai))
                {
                    _activeMonsters.Add(ai);
                }

                Debug.Log($"[SERVER] Spawned AI Monster {i + 1}/{monsterCount} at {spawnPos}");
            }
        }
    }

    // Called by the Girl via ServerRpc to lead her army
    public void CommandAllMonsters(MonsterAI.Command command)
    {
        if (!IsServer) return;

        Debug.Log($"[SERVER] Girl is commanding monsters to: {command}");
        foreach (var monster in _activeMonsters)
        {
            if (monster != null) monster.currentCommand = command;
        }
    }

    private GameObject GetRandomExplorerPrefab()
    {
        if (explorerPrefabs == null || explorerPrefabs.Count == 0) return null;
        return explorerPrefabs[Random.Range(0, explorerPrefabs.Count)];
    }
}