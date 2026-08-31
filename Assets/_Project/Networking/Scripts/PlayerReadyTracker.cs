using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tracks the "ready" state of every player in the lobby and broadcasts it
/// to all clients so the status panel can be shown on every screen
/// (CharacterSelectUI for investigators, GirlPlayerScreen for the girl).
/// </summary>
[DisallowMultipleComponent]
public class PlayerReadyTracker : NetworkBehaviour
{
    public static PlayerReadyTracker Instance { get; private set; }

    // Fired on every client whenever the ready-state snapshot changes.
    // Key = clientId, Value = (playerName, isReady).
    public event System.Action<Dictionary<ulong, (string name, bool ready)>> OnReadyStatesUpdated;

    // Fired on the server when all players are ready.
    public event System.Action OnAllPlayersReady;

    // Server state
    private readonly Dictionary<ulong, bool>   _investigatorReady = new Dictionary<ulong, bool>();
    private readonly Dictionary<ulong, string> _playerNames       = new Dictionary<ulong, string>();
    private bool  _girlReady;
    private ulong _girlClientId = ulong.MaxValue;
    private bool  _trackingStarted;

    // Latest snapshot on all clients
    private readonly Dictionary<ulong, (string name, bool ready)> _snapshot
        = new Dictionary<ulong, (string name, bool ready)>();

    public bool AllPlayersReady => IsSnapshotAllReady();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================================
    //  Server API — called by GirlRevealManager.BeginReveal()
    // =========================================================================
    public void StartTracking(ulong girlClientId, List<ulong> allClientIds)
    {
        if (!IsServer) return;

        _girlClientId = girlClientId;
        _girlReady    = false;
        _investigatorReady.Clear();
        _playerNames.Clear();
        _trackingStarted = true;

        foreach (ulong id in allClientIds)
        {
            _playerNames[id] = ResolvePlayerName(id);
            if (id != girlClientId)
                _investigatorReady[id] = false;
        }

        BroadcastSnapshot();
    }

    // =========================================================================
    //  RPCs — Client -> Server
    // =========================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportInvestigatorConfirmedServerRpc(RpcParams rpcParams = default)
    {
        if (!_trackingStarted) return;
        ulong senderId = rpcParams.Receive.SenderClientId;
        _investigatorReady[senderId] = true;
        Debug.Log($"[PlayerReadyTracker] Investigator {senderId} confirmed.");
        BroadcastSnapshot();
        CheckAllReady();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportGirlReadyServerRpc(RpcParams rpcParams = default)
    {
        if (!_trackingStarted) return;
        _girlReady = true;
        Debug.Log("[PlayerReadyTracker] Girl ready.");
        BroadcastSnapshot();
        CheckAllReady();
    }

    // =========================================================================
    //  RPCs — Server -> All Clients
    // =========================================================================

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastReadyStatesClientRpc(ulong[] clientIds, string[] names, bool[] readyFlags)
    {
        _snapshot.Clear();
        for (int i = 0; i < clientIds.Length; i++)
            _snapshot[clientIds[i]] = (names[i], readyFlags[i]);
        OnReadyStatesUpdated?.Invoke(_snapshot);
    }

    // =========================================================================
    //  Private
    // =========================================================================
    private void BroadcastSnapshot()
    {
        if (!IsServer) return;
        var idList    = new List<ulong>();
        var nameList  = new List<string>();
        var readyList = new List<bool>();
        foreach (var kvp in _playerNames)
        {
            idList.Add(kvp.Key);
            nameList.Add(kvp.Value);
            if (kvp.Key == _girlClientId)
                readyList.Add(_girlReady);
            else
                readyList.Add(_investigatorReady.TryGetValue(kvp.Key, out bool r) && r);
        }
        BroadcastReadyStatesClientRpc(idList.ToArray(), nameList.ToArray(), readyList.ToArray());
    }

    private void CheckAllReady()
    {
        if (!IsServer) return;
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
