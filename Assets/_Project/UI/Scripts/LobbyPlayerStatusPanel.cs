using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NightCrawler.UI;

/// <summary>
/// Universal central controller for the Lobby Player Status Panel (PlayerStatusPanel).
/// Attached directly to PlayerStatusPanel in LobbyScene and shared by both
/// the Investigator Character Select screen and the Girl Player screen.
/// Manages instantiation of HeatPlayerStatusRow prefabs, keeps preset icons,
/// and sets ready / not-ready indicators using the prefab icons.
/// </summary>
[DisallowMultipleComponent]
public class LobbyPlayerStatusPanel : MonoBehaviour
{
    public static LobbyPlayerStatusPanel Instance { get; private set; }

    [Header("Container & Row Prefab")]
    [Tooltip("The parent container where player status rows are instantiated (StatusPanels/List).")]
    public Transform playerStatusContainer;

    [Tooltip("The HeatPlayerStatusRow prefab to instantiate for each player.")]
    public GameObject playerStatusRowPrefab;

    [Header("Optional Character Select Reference")]
    [Tooltip("Optional reference to CharacterSelectUI in the scene to pull character definitions.")]
    public CharacterSelectUI characterSelectUI;

    [Header("Player Role Names (Editable in Inspector)")]
    [Tooltip("Role name displayed in the subtext for the Miner / MineWorker.")]
    public string minerRoleName = "Miner";

    [Tooltip("Role name displayed in the subtext for the Cursed Priest.")]
    public string priestRoleName = "Priest";

    [Tooltip("Role name displayed in the subtext for the Field Medic.")]
    public string medicRoleName = "Medic";

    [Tooltip("Role name displayed in the subtext for the Explorer.")]
    public string explorerRoleName = "Explorer";

    [Tooltip("Role name displayed in the subtext for the Hazard Specialist.")]
    public string hazardRoleName = "Hazard Specialist";

    [Tooltip("Role name displayed in the subtext for the Wraith.")]
    public string spiritRoleName = "Wraith";

    [Tooltip("Fallback role name displayed if selection is pending or unknown.")]
    public string defaultRoleName = "Operative";

    [Header("Subtext Formatting")]
    [Tooltip("Format for the subtext label. {0} = Player Level, {1} = Role Name. Example: '{1}' for just role name, or 'Lv. {0} • {1}'")]
    public string subtextFormat = "{1}";

    [Header("Status Header")]
    [Tooltip("Text displaying 'Waiting for players...' or ready status. Auto-found on 'WaitText' if null.")]
    public TextMeshProUGUI statusHeaderText;

    private readonly List<GameObject> _statusRows = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        if (playerStatusContainer == null)
        {
            Debug.LogWarning("[LobbyPlayerStatusPanel] playerStatusContainer is not assigned in the Inspector. Please drag StatusPanels/List into playerStatusContainer.");
        }
        if (playerStatusRowPrefab == null)
        {
            Debug.LogWarning("[LobbyPlayerStatusPanel] playerStatusRowPrefab is not assigned in the Inspector. Please drag HeatPlayerStatusRow.prefab into playerStatusRowPrefab.");
        }

        if (statusHeaderText == null)
        {
            Transform waitT = transform.Find("WaitText");
            if (waitT != null)
                statusHeaderText = waitT.GetComponent<TextMeshProUGUI>();
            else
                statusHeaderText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (statusHeaderText != null)
        {
            statusHeaderText.richText = true;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeFromTracker();
    }

    private Coroutine _statusEllipsesCoroutine;
    private int _statusDotCount = 1;
    private bool _isAllReady = false;

    private System.Collections.IEnumerator StatusEllipsesRoutine()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (!_isAllReady && statusHeaderText != null)
            {
                _statusDotCount = (_statusDotCount % 3) + 1;
                string dots = new string('.', _statusDotCount);
                statusHeaderText.text = $"WAITING FOR SQUAD{dots}";
            }
        }
    }

    private void OnEnable()
    {
        SubscribeToTracker();
        if (_statusEllipsesCoroutine == null)
        {
            _statusEllipsesCoroutine = StartCoroutine(StatusEllipsesRoutine());
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromTracker();
        if (_statusEllipsesCoroutine != null)
        {
            StopCoroutine(_statusEllipsesCoroutine);
            _statusEllipsesCoroutine = null;
        }
    }

    private void SubscribeToTracker()
    {
        if (PlayerReadyTracker.Instance != null)
        {
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated -= HandlePlayerLobbyStatesUpdated;
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated += HandlePlayerLobbyStatesUpdated;

            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
            PlayerReadyTracker.Instance.OnReadyStatesUpdated += HandleReadyStatesUpdated;

            PlayerReadyTracker.Instance.OnAllPlayersReady -= HandleAllPlayersReady;
            PlayerReadyTracker.Instance.OnAllPlayersReady += HandleAllPlayersReady;
        }
    }

    private void UnsubscribeFromTracker()
    {
        if (PlayerReadyTracker.Instance != null)
        {
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated -= HandlePlayerLobbyStatesUpdated;
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
            PlayerReadyTracker.Instance.OnAllPlayersReady -= HandleAllPlayersReady;
        }
    }

    private void HandleAllPlayersReady()
    {
        _isAllReady = true;
        if (statusHeaderText != null)
        {
            statusHeaderText.text = "<color=#2ECC71>ALL OPERATIVES READY</color>";
        }
    }

    public void UpdateStatusHeader(int readyCount, int totalCount)
    {
        if (statusHeaderText == null) return;

        if (totalCount > 0 && readyCount >= totalCount)
        {
            _isAllReady = true;
            statusHeaderText.text = "<color=#2ECC71>ALL OPERATIVES READY</color>";
        }
        else
        {
            _isAllReady = false;
            string dots = new string('.', _statusDotCount);
            statusHeaderText.text = $"WAITING FOR SQUAD{dots}";
        }
    }

    public void Show()
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        SubscribeToTracker();
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    private void HandlePlayerLobbyStatesUpdated(Dictionary<ulong, PlayerLobbyInfo> snapshot)
    {
        RefreshLobbyStatusRows(snapshot);
    }

    private void HandleReadyStatesUpdated(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        RefreshLegacyStatusRows(snapshot);
    }

    public void RefreshLobbyStatusRows(Dictionary<ulong, PlayerLobbyInfo> snapshot)
    {
        if (playerStatusContainer == null || playerStatusRowPrefab == null) return;

        foreach (var row in _statusRows)
        {
            if (row != null) Destroy(row);
        }
        _statusRows.Clear();

        int readyCount = 0;
        int totalCount = snapshot != null ? snapshot.Count : 0;

        if (snapshot != null)
        {
            foreach (var kvp in snapshot)
            {
                PlayerLobbyInfo info = kvp.Value;
                if (info.isReady) readyCount++;

                GameObject row = Instantiate(playerStatusRowPrefab, playerStatusContainer);
                _statusRows.Add(row);

                string roleName = GetRoleNameForCharacter(info.characterIndex, info.isGirl);
                string subtext = !string.IsNullOrEmpty(subtextFormat) 
                    ? string.Format(subtextFormat, info.playerLevel, roleName) 
                    : roleName;

                HeatPlayerStatusRow heatRow = row.GetComponent<HeatPlayerStatusRow>();
                if (heatRow != null)
                {
                    heatRow.Setup(info.playerName, info.isReady, subtext);
                }
                else
                {
                    var texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                    if (texts.Length >= 1) texts[0].text = info.playerName;
                    if (texts.Length >= 2) texts[1].text = subtext;
                }
            }
        }

        UpdateStatusHeader(readyCount, totalCount);
        RebuildLayout();
    }

    public void RefreshLegacyStatusRows(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        if (playerStatusContainer == null || playerStatusRowPrefab == null) return;

        foreach (var row in _statusRows)
        {
            if (row != null) Destroy(row);
        }
        _statusRows.Clear();

        int readyCount = 0;
        int totalCount = snapshot != null ? snapshot.Count : 0;

        if (snapshot != null)
        {
            foreach (var kvp in snapshot)
            {
                if (kvp.Value.ready) readyCount++;

                GameObject row = Instantiate(playerStatusRowPrefab, playerStatusContainer);
                _statusRows.Add(row);

                string subtext = !string.IsNullOrEmpty(subtextFormat) 
                    ? string.Format(subtextFormat, 1, defaultRoleName) 
                    : defaultRoleName;

                HeatPlayerStatusRow heatRow = row.GetComponent<HeatPlayerStatusRow>();
                if (heatRow != null)
                {
                    heatRow.Setup(kvp.Value.name, kvp.Value.ready, subtext);
                }
                else
                {
                    var texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                    if (texts.Length >= 1) texts[0].text = kvp.Value.name;
                    if (texts.Length >= 2) texts[1].text = subtext;
                }
            }
        }

        UpdateStatusHeader(readyCount, totalCount);
        RebuildLayout();
    }

    public string GetRoleNameForCharacter(int characterIndex, bool isGirl)
    {
        if (isGirl) return spiritRoleName;

        CharacterSelectUI selectUI = characterSelectUI != null
            ? characterSelectUI
            : CharacterSelectUI.Instance;

        if (selectUI != null)
        {
            var def = selectUI.GetDefinitionAtIndex(characterIndex);
            if (def != null)
            {
                return ResolveProfessionRoleName(def.profession, def.characterName);
            }
        }

        switch (characterIndex)
        {
            case 0: return minerRoleName;
            case 1: return hazardRoleName;
            case 2: return explorerRoleName;
            case 3: return priestRoleName;
            case 4: return medicRoleName;
            default: return defaultRoleName;
        }
    }

    private string ResolveProfessionRoleName(InvestigatorProfession profession, string charName)
    {
        if (!string.IsNullOrEmpty(charName))
        {
            string lower = charName.ToLower();
            if (lower.Contains("priest") || lower.Contains("exorcist") || lower.Contains("cursed")) return priestRoleName;
            if (lower.Contains("miner") || lower.Contains("mine") || lower.Contains("breaker") || lower.Contains("worker")) return minerRoleName;
            if (lower.Contains("medic") || lower.Contains("mender") || lower.Contains("doctor")) return medicRoleName;
            if (lower.Contains("hazard") || lower.Contains("protector") || lower.Contains("specialist")) return hazardRoleName;
            if (lower.Contains("explorer") || lower.Contains("adventurer") || lower.Contains("pathfinder")) return explorerRoleName;
        }

        switch (profession)
        {
            case InvestigatorProfession.MineWorker: return minerRoleName;
            case InvestigatorProfession.CursedPriest: return priestRoleName;
            case InvestigatorProfession.FieldMedic: return medicRoleName;
            case InvestigatorProfession.HazardSpecialist: return hazardRoleName;
            case InvestigatorProfession.Explorer: return explorerRoleName;
            default: return defaultRoleName;
        }
    }

    private void RebuildLayout()
    {
        if (playerStatusContainer is RectTransform containerRt)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRt);
            if (containerRt.parent is RectTransform parentRt)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
                if (parentRt.parent is RectTransform grandParentRt)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(grandParentRt);
                }
            }
        }
    }
}
