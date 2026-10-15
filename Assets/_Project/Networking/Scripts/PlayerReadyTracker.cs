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
    public int liarStrikes;
    public int traitorStrikes;
}

/// <summary>
/// Tracks the "ready" state, player level, and selected operative of every player
/// in the lobby and broadcasts it to all clients so the status panel can be shown on every screen
/// (CharacterSelectUI for investigators, GirlPlayerScreen for the girl).
/// </summary>
[DisallowMultipleComponent]
public class PlayerReadyTracker : NetworkBehaviour
{
    private static PlayerReadyTracker _instance;
    public static PlayerReadyTracker Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<PlayerReadyTracker>(FindObjectsInactive.Include);
            }
            return _instance;
        }
        private set => _instance = value;
    }

    // Fired on every client whenever the ready-state snapshot changes (legacy tuple).
    public event Action<Dictionary<ulong, (string name, bool ready)>> OnReadyStatesUpdated;

    // Fired on every client with rich info: (playerName, isReady, playerLevel, characterIndex, isGirl).
    public event Action<Dictionary<ulong, PlayerLobbyInfo>> OnPlayerLobbyStatesUpdated;

    // Fired on every client and host when all players are ready.
    public event Action OnAllPlayersReady;

    // Server state
    private readonly Dictionary<ulong, bool>   _investigatorReady = new Dictionary<ulong, bool>();
    private readonly Dictionary<ulong, string> _playerNames       = new Dictionary<ulong, string>();
    private readonly Dictionary<ulong, int>    _playerLevels      = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, int>    _characterIndices  = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, int>    _liarStrikes       = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, int>    _traitorStrikes    = new Dictionary<ulong, int>();
    private bool  _girlReady;
    private ulong _girlClientId = ulong.MaxValue;
    private bool  _trackingStarted;

    // Latest snapshot on all clients
    private readonly Dictionary<ulong, (string name, bool ready)> _snapshot
        = new Dictionary<ulong, (string name, bool ready)>();

    private readonly Dictionary<ulong, PlayerLobbyInfo> _lobbySnapshot
        = new Dictionary<ulong, PlayerLobbyInfo>();

    public bool AllPlayersReady => IsSnapshotAllReady();

    public int ReadyCount
    {
        get
        {
            int count = 0;
            foreach (var kvp in _lobbySnapshot)
                if (kvp.Value.isReady) count++;
            return count;
        }
    }

    public int TotalCount => _lobbySnapshot.Count;

    public Dictionary<ulong, PlayerLobbyInfo> GetLobbySnapshot() => _lobbySnapshot;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
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
            _liarStrikes.Clear();
            _traitorStrikes.Clear();
            _trackingStarted = true;

            foreach (ulong id in allClientIds)
            {
                _playerNames[id] = ResolvePlayerName(id);
                _playerLevels[id] = 1;
                _characterIndices[id] = (id == girlClientId) ? -1 : 0;
                _liarStrikes[id] = 0;
                _traitorStrikes[id] = 0;
                if (id != girlClientId)
                    _investigatorReady[id] = false;
            }

            BroadcastSnapshot();
        }
    }

    // =========================================================================
    //  Public API (Safe for Spawned / Unspawned / Offline calls)
    // =========================================================================
    public void ReportInvestigatorConfirmed(ulong clientId = 0, int playerLevel = 1, int characterIndex = 0, int liarStrikes = 0, int traitorStrikes = 0)
    {
        if (liarStrikes == 0 && traitorStrikes == 0 && CloudCharacterSaveManager.Instance != null)
        {
            liarStrikes = CloudCharacterSaveManager.Instance.LiarStrikes;
            traitorStrikes = CloudCharacterSaveManager.Instance.TraitorStrikes;
        }

        if (IsSpawned)
        {
            ReportInvestigatorConfirmedServerRpc(playerLevel, characterIndex, liarStrikes, traitorStrikes);
        }
        else
        {
            _investigatorReady[clientId] = true;
            _playerLevels[clientId] = playerLevel;
            _characterIndices[clientId] = characterIndex;
            _liarStrikes[clientId] = liarStrikes;
            _traitorStrikes[clientId] = traitorStrikes;

            string pName = ResolvePlayerName(clientId);
            _snapshot[clientId] = (pName, true);
            _lobbySnapshot[clientId] = new PlayerLobbyInfo
            {
                clientId = clientId,
                playerName = pName,
                isReady = true,
                playerLevel = playerLevel,
                characterIndex = characterIndex,
                isGirl = false,
                liarStrikes = liarStrikes,
                traitorStrikes = traitorStrikes
            };

            OnReadyStatesUpdated?.Invoke(_snapshot);
            OnPlayerLobbyStatesUpdated?.Invoke(_lobbySnapshot);
            Debug.Log($"[PlayerReadyTracker] Investigator {clientId} confirmed (unspawned/local).");
            CheckAllReady();
        }
    }

    public void ReportInvestigatorUnconfirmed(ulong clientId = 0)
    {
        if (IsSpawned)
        {
            ReportInvestigatorUnconfirmedServerRpc();
        }
        else
        {
            _investigatorReady[clientId] = false;
            string pName = ResolvePlayerName(clientId);
            _snapshot[clientId] = (pName, false);
            if (_lobbySnapshot.TryGetValue(clientId, out var info))
            {
                info.isReady = false;
                _lobbySnapshot[clientId] = info;
            }

            OnReadyStatesUpdated?.Invoke(_snapshot);
            OnPlayerLobbyStatesUpdated?.Invoke(_lobbySnapshot);
            Debug.Log($"[PlayerReadyTracker] Investigator {clientId} unconfirmed (unspawned/local).");
            CheckAllReady();
        }
    }

    public void ReportGirlReady(int playerLevel = 1, int liarStrikes = 0, int traitorStrikes = 0)
    {
        if (liarStrikes == 0 && traitorStrikes == 0 && CloudCharacterSaveManager.Instance != null)
        {
            liarStrikes = CloudCharacterSaveManager.Instance.LiarStrikes;
            traitorStrikes = CloudCharacterSaveManager.Instance.TraitorStrikes;
        }

        if (IsSpawned)
        {
            ReportGirlReadyServerRpc(playerLevel, liarStrikes, traitorStrikes);
        }
        else
        {
            _girlReady = true;
            ulong girlId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            _playerLevels[girlId] = playerLevel;
            _characterIndices[girlId] = -1;
            _liarStrikes[girlId] = liarStrikes;
            _traitorStrikes[girlId] = traitorStrikes;

            string gName = ResolvePlayerName(girlId);
            _snapshot[girlId] = (gName, true);
            _lobbySnapshot[girlId] = new PlayerLobbyInfo
            {
                clientId = girlId,
                playerName = gName,
                isReady = true,
                playerLevel = playerLevel,
                characterIndex = -1,
                isGirl = true,
                liarStrikes = liarStrikes,
                traitorStrikes = traitorStrikes
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
    public void ReportInvestigatorConfirmedServerRpc(int playerLevel, int characterIndex, int liarStrikes = 0, int traitorStrikes = 0, RpcParams rpcParams = default)
    {
        if (!_trackingStarted) return;
        ulong senderId = rpcParams.Receive.SenderClientId;
        _investigatorReady[senderId] = true;
        _playerLevels[senderId] = playerLevel;
        _characterIndices[senderId] = characterIndex;
        _liarStrikes[senderId] = liarStrikes;
        _traitorStrikes[senderId] = traitorStrikes;
        Debug.Log($"[PlayerReadyTracker] Investigator {senderId} confirmed (Lv. {playerLevel}, Operative: {characterIndex}, Liar: {liarStrikes}, Traitor: {traitorStrikes}).");
        BroadcastSnapshot();
        CheckAllReady();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportInvestigatorUnconfirmedServerRpc(RpcParams rpcParams = default)
    {
        if (!_trackingStarted) return;
        ulong senderId = rpcParams.Receive.SenderClientId;
        _investigatorReady[senderId] = false;
        Debug.Log($"[PlayerReadyTracker] Investigator {senderId} unconfirmed.");
        BroadcastSnapshot();
        CheckAllReady();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportGirlReadyServerRpc(int playerLevel, int liarStrikes = 0, int traitorStrikes = 0, RpcParams rpcParams = default)
    {
        if (!_trackingStarted) return;
        ulong senderId = rpcParams.Receive.SenderClientId;
        _girlReady = true;
        _playerLevels[senderId] = playerLevel;
        _characterIndices[senderId] = -1;
        _liarStrikes[senderId] = liarStrikes;
        _traitorStrikes[senderId] = traitorStrikes;
        Debug.Log($"[PlayerReadyTracker] Girl {senderId} ready (Lv. {playerLevel}, Liar: {liarStrikes}, Traitor: {traitorStrikes}).");
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
        int[] characterIndices,
        int[] liarStrikes,
        int[] traitorStrikes)
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
            int lStrikes = (liarStrikes != null && i < liarStrikes.Length) ? liarStrikes[i] : 0;
            int tStrikes = (traitorStrikes != null && i < traitorStrikes.Length) ? traitorStrikes[i] : 0;
            bool isGirl = (cid == _girlClientId);

            _snapshot[cid] = (n, r);
            _lobbySnapshot[cid] = new PlayerLobbyInfo
            {
                clientId = cid,
                playerName = n,
                isReady = r,
                playerLevel = lvl,
                characterIndex = cIdx,
                isGirl = isGirl,
                liarStrikes = lStrikes,
                traitorStrikes = tStrikes
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
        var idList      = new List<ulong>();
        var nameList    = new List<FixedString64Bytes>();
        var readyList   = new List<bool>();
        var levelList   = new List<int>();
        var charList    = new List<int>();
        var liarList    = new List<int>();
        var traitorList = new List<int>();

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
            liarList.Add(_liarStrikes.TryGetValue(kvp.Key, out int lStr) ? lStr : 0);
            traitorList.Add(_traitorStrikes.TryGetValue(kvp.Key, out int tStr) ? tStr : 0);
        }

        BroadcastReadyStatesClientRpc(
            idList.ToArray(),
            nameList.ToArray(),
            readyList.ToArray(),
            levelList.ToArray(),
            charList.ToArray(),
            liarList.ToArray(),
            traitorList.ToArray());
    }

    private void CheckAllReady()
    {
        if (IsSpawned && !_trackingStarted) return;
        if (_investigatorReady.Count == 0 && _girlClientId == ulong.MaxValue) return;

        bool hasInvestigators = (_investigatorReady.Count > 0);
        bool investigatorsDone = hasInvestigators;
        foreach (var kvp in _investigatorReady)
            if (!kvp.Value) { investigatorsDone = false; break; }

        bool girlDone = (_girlClientId == ulong.MaxValue) || _girlReady;

        if (investigatorsDone && girlDone)
        {
            Debug.Log("[PlayerReadyTracker] All players ready!");
            if (IsServer && IsSpawned)
            {
                BroadcastAllPlayersReadyClientRpc();
            }
            else
            {
                OnAllPlayersReady?.Invoke();
            }

            if (GirlRevealManager.Instance != null)
                GirlRevealManager.Instance.OnAllTrackerPlayersReady();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastAllPlayersReadyClientRpc()
    {
        Debug.Log("[PlayerReadyTracker] All players confirmed ready across network.");
        OnAllPlayersReady?.Invoke();
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
                if (nameComp != null && !string.IsNullOrEmpty(nameComp.playerName.Value.ToString()))
                    return nameComp.playerName.Value.ToString();
            }
        }
        if (clientId == NetworkManager.Singleton.LocalClientId)
            return PlayerNameManager.GetPlayerName();

        string nameFallback = PlayerNameManager.GetPlayerName(clientId);
        if (!string.IsNullOrEmpty(nameFallback) && !nameFallback.StartsWith("Player "))
            return nameFallback;

        return $"Player {clientId % 1000}";
    }
}
