using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Manages random Vengeful Spirit role assignment and Investigator character selection.
///
/// Flow (per Game Design Document "The Mine"):
///   1. Server randomly selects 1 connected player as the Vengeful Spirit.
///   2. Vengeful Spirit receives secret role notification.
///   3. Remaining players choose their Investigator profession.
/// </summary>
public class CharacterSelectManager : NetworkBehaviour
{
    public static CharacterSelectManager Instance { get; private set; }

    [Header("Available Investigator Characters")]
    public List<InvestigatorCharacterData> availableCharacters = new List<InvestigatorCharacterData>();

    // -------------------------------------------------------------------------
    //  Network State
    // -------------------------------------------------------------------------
    public NetworkVariable<ulong> vengefulSpiritClientId = new NetworkVariable<ulong>(
        999,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> roleSelectionDone = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Track selected character index per clientId
    private Dictionary<ulong, int> _playerCharacterChoices = new Dictionary<ulong, int>();

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        PopulateDefaultCharactersIfEmpty();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            SelectRandomVengefulSpirit();
        }
    }

    // =========================================================================
    //  Role Selection (Server)
    // =========================================================================
    public void SelectRandomVengefulSpirit()
    {
        if (!IsServer) return;

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        if (clientIds.Count == 0) return;

        int randomIndex = Random.Range(0, clientIds.Count);
        vengefulSpiritClientId.Value = clientIds[randomIndex];
        roleSelectionDone.Value = true;

        Debug.Log($"[CharacterSelectManager] Client {vengefulSpiritClientId.Value} secretively chosen as Vengeful Spirit!");
    }

    // =========================================================================
    //  Character Choice API
    // =========================================================================
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSelectCharacterServerRpc(int characterIndex, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        _playerCharacterChoices[senderId] = characterIndex;
        Debug.Log($"[CharacterSelectManager] Client {senderId} selected character index {characterIndex}.");
    }

    public int GetSelectedCharacterIndex(ulong clientId)
    {
        if (_playerCharacterChoices.TryGetValue(clientId, out int idx))
            return idx;
        return 0;
    }

    public GameObject GetInvestigatorPrefab(int index)
    {
        if (availableCharacters != null && index >= 0 && index < availableCharacters.Count)
        {
            if (availableCharacters[index].characterPrefab != null)
                return availableCharacters[index].characterPrefab;
        }
        return null;
    }

    // =========================================================================
    //  Default Fallback Characters (Prevents crashes if empty)
    // =========================================================================
    private void PopulateDefaultCharactersIfEmpty()
    {
        if (availableCharacters != null && availableCharacters.Count > 0) return;

        availableCharacters = new List<InvestigatorCharacterData>
        {
            new InvestigatorCharacterData
            {
                characterName = "Mine Worker",
                profession = InvestigatorProfession.MineWorker,
                description = "Understands mine structures, machinery, and practical underground problems.",
                specialAbilities = "• Heavy Pickaxe Attack\n• Structural Inspection\n• Machine Repair"
            },
            new InvestigatorCharacterData
            {
                characterName = "Hazard Specialist",
                profession = InvestigatorProfession.HazardSpecialist,
                description = "Wears a protective suit to handle environmental hazards and toxic gas without panic.",
                specialAbilities = "• Toxic Gas Immunity\n• Hazard Filter Deployment\n• Heavy Armor"
            },
            new InvestigatorCharacterData
            {
                characterName = "Explorer",
                profession = InvestigatorProfession.Explorer,
                description = "Experienced with underground navigation, rappelling, and difficult terrain.",
                specialAbilities = "• Stamina Boost\n• Terrain Traversal\n• Flare Marker"
            },
            new InvestigatorCharacterData
            {
                characterName = "Cursed Priest",
                profession = InvestigatorProfession.CursedPriest,
                description = "Supernatural specialist whose unsettling presence makes the team wonder why he joined.",
                specialAbilities = "• Occult Sensing\n• Ward Placement\n• Presence Detection"
            },
            new InvestigatorCharacterData
            {
                characterName = "Field Medic",
                profession = InvestigatorProfession.FieldMedic,
                description = "Examines injuries and determines if deaths were caused by accidents or violence.",
                specialAbilities = "• First Aid Healing\n• Autopsy Examination\n• Revive Assistance"
            }
        };
    }
}
