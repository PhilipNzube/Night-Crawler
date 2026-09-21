using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Data container representing a player's full status in the lobby lineup.
/// </summary>
[Serializable]
public struct PlayerLobbyInfo
{
    public ulong clientId;
    public string playerName;
    public bool isReady;
    public int playerLevel;
    public int characterIndex; // Operative roster index, or -1 for the Girl
    public bool isGirl;
}

/// <summary>
/// Tracks the "ready" state, player level, and selected operative of every player
/// in the lobby and broadcasts it to all clients so the status panel can be shown on every screen
/// (CharacterSelectUI for investigators, GirlPlayerScreen for the girl).
/// </summary>
[DisallowMultipleComponent]
public class PlayerReadyTracker : NetworkBehaviour
{
    public static PlayerReadyTracker Instance { get; private set; }

    // Fired on every client whenever the ready-state snapshot changes (legacy tuple).
    public event Action<Dictionary<ulong, (string name, bool ready)>> OnReadyStatesUpdated;

    // Fired on every client with rich info: (playerName, isReady, playerLevel, characterIndex, isGirl).
    public event Action<Dictionary<ulong, PlayerLobbyInfo>> OnPlayerLobbyStatesUpdated;

    // Fired on the server when all players are ready.
    public event Action OnAllPlayersReady;

    // Server state
    private readonly Dictionary<ulong, bool>   _investigatorReady = new Dictionary<ulong, bool>();
    private readonly Dictionary<ulong, string> _playerNames       = new Dictionary<ulong, string>();
    private readonly Dictionary<ulong, int>    _playerLevels      = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, int>    _characterIndices  = new Dictionary<ulong, int>();
    private bool  _girlReady;
    private ulong _girlClientId = ulong.MaxValue;
    private bool  _trackingStarted;

    // Latest snapshot on all clients
    private readonly Dictionary<ulong, (string name, bool ready)> _snapshot
        = new Dictionary<ulong, (string name, bool ready)>();

    private readonly Dictionary<ulong, PlayerLobbyInfo> _lobbySnapshot
        = new Dictionary<ulong, PlayerLobbyInfo>();

    public bool AllPlayersReady => IsSnapshotAllReady();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================================
    //  Server API - called by GirlRevealManager.BeginReveal()
    // =========================================================================
    public void StartTracking(ulong girlClientId, List<ulong> allClientIds)
    {
        if (IsServer)
        {
            _girlClientId = girlClientId;
            _girlReady    = false;
            _investigatorReady.Clear();
            _playerNames.Clear();
            _playerLevels.Clear();
            _characterIndices.Clear();
            _trackingStarted = true;

            foreach (ulong id in allClientIds)
            {
                _playerNames[id] = ResolvePlayerName(id);
                _playerLevels[id] = 1;
                _characterIndices[id] = (id == girlClientId) ? -1 : 0;
                if (id != girlClientId)
                    _investigatorReady[id] = false;
            }

            BroadcastSnapshot();
        }
    }

    // =========================================================================
    //  Public API (Safe for Spawned / Unspawned / Offline calls)
    // =========================================================================
    public void ReportInvestigatorConfirmed(ulong clientId = 0, int playerLevel = 1, int characterIndex = 0)
    {
        if (IsSpawned)
        {
            ReportInvestigatorConfirmedServerRpc(playerLevel, characterIndex);
        }
        else
        {
            _investigatorReady[clientId] = true;
            _playerLevels[clientId] = playerLevel;
            _characterIndices[clientId] = characterIndex;

            string pName = ResolvePlayerName(clientId);
            _snapshot[clientId] = (pName, true);
            _lobbySnapshot[clientId] = new PlayerLobbyInfo
            {
                clientId = clientId,
                playerName = pName,
                isReady = true,
                playerLevel = playerLevel,
                characterIndex = characterIndex,
                isGirl = false
            };

            OnReadyStatesUpdated?.Invoke(_snapshot);
            OnPlayerLobbyStatesUpdated?.Invoke(_lobbySnapshot);
            Debug.Log($"[PlayerReadyTracker] Investigator {clientId} confirmed (unspawned/local).");
            CheckAllReady();
        }
    }

    public void ReportGirlReady(int playerLevel = 1)
    {
        if (IsSpawned)
        {
            ReportGirlReadyServerRpc(playerLevel);
        }
        else
        {
            _girlReady = true;
            ulong girlId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            _playerLevels[girlId] = playerLevel;
            _characterIndices[girlId] = -1;

            string gName = ResolvePlayerName(girlId);
            _snapshot[girlId] = (gName, true);
            _lobbySnapshot[girlId] = new PlayerLobbyInfo
            {
                clientId = girlId,
                playerName = gName,
                isReady = true,
                playerLevel = playerLevel,
                characterIndex = -1,
                isGirl = true
            };

            OnReadyStatesUpdated?.Invoke(_snapshot);
            OnPlayerLobbyStatesUpdated?.Invoke(_lobbySnapshot);
            Debug.Log("[PlayerReadyTracker] Girl ready (unspawned/local).");
            CheckAllReady();
        }
    }

    // =========================================================================
    //  RPCs - Client -> Server
    // =========================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportInvestigatorConfirmedServerRpc(int playerLevel, int characterIndex, RpcParams rpcParams = default)
    {
        if (!_trackingStarted) return;
        ulong senderId = rpcParams.Receive.SenderClientId;
        _investigatorReady[senderId] = true;
        _playerLevels[senderId] = playerLevel;
        _characterIndices[senderId] = characterIndex;
        Debug.Log($"[PlayerReadyTracker] Investigator {senderId} confirmed (Lv. {playerLevel}, Operative: {characterIndex}).");
        BroadcastSnapshot();
        CheckAllReady();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportGirlReadyServerRpc(int playerLevel, RpcParams rpcParams = default)
    {
        if (!_trackingStarted) return;
        ulong senderId = rpcParams.Receive.SenderClientId;
        _girlReady = true;
        _playerLevels[senderId] = playerLevel;
        _characterIndices[senderId] = -1;
        Debug.Log($"[PlayerReadyTracker] Girl {senderId} ready (Lv. {playerLevel}).");
        BroadcastSnapshot();
        CheckAllReady();
    }

    // =========================================================================
    //  RPCs - Server -> All Clients
    // =========================================================================

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastReadyStatesClientRpc(
        ulong[] clientIds,
        FixedString64Bytes[] names,
        bool[] readyFlags,
        int[] playerLevels,
        int[] characterIndices)
    {
        _snapshot.Clear();
        _lobbySnapshot.Clear();

        for (int i = 0; i < clientIds.Length; i++)
        {
            ulong cid = clientIds[i];
            string n = names[i].ToString();
            bool r = readyFlags[i];
            int lvl = (playerLevels != null && i < playerLevels.Length) ? playerLevels[i] : 1;
            int cIdx = (characterIndices != null && i < characterIndices.Length) ? characterIndices[i] : 0;
            bool isGirl = (cid == _girlClientId);

            _snapshot[cid] = (n, r);
            _lobbySnapshot[cid] = new PlayerLobbyInfo
            {
                clientId = cid,
                playerName = n,
                isReady = r,
                playerLevel = lvl,
                characterIndex = cIdx,
                isGirl = isGirl
            };
        }

        OnReadyStatesUpdated?.Invoke(_snapshot);
        OnPlayerLobbyStatesUpdated?.Invoke(_lobbySnapshot);
    }

    // =========================================================================
    //  Private
    // =========================================================================
    private void BroadcastSnapshot()
    {
        if (!IsServer || !IsSpawned) return;
        var idList    = new List<ulong>();
        var nameList  = new List<FixedString64Bytes>();
        var readyList = new List<bool>();
        var levelList = new List<int>();
        var charList  = new List<int>();

        foreach (var kvp in _playerNames)
        {
            idList.Add(kvp.Key);
            string n = kvp.Value.Length > 63 ? kvp.Value.Substring(0, 63) : kvp.Value;
            nameList.Add(new FixedString64Bytes(n));

            bool isGirl = (kvp.Key == _girlClientId);
            if (isGirl)
                readyList.Add(_girlReady);
            else
                readyList.Add(_investigatorReady.TryGetValue(kvp.Key, out bool r) && r);

            levelList.Add(_playerLevels.TryGetValue(kvp.Key, out int lvl) ? lvl : 1);
            charList.Add(_characterIndices.TryGetValue(kvp.Key, out int cIdx) ? cIdx : (isGirl ? -1 : 0));
        }

        BroadcastReadyStatesClientRpc(
            idList.ToArray(),
            nameList.ToArray(),
            readyList.ToArray(),
            levelList.ToArray(),
            charList.ToArray());
    }

    private void CheckAllReady()
    {
        bool investigatorsDone = true;
        foreach (var kvp in _investigatorReady)
            if (!kvp.Value) { investigatorsDone = false; break; }

        bool girlDone = (_girlClientId == ulong.MaxValue) || _girlReady;

        if (investigatorsDone && girlDone)
        {
            Debug.Log("[PlayerReadyTracker] All players ready!");
            OnAllPlayersReady?.Invoke();
            if (GirlRevealManager.Instance != null)
                GirlRevealManager.Instance.OnAllTrackerPlayersReady();
        }
    }

    private bool IsSnapshotAllReady()
    {
        if (_snapshot.Count == 0) return false;
        foreach (var kvp in _snapshot)
            if (!kvp.Value.ready) return false;
        return true;
    }

    private string ResolvePlayerName(ulong clientId)
    {
        string registered = GirlRevealManager.GetRegisteredPlayerName(clientId);
        if (!string.IsNullOrEmpty(registered) && !registered.StartsWith("Player "))
        {
            return registered;
        }

        if (NetworkManager.Singleton == null) return $"Player {clientId % 1000}";
        if (NetworkManager.Singleton.IsServer)
        {
            var netObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (netObj != null)
            {
                var nameComp = netObj.GetComponent<NetworkPlayerName>();
                if (nameComp != null) return nameComp.playerName.Value.ToString();
            }
        }
        if (clientId == NetworkManager.Singleton.LocalClientId)
            return PlayerNameManager.GetPlayerName();
        return $"Player {clientId % 1000}";
    }
}
