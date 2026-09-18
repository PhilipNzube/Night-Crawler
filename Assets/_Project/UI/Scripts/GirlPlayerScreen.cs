using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using NightCrawler.Economy;
using Michsky.UI.Heat;
using NightCrawler.UI;

/// <summary>
/// SOLID — SRP: The exclusive cinematic screen shown only to the player chosen
///              as the Vengeful Spirit / "Girl".
/// </summary>
public class GirlPlayerScreen : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — Environment
    // -------------------------------------------------------------------------
    [Header("Environment")]
    [Tooltip("Root panel of this screen. Enabled only for the girl player after reveal.")]
    public GameObject girlScreenPanel;

    [Tooltip("Camera that looks at the girl's character model on this screen.")]
    public Camera girlScreenCamera;

    // -------------------------------------------------------------------------
    //  Inspector — Character Model
    // -------------------------------------------------------------------------
    [Header("Character Model")]
    [Tooltip("Transform pivot in the scene where the girl's model is spawned.")]
    public Transform modelPivot;

    [Tooltip("Direct prefab override for the girl model. Used if GameManager.girlPrefab is null.")]
    public GameObject girlPrefabOverride;

    // -------------------------------------------------------------------------
    //  Inspector — UI Text
    // -------------------------------------------------------------------------
    [Header("UI — Info Section")]
    [Tooltip("The parent GameObject/panel containing the girl's info, abilities, and lore. Hidden when READY is pressed.")]
    public GameObject girlInfoSection;

    [Tooltip("Large title label. Displays the role name.")]
    public TextMeshProUGUI roleTitleText;

    [Tooltip("Flavour / lore text panel describing the girl's role.")]
    public TextMeshProUGUI flavourText;

    [Tooltip("Status line that changes after READY is pressed.")]
    public TextMeshProUGUI waitingText;

    // -------------------------------------------------------------------------
    //  Inspector — Staking & Upgrades (Economy)
    // -------------------------------------------------------------------------
    [Header("Match Stake (Lobby Staking)")]
    [Tooltip("Input field where the Girl player enters her match stake. READY button remains disabled until valid stake is entered.")]
    public TMP_InputField stakeInputField;
    [Tooltip("Michsky Heat/Dark UI Input Field for staking.")]
    public InputFieldManager heatStakeInputField;

    [Tooltip("Text displaying stake validation errors (e.g. empty or less than minimum).")]
    public TextMeshProUGUI stakeErrorText;

    [Tooltip("Text displaying current persistent credit balance.")]
    public TextMeshProUGUI creditBalanceText;

    [Header("The Girl Persistent Upgrades")]
    [Tooltip("Button to open the Vengeful Spirit Upgrades panel.")]
    public Button openUpgradesButton;
    [Tooltip("Michsky Heat/Dark UI Button to open the Vengeful Spirit Upgrades panel.")]
    public ButtonManager heatOpenUpgradesButton;

    [Tooltip("Reference to the Vengeful Spirit Upgrades panel GameObject.")]
    public GameObject upgradePanel;

    // -------------------------------------------------------------------------
    //  Inspector — READY Button
    // -------------------------------------------------------------------------
    [Header("UI — Ready Button")]
    [Tooltip("Button the girl presses when she is ready for the game to start.")]
    public Button readyButton;
    [Tooltip("Michsky Heat/Dark UI Button for Ready.")]
    public ButtonManager heatReadyButton;

    [Tooltip("Seconds after Show() before the READY button appears.")]
    public float readyButtonDelay = 3.5f;

    [Header("Player Status Panel (Live Investigator Status)")]
    [Tooltip("Optional: root panel to show all players' ready status on the girl screen.")]
    public GameObject playerStatusPanel;
    [Tooltip("Vertical container inside playerStatusPanel for per-player rows.")]
    public Transform playerStatusContainer;
    [Tooltip("Prefab for a single row: must have 2 TMP_Text children — [0]=name, [1]=status.")]
    public GameObject playerStatusRowPrefab;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private GameObject                   _modelInstance;
    private CharacterAnimationController _animController;
    private Coroutine                    _readyDelayCoroutine;
    private bool                         _readySent = false;
    private readonly List<GameObject>    _statusRows = new List<GameObject>();

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (girlScreenPanel == null)
            girlScreenPanel = gameObject;

        MichskyUIBridge.BindButton(readyButton, heatReadyButton, OnReadyPressed);
    }

    void OnEnable()
    {
        Show();
    }

    void OnDisable()
    {
        Hide();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    public void Show()
    {
        _readySent = false;
        PersistentCharacterSelection.SetIsVengefulSpirit(true);

        // Ensure CharacterSelectUI and white room are disabled so nothing overlaps the girl screen
        CharacterSelectUI selectUI = FindFirstObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
        if (selectUI != null && selectUI.gameObject.activeSelf)
            selectUI.gameObject.SetActive(false);

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();

        SetScreenVisible(true);
        if (girlInfoSection != null) girlInfoSection.SetActive(true);
        if (roleTitleText != null) roleTitleText.gameObject.SetActive(true);
        if (flavourText != null) flavourText.gameObject.SetActive(true);
        if (playerStatusPanel != null) playerStatusPanel.SetActive(false);

        PopulateTexts();
        SpawnGirlModel();
        SetupStakingAndUpgrades();

        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.GirlScreen);

        if (_readyDelayCoroutine != null) StopCoroutine(_readyDelayCoroutine);
        _readyDelayCoroutine = StartCoroutine(EnableReadyButtonAfterDelay());

        // Subscribe to live ready-state updates so investigators' status is visible here too
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.OnReadyStatesUpdated += HandleReadyStatesUpdated;
    }

    private void SetupStakingAndUpgrades()
    {
        if (stakeInputField != null) stakeInputField.gameObject.SetActive(true);
        if (heatStakeInputField != null) heatStakeInputField.gameObject.SetActive(true);
        MichskyUIBridge.BindInputField(stakeInputField, heatStakeInputField, _ => ValidateStake());
        MichskyUIBridge.SetInputText(stakeInputField, heatStakeInputField, "");

        if (creditBalanceText != null)
        {
            creditBalanceText.gameObject.SetActive(true);
            int credits = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.CurrentCredits : CurrencyConfig.DefaultStartingBalance;
            creditBalanceText.text = $"Credits: {credits} {CurrencyConfig.CurrencySymbol}";
        }

        if (openUpgradesButton != null) openUpgradesButton.gameObject.SetActive(true);
        if (heatOpenUpgradesButton != null) heatOpenUpgradesButton.gameObject.SetActive(true);

        MichskyUIBridge.BindButton(openUpgradesButton, heatOpenUpgradesButton, () =>
        {
            if (upgradePanel != null)
            {
                upgradePanel.SetActive(!upgradePanel.activeSelf);
                if (creditBalanceText != null && CloudCharacterSaveManager.Instance != null)
                {
                    creditBalanceText.text = $"Credits: {CloudCharacterSaveManager.Instance.CurrentCredits} {CurrencyConfig.CurrencySymbol}";
                }
            }
        });

        ValidateStake();
    }

    public bool ValidateStake()
    {
        if (readyButton == null && heatReadyButton == null) return true;

        if (stakeInputField == null && heatStakeInputField == null)
        {
            // If inspector reference not assigned, keep readyButton interactable once delay passes
            return true;
        }

        string raw = MichskyUIBridge.GetInputText(stakeInputField, heatStakeInputField).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            MichskyUIBridge.SetButtonInteractable(readyButton, heatReadyButton, false);
            if (stakeErrorText != null)
            {
                stakeErrorText.gameObject.SetActive(true);
                stakeErrorText.text = $"Stake cannot be empty! (Min: {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol})";
            }
            return false;
        }

        if (!int.TryParse(raw, out int stake))
        {
            MichskyUIBridge.SetButtonInteractable(readyButton, heatReadyButton, false);
            if (stakeErrorText != null)
            {
                stakeErrorText.gameObject.SetActive(true);
                stakeErrorText.text = "Please enter a valid numeric stake!";
            }
            return false;
        }

        if (stake < CurrencyConfig.MinimumStake)
        {
            MichskyUIBridge.SetButtonInteractable(readyButton, heatReadyButton, false);
            if (stakeErrorText != null)
            {
                stakeErrorText.gameObject.SetActive(true);
                stakeErrorText.text = $"Minimum stake required is {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol}!";
            }
            return false;
        }

        int currentCredits = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.CurrentCredits : CurrencyConfig.DefaultStartingBalance;
        if (stake > currentCredits)
        {
            MichskyUIBridge.SetButtonInteractable(readyButton, heatReadyButton, false);
            if (stakeErrorText != null)
            {
                stakeErrorText.gameObject.SetActive(true);
                stakeErrorText.text = $"Insufficient credits! Balance: {currentCredits} {CurrencyConfig.CurrencySymbol}";
            }
            return false;
        }

        // All checks passed!
        MichskyUIBridge.SetButtonInteractable(readyButton, heatReadyButton, true);
        if (stakeErrorText != null)
        {
            stakeErrorText.text = "";
            stakeErrorText.gameObject.SetActive(false);
        }
        return true;
    }

    public void Hide()
    {
        SetScreenVisible(false);
        DestroyModel();

        if (_readyDelayCoroutine != null)
        {
            StopCoroutine(_readyDelayCoroutine);
            _readyDelayCoroutine = null;
        }

        // Unsubscribe
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
    }

    // =========================================================================
    //  Private
    // =========================================================================

    private void PopulateTexts()
    {
        if (roleTitleText != null)
            roleTitleText.text = "VENGEFUL SPIRIT";

        if (flavourText != null)
            flavourText.text =
                "The mine took everything from you.\n\n" +
                "Seep into the shadows.\n" +
                "Manipulate the lights. Whisper lies.\n" +
                "Turn the investigators against each other.\n\n" +
                "Make them suffer.";

        if (waitingText != null)
            waitingText.text = "The investigators are assembling their squad...";
    }

    private void SpawnGirlModel()
    {
        DestroyModel();

        // 1. Resolve Pivot
        Transform pivot = modelPivot;
        if (pivot == null)
        {
            Transform childPivot = transform.Find("ModelPivot");
            pivot = childPivot != null ? childPivot : transform;
            Debug.Log($"[GirlPlayerScreen] modelPivot unassigned — using fallback: {pivot.name}");
        }

        // 2. Resolve Prefab
        GameObject prefab = girlPrefabOverride;
        if (prefab == null && GameManager.Instance != null)
            prefab = GameManager.Instance.girlPrefab;

        if (prefab == null)
        {
            Debug.LogWarning("[GirlPlayerScreen] CRITICAL: No Girl Prefab assigned! " +
                             "Assign 'girlPrefabOverride' on GirlPlayerScreen or 'girlPrefab' on GameManager.");
            return;
        }

        Debug.Log($"[GirlPlayerScreen] Spawning Girl Model: {prefab.name} at pivot {pivot.name}");
        _modelInstance = Instantiate(prefab, pivot.position, pivot.rotation, pivot);

        // Disable gameplay components — only keep CharacterAnimationController
        foreach (MonoBehaviour mb in _modelInstance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is CharacterAnimationController) continue;
            mb.enabled = false;
        }

        // Ensure CharacterAnimationController is present and set to Girl
        _animController = _modelInstance.GetComponent<CharacterAnimationController>();
        if (_animController == null)
            _animController = _modelInstance.AddComponent<CharacterAnimationController>();

        _animController.characterType = CharacterAnimationController.CharacterType.Girl;
        _animController.PlayDanceLoop();
    }

    private IEnumerator EnableReadyButtonAfterDelay()
    {
        if (readyButton != null) readyButton.gameObject.SetActive(false);
        if (heatReadyButton != null) heatReadyButton.gameObject.SetActive(false);
        yield return new WaitForSecondsRealtime(readyButtonDelay);
        if (readyButton != null) readyButton.gameObject.SetActive(true);
        if (heatReadyButton != null) heatReadyButton.gameObject.SetActive(true);
        ValidateStake();
        _readyDelayCoroutine = null;
    }

    private void OnReadyPressed()
    {
        if (_readySent) return;
        if (!ValidateStake()) return;

        // Save persistent stake and spend credits locally
        int stake = CurrencyConfig.MinimumStake;
        string rawStake = MichskyUIBridge.GetInputText(stakeInputField, heatStakeInputField).Trim();
        if (!string.IsNullOrEmpty(rawStake) && int.TryParse(rawStake, out int parsed))
        {
            stake = parsed;
        }

        PersistentCharacterSelection.SetSavedMatchStake(stake);
        if (CloudCharacterSaveManager.Instance != null)
        {
            CloudCharacterSaveManager.Instance.SpendCredits(stake);
        }

        _readySent = true;

        // Hide abilities, info section, flavour text, and READY button so they don't overlap the status panel
        if (girlInfoSection != null)
        {
            girlInfoSection.SetActive(false);
        }
        else if (flavourText != null && flavourText.transform.parent != null && flavourText.transform.parent.gameObject != girlScreenPanel && flavourText.transform.parent.gameObject != gameObject)
        {
            flavourText.transform.parent.gameObject.SetActive(false);
        }

        if (readyButton != null) readyButton.gameObject.SetActive(false);
        if (heatReadyButton != null) heatReadyButton.gameObject.SetActive(false);
        if (flavourText != null) flavourText.gameObject.SetActive(false);
        if (roleTitleText != null) roleTitleText.gameObject.SetActive(false);

        // Hide staking & upgrade controls on ready
        if (stakeInputField != null)   stakeInputField.gameObject.SetActive(false);
        if (heatStakeInputField != null) heatStakeInputField.gameObject.SetActive(false);
        if (stakeErrorText != null)     stakeErrorText.gameObject.SetActive(false);
        if (creditBalanceText != null)  creditBalanceText.gameObject.SetActive(false);
        if (openUpgradesButton != null) openUpgradesButton.gameObject.SetActive(false);
        if (heatOpenUpgradesButton != null) heatOpenUpgradesButton.gameObject.SetActive(false);
        if (upgradePanel != null)       upgradePanel.SetActive(false);

        if (waitingText != null)
        {
            waitingText.gameObject.SetActive(true);
            waitingText.text = "READY! WAITING FOR INVESTIGATORS...";
        }

        if (playerStatusPanel != null)
            playerStatusPanel.SetActive(true);

        // Primary path: PlayerReadyTracker (broadcasts status to all clients)
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.ReportGirlReady();

        // Legacy fallback: GirlRevealManager (keeps old scene-load logic in sync)
        if (GirlRevealManager.Instance != null)
            GirlRevealManager.Instance.ReportGirlReady();
    }

    private void HandleReadyStatesUpdated(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        if (playerStatusContainer == null || playerStatusRowPrefab == null) return;

        foreach (var row in _statusRows)
            if (row != null) Destroy(row);
        _statusRows.Clear();

        foreach (var kvp in snapshot)
        {
            GameObject row = Instantiate(playerStatusRowPrefab, playerStatusContainer);
            _statusRows.Add(row);
            var texts = row.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            if (texts.Length >= 1) texts[0].text = kvp.Value.name;
            if (texts.Length >= 2)
            {
                texts[1].text = kvp.Value.ready ? "READY" : "NOT READY";
                texts[1].color = kvp.Value.ready
                    ? new Color(0.18f, 0.80f, 0.44f)  // Bright Emerald Green
                    : new Color(0.91f, 0.30f, 0.24f); // Vibrant Crimson Red
            }
        }

        // Only show status panel after the girl has pressed READY
        if (playerStatusPanel != null)
            playerStatusPanel.SetActive(_readySent && _statusRows.Count > 0);
    }

    private void SetScreenVisible(bool visible)
    {
        if (girlScreenPanel != null && girlScreenPanel != gameObject)
            girlScreenPanel.SetActive(visible);

        if (girlScreenCamera != null)
            girlScreenCamera.enabled = visible;
    }

    private void DestroyModel()
    {
        if (_modelInstance != null)
        {
            Destroy(_modelInstance);
            _modelInstance  = null;
            _animController = null;
        }
    }
}
