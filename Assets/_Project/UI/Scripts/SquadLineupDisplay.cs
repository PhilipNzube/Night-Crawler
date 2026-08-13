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
    [Header("Timing (Customizable in Inspector)")]
    [Tooltip("Seconds to wait and display 'SQUAD ASSEMBLED' before starting the countdown.")]
    public float initialHoldBeforeCountdown = 3.0f;

    [Tooltip("Starting number in seconds for the countdown header (e.g. 10).")]
    public int countdownFrom = 10;

    // -------------------------------------------------------------------------
    //  Inspector — Cinematic Animation
    // -------------------------------------------------------------------------
    [Header("Cinematic Animation")]
    [Tooltip("Seconds between each character starting their cinematic sequence. " +
             "Staggers the animations so they don't all fire simultaneously.")]
    public float characterSequenceStagger = 0.6f;

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
    /// Activates the squad environment, builds the 3D lineup, plays cinematic
    /// sequences per character, then after countdown finishes calls onComplete.
    /// Safe to call when prefabs or pivots are not yet wired.
    /// </summary>
    public void ShowSquadLineup(System.Action onComplete = null)
    {
        // Activate the rocks/trees squad world and camera
        if (SquadSceneController.Instance != null)
            SquadSceneController.Instance.EnableSquadEnvironment();

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

        // Initial hold before starting countdown
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, initialHoldBeforeCountdown));

        // Countdown header
        for (int i = countdownFrom; i >= 1; i--)
        {
            if (headerText != null)
                headerText.text = $"ENTERING THE MINE IN {i}...";
            yield return new WaitForSecondsRealtime(1f);
        }

        ClearLineup();
        SetPanelVisible(false);

        // Disable squad world environment
        if (SquadSceneController.Instance != null)
            SquadSceneController.Instance.DisableSquadEnvironment();

        // Notify GirlRevealManager that this investigator is done
        if (GirlRevealManager.Instance != null)
            GirlRevealManager.Instance.ReportInvestigatorReadyServerRpc();

        onComplete?.Invoke();
        _showcaseRoutine = null;
    }

    [Header("Fallback Prefabs (used if no prefab resolved)")]
    [Tooltip("Drag character prefabs here as a guarantee that squad models will spawn even in solo/offline testing.")]
    public List<GameObject> fallbackSquadPrefabs = new List<GameObject>();

    // =========================================================================
    //  Lineup Construction — SOLID (SRP each step is its own method)
    // =========================================================================
    private void BuildLineup()
    {
        ClearLineup();

        // Resolve pivots — if squadPivots is unassigned or empty, generate auto-pivots in front of camera
        List<Transform> activePivots = GetActivePivots();
        if (activePivots == null || activePivots.Count == 0) return;

        // Resolve client IDs — if offline or solo test, create 1-4 dummy slots
        List<ulong> clientIds = ResolveActiveClientIds();

        int slots = Mathf.Min(clientIds.Count, activePivots.Count);
        for (int i = 0; i < slots; i++)
        {
            ulong     clientId = clientIds[i];
            Transform pivot    = activePivots[i];
            if (pivot == null) continue;

            SpawnModelAtPivot(clientId, pivot, i);
            PlaceNameTag(clientId, i);
        }
    }

    private List<Transform> GetActivePivots()
    {
        if (squadPivots != null && squadPivots.Count > 0)
        {
            List<Transform> valid = squadPivots.FindAll(p => p != null);
            if (valid.Count > 0) return valid;
        }

        // Auto-generate 4 fallback pivots in front of lineupCamera if squadPivots is empty
        Transform refCam = lineupCamera != null ? lineupCamera.transform : transform;
        List<Transform> generated = new List<Transform>();

        float[] xOffsets = new float[] { -1.8f, -0.6f, 0.6f, 1.8f };
        GameObject pivotParent = new GameObject("AutoGeneratedSquadPivots");
        pivotParent.transform.SetParent(transform);
        _modelInstances.Add(pivotParent); // track for cleanup

        for (int i = 0; i < xOffsets.Length; i++)
        {
            GameObject pObj = new GameObject($"AutoPivot_{i}");
            pObj.transform.SetParent(pivotParent.transform);
            pObj.transform.position = refCam.position + refCam.forward * 4.5f + refCam.right * xOffsets[i] - refCam.up * 0.8f;
            pObj.transform.rotation = Quaternion.LookRotation(-refCam.forward, Vector3.up);
            generated.Add(pObj.transform);
        }

        return generated;
    }

    private List<ulong> ResolveActiveClientIds()
    {
        ulong girlClientId = ulong.MaxValue;
        if (CharacterSelectManager.Instance != null)
            girlClientId = CharacterSelectManager.Instance.vengefulSpiritClientId.Value;

        List<ulong> clientIds = new List<ulong>();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.ConnectedClientsIds.Count > 0)
        {
            clientIds.AddRange(NetworkManager.Singleton.ConnectedClientsIds);
            clientIds.RemoveAll(id => id == girlClientId);
        }

        // Offline / Solo test fallback: if no clients connected, spawn 1 to 4 test slots
        if (clientIds.Count == 0)
        {
            clientIds.Add(0); // Local player slot
        }

        return clientIds;
    }

    /// <summary>
    /// Instantiates the correct character model at the given pivot, then
    /// attaches a CharacterAnimationController and queues its cinematic sequence.
    /// </summary>
    private void SpawnModelAtPivot(ulong clientId, Transform pivot, int slotIndex)
    {
        GameObject prefab = ResolveCharacterPrefab(clientId, slotIndex);
        if (prefab == null)
        {
            Debug.LogWarning($"[SquadLineupDisplay] Could not resolve character prefab for slot {slotIndex}. " +
                             "Assign characterPrefabs on CharacterCarousel or fallbackSquadPrefabs on SquadLineupDisplay.");
            return;
        }

        GameObject instance = Instantiate(prefab, pivot.position, pivot.rotation, pivot);
        _modelInstances.Add(instance);

        // Disable all MonoBehaviours except CharacterAnimationController
        foreach (MonoBehaviour mb in instance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is CharacterAnimationController) continue;
            mb.enabled = false;
        }

        // Wire up animation controller and trigger the staggered cinematic sequence
        CharacterAnimationController animCtrl = instance.GetComponent<CharacterAnimationController>();
        if (animCtrl == null)
            animCtrl = instance.AddComponent<CharacterAnimationController>();

        animCtrl.characterType = MapProfessionToCharType(clientId);
        float delay = slotIndex * characterSequenceStagger;
        StartCoroutine(PlaySequenceAfterDelay(animCtrl, delay));
    }

    /// <summary>
    /// Multi-stage resolution for character prefabs to guarantee models always spawn in squad room.
    /// Uses PersistentCharacterSelection for local player.
    /// </summary>
    private GameObject ResolveCharacterPrefab(ulong clientId, int slotIndex)
    {
        int targetCharIndex = PersistentCharacterSelection.GetSelectedCharacterIndex();

        // 1. Check CharacterSelectManager choice
        if (CharacterSelectManager.Instance != null)
        {
            int charIdx = (NetworkManager.Singleton != null && clientId != NetworkManager.Singleton.LocalClientId)
                ? CharacterSelectManager.Instance.GetSelectedCharacterIndex(clientId)
                : targetCharIndex;
            GameObject selected = CharacterSelectManager.Instance.GetInvestigatorPrefab(charIdx);
            if (selected != null) return selected;
        }

        // 2. Check CharacterSelectUI inspector data list (SO or inline, including inactive)
        CharacterSelectUI selectUI = FindFirstObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
        if (selectUI != null)
        {
            int idx = (slotIndex == 0)
                ? Mathf.Clamp(targetCharIndex, 0, selectUI.GetTotalCharacterCount() - 1)
                : Mathf.Clamp(slotIndex, 0, selectUI.GetTotalCharacterCount() - 1);

            if (selectUI.characterDefinitions != null && idx < selectUI.characterDefinitions.Count && selectUI.characterDefinitions[idx] != null)
            {
                if (selectUI.characterDefinitions[idx].characterPrefab != null)
                    return selectUI.characterDefinitions[idx].characterPrefab;
            }

            if (selectUI.characterDataList != null && idx < selectUI.characterDataList.Count && selectUI.characterDataList[idx] != null)
            {
                if (selectUI.characterDataList[idx].characterPrefab != null)
                    return selectUI.characterDataList[idx].characterPrefab;
            }
        }

        // 3. Check GameManager explorerPrefabs by index
        if (GameManager.Instance != null && GameManager.Instance.explorerPrefabs != null && GameManager.Instance.explorerPrefabs.Count > 0)
        {
            int idx = Mathf.Clamp(targetCharIndex, 0, GameManager.Instance.explorerPrefabs.Count - 1);
            if (GameManager.Instance.explorerPrefabs[idx] != null)
                return GameManager.Instance.explorerPrefabs[idx];
        }


        // 4. Check fallbackSquadPrefabs on this component
        if (fallbackSquadPrefabs != null && fallbackSquadPrefabs.Count > 0)
        {
            int idx = Mathf.Clamp(slotIndex, 0, fallbackSquadPrefabs.Count - 1);
            if (fallbackSquadPrefabs[idx] != null)
                return fallbackSquadPrefabs[idx];
        }

        // 5. Check GameManager fallback
        if (GameManager.Instance != null)
        {
            GameObject explorer = GameManager.Instance.GetRandomExplorerPrefab();
            if (explorer != null) return explorer;
        }

        return null;
    }

    // =========================================================================
    //  Animation Helpers
    // =========================================================================

    private System.Collections.IEnumerator PlaySequenceAfterDelay(
        CharacterAnimationController ctrl, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        if (ctrl != null) ctrl.PlayCinematicSequence(startGestureLoopAfter: true);
    }

    /// <summary>
    /// Maps an investigator's selected profession to the animation character type
    /// so the correct built-in cinematic sequence plays.
    /// </summary>
    private CharacterAnimationController.CharacterType MapProfessionToCharType(ulong clientId)
    {
        if (CharacterSelectManager.Instance == null)
            return CharacterAnimationController.CharacterType.Adventurer;

        int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(clientId);
        var chars = CharacterSelectManager.Instance.availableCharacters;
        if (chars == null || idx < 0 || idx >= chars.Count)
            return CharacterAnimationController.CharacterType.Adventurer;

        switch (chars[idx].profession)
        {
            case InvestigatorProfession.CursedPriest:    return CharacterAnimationController.CharacterType.Priest;
            case InvestigatorProfession.MineWorker:       return CharacterAnimationController.CharacterType.Miner;
            case InvestigatorProfession.FieldMedic:       return CharacterAnimationController.CharacterType.Medic;
            case InvestigatorProfession.HazardSpecialist: return CharacterAnimationController.CharacterType.Protector;
            case InvestigatorProfession.Explorer:         return CharacterAnimationController.CharacterType.Adventurer;
            default:                                      return CharacterAnimationController.CharacterType.Adventurer;
        }
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
            NetworkManager.Singleton.SpawnManager != null &&
            NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId) is NetworkObject netObj &&
            netObj != null)
        {
            NetworkPlayerName nameComp = netObj.GetComponent<NetworkPlayerName>();
            if (nameComp != null && !string.IsNullOrEmpty(nameComp.playerName.Value.ToString()))
                return nameComp.playerName.Value.ToString();
        }

        // Local player fallback
        string savedName = PlayerNameManager.GetPlayerName();
        if (!string.IsNullOrEmpty(savedName)) return savedName;

        return $"Investigator_{clientId % 1000}";
    }

    /// <summary>Returns the profession name for a client's selected character, or "Investigator".</summary>
    private string ResolveProfessionName(ulong clientId)
    {
        if (CharacterSelectManager.Instance != null)
        {
            bool isVengefulSpirit = CharacterSelectManager.Instance.vengefulSpiritClientId.Value == clientId;
            if (isVengefulSpirit) return "Investigator"; // Disguised — role hidden

            int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(clientId);
            var chars = CharacterSelectManager.Instance.availableCharacters;
            if (chars != null && idx >= 0 && idx < chars.Count)
                return chars[idx].characterName;
        }

        // Check CharacterSelectUI fallback
        CharacterSelectUI selectUI = FindFirstObjectByType<CharacterSelectUI>();
        if (selectUI != null)
        {
            int savedIdx = PersistentCharacterSelection.GetSelectedCharacterIndex();

            if (selectUI.characterDefinitions != null && savedIdx >= 0 && savedIdx < selectUI.characterDefinitions.Count)
            {
                if (selectUI.characterDefinitions[savedIdx] != null)
                    return selectUI.characterDefinitions[savedIdx].characterName;
            }

            if (selectUI.characterDataList != null && savedIdx >= 0 && savedIdx < selectUI.characterDataList.Count)
            {
                if (selectUI.characterDataList[savedIdx] != null)
                    return selectUI.characterDataList[savedIdx].characterName;
            }
        }

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
