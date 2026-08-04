using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Manages the 3D cinematic squad lineup showcase
///              (Call of Duty / Warzone style) after all players finish character selection.
///
/// OCP: Squad size is driven by the 'squadPivots' list — add more pivots for 5+ players
///      without touching this code.
///
/// DIP: Reads player names via <see cref="NetworkPlayerName"/> on spawned NetworkObjects,
///      and character data via <see cref="CharacterSelectManager.Instance"/> — no coupling
///      to concrete spawning or transport logic.
///
/// Design ("The Mine"):
///   • All investigators stand side-by-side in a lit, cinematic 3D row.
///   • The Vengeful Spirit stands among them disguised — nobody knows who she is yet.
///     This preserves the social-horror paranoia that is core to "The Mine".
///   • A countdown header reads "ENTERING THE MINE IN X..." before scene load.
/// </summary>
public class SquadLineupDisplay : MonoBehaviour
{
    public static SquadLineupDisplay Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Inspector — 3D Scene
    // -------------------------------------------------------------------------
    [Header("3D Scene Layout")]
    [Tooltip("Cinematic camera aimed at the squad lineup. Enabled only during showcase.")]
    public Camera lineupCamera;

    [Tooltip("Ordered list of Transform pivots where player models stand, left to right.")]
    public List<Transform> squadPivots = new List<Transform>();

    // -------------------------------------------------------------------------
    //  Inspector — Canvas Overlay
    // -------------------------------------------------------------------------
    [Header("Canvas Overlay")]
    [Tooltip("Root UI panel that hosts the name cards and header text.")]
    public GameObject lineupUIPanel;

    [Tooltip("Big header TMP. Displays 'SQUAD ASSEMBLED' then countdown.")]
    public TextMeshProUGUI headerText;

    [Tooltip("Prefab with a SquadTagUI component containing Name + Profession TMPs.")]
    public GameObject squadTagPrefab;

    // -------------------------------------------------------------------------
    //  Inspector — Timing
    // -------------------------------------------------------------------------
    [Header("Timing")]
    [Tooltip("Total seconds to display the squad before the scene loads.")]
    public float showcaseDuration = 5f;

    [Tooltip("Seconds before scene load to begin the countdown header.")]
    public float countdownFrom = 3f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private readonly List<GameObject> _modelInstances = new List<GameObject>();
    private readonly List<GameObject> _tagInstances   = new List<GameObject>();
    private Coroutine                 _showcaseRoutine;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        SetPanelVisible(false);
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Activates the 3D squad lineup, then after <see cref="showcaseDuration"/> calls
    /// <paramref name="onComplete"/> (e.g. to trigger scene load).
    /// Safe to call when prefabs or pivots are not yet wired.
    /// </summary>
    public void ShowSquadLineup(System.Action onComplete = null)
    {
        if (_showcaseRoutine != null) StopCoroutine(_showcaseRoutine);
        _showcaseRoutine = StartCoroutine(RunShowcase(onComplete));
    }

    /// <summary>Immediately hides and clears the lineup.</summary>
    public void HideSquadLineup()
    {
        if (_showcaseRoutine != null) { StopCoroutine(_showcaseRoutine); _showcaseRoutine = null; }
        ClearLineup();
        SetPanelVisible(false);
    }

    // =========================================================================
    //  Showcase Coroutine
    // =========================================================================
    private IEnumerator RunShowcase(System.Action onComplete)
    {
        SetPanelVisible(true);
        BuildLineup();

        if (headerText != null)
            headerText.text = "SQUAD ASSEMBLED";

        float holdTime = Mathf.Max(0f, showcaseDuration - countdownFrom);
        yield return new WaitForSecondsRealtime(holdTime);

        // Countdown
        for (int i = Mathf.RoundToInt(countdownFrom); i >= 1; i--)
        {
            if (headerText != null)
                headerText.text = $"ENTERING THE MINE IN {i}...";
            yield return new WaitForSecondsRealtime(1f);
        }

        ClearLineup();
        SetPanelVisible(false);

        onComplete?.Invoke();
        _showcaseRoutine = null;
    }

    // =========================================================================
    //  Lineup Construction — SOLID (SRP each step is its own method)
    // =========================================================================
    private void BuildLineup()
    {
        ClearLineup();

        if (NetworkManager.Singleton == null) return;

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        int slots = Mathf.Min(clientIds.Count, squadPivots != null ? squadPivots.Count : 0);

        for (int i = 0; i < slots; i++)
        {
            ulong     clientId = clientIds[i];
            Transform pivot    = squadPivots[i];
            if (pivot == null) continue;

            SpawnModelAtPivot(clientId, pivot);
            PlaceNameTag(clientId, i);
        }
    }

    /// <summary>Instantiates the correct character model at the given pivot.</summary>
    private void SpawnModelAtPivot(ulong clientId, Transform pivot)
    {
        GameObject prefab = ResolveCharacterPrefab(clientId);
        if (prefab == null) return;

        GameObject instance = Instantiate(prefab, pivot.position, pivot.rotation, pivot);
        _modelInstances.Add(instance);

        // Disable all MonoBehaviours except Animator — model stays in idle pose
        foreach (MonoBehaviour mb in instance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (!(mb is Animator))
                mb.enabled = false;
        }
    }

    /// <summary>
    /// Resolves which prefab to show for a given client.
    /// Priority: CharacterSelectManager choice → girl prefab for Vengeful Spirit → fallback explorer.
    /// </summary>
    private GameObject ResolveCharacterPrefab(ulong clientId)
    {
        // Is this client the Vengeful Spirit?
        bool isVengefulSpirit = CharacterSelectManager.Instance != null &&
                                CharacterSelectManager.Instance.vengefulSpiritClientId.Value == clientId;

        if (isVengefulSpirit)
        {
            // Vengeful Spirit shown as their girl model (or first explorer as fallback)
            if (GameManager.Instance != null && GameManager.Instance.girlPrefab != null)
                return GameManager.Instance.girlPrefab;
        }
        else if (CharacterSelectManager.Instance != null)
        {
            int charIdx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(clientId);
            GameObject selected = CharacterSelectManager.Instance.GetInvestigatorPrefab(charIdx);
            if (selected != null) return selected;
        }

        // Fallback: any explorer prefab
        return GameManager.Instance != null ? GameManager.Instance.GetRandomExplorerPrefab() : null;
    }

    /// <summary>Instantiates a name card UI tag for the given slot.</summary>
    private void PlaceNameTag(ulong clientId, int slotIndex)
    {
        if (squadTagPrefab == null || lineupUIPanel == null) return;

        GameObject tagObj = Instantiate(squadTagPrefab, lineupUIPanel.transform);
        _tagInstances.Add(tagObj);

        // Resolve name via spawned NetworkObject if available; fall back to PlayerPrefs / generic
        string playerName    = ResolvePlayerName(clientId);
        string professionName = ResolveProfessionName(clientId);

        SquadTagUI tagUI = tagObj.GetComponent<SquadTagUI>();
        if (tagUI != null)
        {
            tagUI.SetTag(playerName, professionName);
        }
        else
        {
            // Graceful fallback if SquadTagUI component isn't on the prefab
            TextMeshProUGUI[] texts = tagObj.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0) texts[0].text = playerName;
            if (texts.Length > 1) texts[1].text = professionName;
        }
    }

    /// <summary>
    /// Finds the spawned NetworkObject owned by clientId and reads the synced player name.
    /// Falls back to the local PlayerPrefs name for the local player, or a generic label.
    /// </summary>
    private string ResolvePlayerName(ulong clientId)
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId) is NetworkObject netObj &&
            netObj != null)
        {
            NetworkPlayerName nameComp = netObj.GetComponent<NetworkPlayerName>();
            if (nameComp != null)
                return nameComp.playerName.Value.ToString();
        }

        // Local player fallback
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
            return PlayerNameManager.GetPlayerName();

        return $"Investigator_{clientId % 1000}";
    }

    /// <summary>Returns the profession name for a client's selected character, or "Investigator".</summary>
    private string ResolveProfessionName(ulong clientId)
    {
        if (CharacterSelectManager.Instance == null) return "Investigator";

        bool isVengefulSpirit = CharacterSelectManager.Instance.vengefulSpiritClientId.Value == clientId;
        if (isVengefulSpirit) return "Investigator"; // Disguised — role hidden

        int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(clientId);
        var chars = CharacterSelectManager.Instance.availableCharacters;
        if (chars != null && idx >= 0 && idx < chars.Count)
            return chars[idx].characterName;

        return "Investigator";
    }

    // =========================================================================
    //  Helpers
    // =========================================================================
    private void SetPanelVisible(bool visible)
    {
        if (lineupCamera != null) lineupCamera.enabled = visible;
        if (lineupUIPanel != null) lineupUIPanel.SetActive(visible);
    }

    private void ClearLineup()
    {
        foreach (GameObject inst in _modelInstances)
            if (inst != null) Destroy(inst);
        _modelInstances.Clear();

        foreach (GameObject tag in _tagInstances)
            if (tag != null) Destroy(tag);
        _tagInstances.Clear();
    }
}
