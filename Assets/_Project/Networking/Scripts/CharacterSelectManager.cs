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

    [Header("Roster Configuration")]
    [Tooltip("Toggle whether Hazard Specialist is included in character selection. Defaults to false.")]
    public bool includeHazardSpecialist = false;

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

    // Static persistence across scene loads
    private static readonly Dictionary<ulong, int> s_SavedChoices = new Dictionary<ulong, int>();
    private static ulong s_SavedVengefulSpirit = 999;
    private static bool s_SavedRoleSelectionDone = false;

    public static ulong SavedVengefulSpiritClientId => s_SavedVengefulSpirit;
    public static bool SavedRoleSelectionDone => s_SavedRoleSelectionDone;

    public static void SaveVengefulSpiritRole(ulong clientId)
    {
        s_SavedVengefulSpirit = clientId;

        // ulong.MaxValue is the sentinel meaning "no girl / role cleared"
        if (clientId == ulong.MaxValue)
        {
            s_SavedRoleSelectionDone = false;
            if (Instance != null && Instance.IsServer)
            {
                Instance.vengefulSpiritClientId.Value = 999;
                Instance.roleSelectionDone.Value      = false;
            }
            Debug.Log("[CharacterSelectManager] Wraith role cleared (forceInvestigator or no selection).");
        }
        else
        {
            s_SavedRoleSelectionDone = true;
            if (Instance != null && Instance.IsServer)
            {
                Instance.vengefulSpiritClientId.Value = clientId;
                Instance.roleSelectionDone.Value      = true;
            }
            Debug.Log($"[CharacterSelectManager] Persistent Wraith role saved for Client {clientId}.");
        }
    }

    public static void SetChoiceStatic(ulong clientId, int index)
    {
        s_SavedChoices[clientId] = index;
        if (Instance != null)
            Instance._playerCharacterChoices[clientId] = index;
    }

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);

        PopulateDefaultCharactersIfEmpty();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // NOTE: SelectRandomVengefulSpirit() is intentionally NOT called here.
        // It is called by GirlRevealManager.BeginReveal() when the host presses
        // START MATCH, at which point all players are guaranteed to be connected.
        // Calling it at spawn time risked selecting before all clients joined.
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
        ulong chosenId = clientIds[randomIndex];
        vengefulSpiritClientId.Value = chosenId;
        roleSelectionDone.Value      = true;

        s_SavedVengefulSpirit    = chosenId;
        s_SavedRoleSelectionDone = true;

        Debug.Log($"[CharacterSelectManager] {clientIds.Count} players connected. " +
                  $"Client {chosenId} selected as Wraith.");
    }

    // =========================================================================
    //  Character Choice API
    // =========================================================================
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSelectCharacterServerRpc(int characterIndex, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        _playerCharacterChoices[senderId] = characterIndex;
        s_SavedChoices[senderId] = characterIndex;
        Debug.Log($"[CharacterSelectManager] Client {senderId} selected character index {characterIndex}.");
    }

    public int GetSelectedCharacterIndex(ulong clientId)
    {
        if (_playerCharacterChoices.TryGetValue(clientId, out int idx))
            return idx;
        if (s_SavedChoices.TryGetValue(clientId, out int savedIdx))
            return savedIdx;
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
            return PersistentCharacterSelection.GetSelectedCharacterIndex();
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
                characterName = "Breaker",
                profession = InvestigatorProfession.MineWorker,
                description = "Speaks fluent pickaxe. Overtime approved.",
                specialAbilities = "• Heavy Pickaxe Attack\n• Structural Inspection\n• Machine Repair"
            },
            new InvestigatorCharacterData
            {
                characterName = "Hazard Specialist",
                profession = InvestigatorProfession.HazardSpecialist,
                description = "Allergic to dying. Prays the suit holds.",
                specialAbilities = "• Toxic Gas Immunity\n• Hazard Filter Deployment\n• Heavy Armor"
            },
            new InvestigatorCharacterData
            {
                characterName = "Pathfinder",
                profession = InvestigatorProfession.Explorer,
                description = "Fourteen caves mapped. Zero escape plans.",
                specialAbilities = "• Stamina Boost\n• Terrain Traversal\n• Flare Marker"
            },
            new InvestigatorCharacterData
            {
                characterName = "Exorcist",
                profession = InvestigatorProfession.CursedPriest,
                description = "Nobody knows whose side he’s praying for.",
                specialAbilities = "• Occult Sensing\n• Ward Placement\n• Presence Detection"
            },
            new InvestigatorCharacterData
            {
                characterName = "Mender",
                profession = InvestigatorProfession.FieldMedic,
                description = "Stitches you up. Judges your life choices.",
                specialAbilities = "• First Aid Healing\n• Autopsy Examination\n• Revive Assistance"
            }
        };

        if (!includeHazardSpecialist)
        {
            availableCharacters.RemoveAll(c => c != null && (c.profession == InvestigatorProfession.HazardSpecialist || c.characterName.ToLower().Contains("hazard")));
        }
    }
}
