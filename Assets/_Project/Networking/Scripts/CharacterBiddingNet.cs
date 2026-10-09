using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Network model for Character Selection Bidding.
/// Synchronizes character ownership, bids, and yield statuses across all players in the lobby.
/// </summary>
[DisallowMultipleComponent]
public class CharacterBiddingNet : NetworkBehaviour
{
    private static CharacterBiddingNet _instance;
    public static CharacterBiddingNet Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<CharacterBiddingNet>(FindObjectsInactive.Include);
            }
            return _instance;
        }
        private set => _instance = value;
    }

    // Client-side cache: characterIndex -> (ownerClientId, ownerPlayerName, currentBid)
    private readonly Dictionary<int, (ulong ownerId, string ownerName, int bid)> _characterBids = new Dictionary<int, (ulong, string, int)>();

    // Client-side yielded set: characters local client yielded on
    private readonly HashSet<int> _localYieldedCharacters = new HashSet<int>();

    // Server-side yielded sets: characterIndex -> set of clientIds who yielded
    private readonly Dictionary<int, HashSet<ulong>> _serverYieldedMap = new Dictionary<int, HashSet<ulong>>();

    // Events
    public event Action<int, ulong, string, int> OnCharacterBidUpdated; // (charIndex, ownerId, ownerName, bid)
    public event Action<int, int, string> OnLocalOutbid;                 // (charIndex, newBid, outbidderName)
    public event Action<int> OnLocalYieldConfirmed;                      // (charIndex)

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            }
        }
        else
        {
            RequestSyncServerRpc();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }
        base.OnNetworkDespawn();
    }

    private void HandleClientConnected(ulong clientId)
    {
        BroadcastFullSyncToClients();
    }

    // =========================================================================
    //  Public Queries
    // =========================================================================

    public bool HasLocalYielded(int characterIndex)
    {
        return _localYieldedCharacters.Contains(characterIndex);
    }

    public bool TryGetCharacterOwner(int characterIndex, out ulong ownerId, out string ownerName, out int currentBid)
    {
        if (_characterBids.TryGetValue(characterIndex, out var data))
        {
            ownerId = data.ownerId;
            ownerName = data.ownerName;
            currentBid = data.bid;
            return true;
        }
        ownerId = ulong.MaxValue;
        ownerName = string.Empty;
        currentBid = 0;
        return false;
    }

    public int GetCurrentBid(int characterIndex)
    {
        return _characterBids.TryGetValue(characterIndex, out var data) ? data.bid : 0;
    }

    public ulong GetOwnerClientId(int characterIndex)
    {
        return _characterBids.TryGetValue(characterIndex, out var data) ? data.ownerId : ulong.MaxValue;
    }

    public string GetOwnerName(int characterIndex)
    {
        return _characterBids.TryGetValue(characterIndex, out var data) ? data.ownerName : string.Empty;
    }

    // =========================================================================
    //  RPC Actions
    // =========================================================================

    /// <summary>
    /// Initial claim when a player selects a character that has no owner yet.
    /// </summary>
    public void ClaimCharacterInitial(int characterIndex, string playerName)
    {
        if (!IsSpawned) return;
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        RequestClaimServerRpc(characterIndex, localId, new FixedString64Bytes(playerName));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestClaimServerRpc(int characterIndex, ulong clientId, FixedString64Bytes playerName, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        // Verify character doesn't already have an active owner
        if (_characterBids.TryGetValue(characterIndex, out var current) && current.ownerId != ulong.MaxValue && current.ownerId != senderId)
        {
            // Already owned by someone else! Reject blind claim; they must outbid.
            return;
        }

        // Check if sender previously yielded on this character
        if (_serverYieldedMap.TryGetValue(characterIndex, out var yields) && yields.Contains(senderId))
        {
            return;
        }

        string pName = playerName.ToString();
        _characterBids[characterIndex] = (senderId, pName, 0);
        BroadcastCharacterClaimClientRpc(characterIndex, senderId, playerName, 0);
    }

    /// <summary>
    /// Submit a competitive bid on a character.
    /// </summary>
    public void PlaceBid(int characterIndex, int bidAmount, string playerName)
    {
        if (!IsSpawned)
        {
            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            _characterBids[characterIndex] = (localId, playerName, bidAmount);
            OnCharacterBidUpdated?.Invoke(characterIndex, localId, playerName, bidAmount);
            return;
        }
        ulong lId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        SubmitBidServerRpc(characterIndex, bidAmount, lId, new FixedString64Bytes(playerName));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitBidServerRpc(int characterIndex, int bidAmount, ulong clientId, FixedString64Bytes playerName, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // Yield check
        if (_serverYieldedMap.TryGetValue(characterIndex, out var yields) && yields.Contains(senderId))
        {
            return;
        }

        int curBid = 0;
        ulong previousOwner = ulong.MaxValue;
        if (_characterBids.TryGetValue(characterIndex, out var existing))
        {
            curBid = existing.bid;
            previousOwner = existing.ownerId;
        }

        // Must strictly exceed current bid and be at least 2 Cinders
        if (bidAmount <= curBid || bidAmount < 2)
        {
            return;
        }

        string pName = playerName.ToString();
        _characterBids[characterIndex] = (senderId, pName, bidAmount);

        // Notify all clients of new ownership & bid
        BroadcastCharacterClaimClientRpc(characterIndex, senderId, playerName, bidAmount);

        // Notify previous owner they were outbid
        if (previousOwner != ulong.MaxValue && previousOwner != senderId)
        {
            NotifyOutbidClientRpc(characterIndex, previousOwner, bidAmount, playerName);
        }
    }

    /// <summary>
    /// Local player yields on this character.
    /// </summary>
    public void YieldCharacter(int characterIndex)
    {
        if (!IsSpawned)
        {
            _localYieldedCharacters.Add(characterIndex);
            OnLocalYieldConfirmed?.Invoke(characterIndex);
            return;
        }
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        SubmitYieldServerRpc(characterIndex, localId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitYieldServerRpc(int characterIndex, ulong clientId, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!_serverYieldedMap.ContainsKey(characterIndex))
            _serverYieldedMap[characterIndex] = new HashSet<ulong>();

        _serverYieldedMap[characterIndex].Add(senderId);

        ConfirmYieldClientRpc(characterIndex, senderId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ConfirmYieldClientRpc(int characterIndex, ulong yieldingClientId)
    {
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        if (localId == yieldingClientId)
        {
            _localYieldedCharacters.Add(characterIndex);
            OnLocalYieldConfirmed?.Invoke(characterIndex);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastCharacterClaimClientRpc(int characterIndex, ulong ownerId, FixedString64Bytes ownerName, int bid)
    {
        string name = ownerName.ToString();
        _characterBids[characterIndex] = (ownerId, name, bid);
        OnCharacterBidUpdated?.Invoke(characterIndex, ownerId, name, bid);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyOutbidClientRpc(int characterIndex, ulong outbidClientId, int newBid, FixedString64Bytes outbidderName)
    {
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        if (localId == outbidClientId)
        {
            OnLocalOutbid?.Invoke(characterIndex, newBid, outbidderName.ToString());
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSyncServerRpc(RpcParams rpcParams = default)
    {
        BroadcastFullSyncToClients();
    }

    private void BroadcastFullSyncToClients()
    {
        if (!IsServer) return;

        int count = _characterBids.Count;
        if (count == 0) return;

        int[] indices = new int[count];
        ulong[] owners = new ulong[count];
        FixedString64Bytes[] names = new FixedString64Bytes[count];
        int[] bids = new int[count];

        int i = 0;
        foreach (var kvp in _characterBids)
        {
            indices[i] = kvp.Key;
            owners[i] = kvp.Value.ownerId;
            names[i] = new FixedString64Bytes(kvp.Value.ownerName);
            bids[i] = kvp.Value.bid;
            i++;
        }

        SyncFullBidsClientRpc(indices, owners, names, bids);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncFullBidsClientRpc(int[] indices, ulong[] owners, FixedString64Bytes[] names, int[] bids)
    {
        if (indices == null) return;
        for (int i = 0; i < indices.Length; i++)
        {
            int idx = indices[i];
            ulong oId = owners[i];
            string oName = names[i].ToString();
            int b = bids[i];

            _characterBids[idx] = (oId, oName, b);
            OnCharacterBidUpdated?.Invoke(idx, oId, oName, b);
        }
    }
}
