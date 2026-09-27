using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using NightCrawler.Economy;
using Michsky.UI.Heat;
using NightCrawler.UI;

/// <summary>
/// SOLID — SRP: The exclusive cinematic screen shown only to the player chosen
///              as the Vengeful Spirit / "Girl".
///
/// Flow:
///   1. Girl model dances on stage.
///   2. After delay, READY button appears.
///   3. Girl taps READY -> Match Stake Modal opens.
///      - Confirm button is INACTIVE until at least 2 credits are entered.
///      - If cancelled without confirming: girl is NOT marked ready.
///      - Once confirmed: stake is recorded, and ready status is reported.
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
    //  Inspector — UI Info Section
    // -------------------------------------------------------------------------
    [Header("UI — Info Section")]
    [Tooltip("The parent GameObject/panel containing the girl's info and abilities. Hidden when READY is pressed.")]
    public GameObject girlInfoSection;

    [Header("Stat Progress Bars (Girl / Vengeful Spirit)")]
    [Tooltip("List of progress bars displaying the persistent upgrade stats of the Vengeful Spirit.")]
    public List<StatProgressBarItem> statProgressBars = new List<StatProgressBarItem>();

    // -------------------------------------------------------------------------
    //  Inspector — READY Button
    // -------------------------------------------------------------------------
    [Header("UI — Ready Button")]
    [Tooltip("Michsky Heat Button for Ready.")]
    public ButtonManager heatReadyButton;

    [Tooltip("Seconds after Show() before the READY button appears.")]
    public float readyButtonDelay = 3.5f;

    // -------------------------------------------------------------------------
    //  Inspector — Match Stake Modal (Opened on Ready)
    // -------------------------------------------------------------------------
    [Header("Match Stake Modal (Opened on Ready)")]
    [Tooltip("Modal Window that pops up when tapping READY to enter the match stake.")]
    public ModalWindowManager matchStakeModal;

    [Tooltip("Michsky Input Field inside the modal where the player types their stake.")]
    public InputFieldManager heatStakeInputField;

    [Tooltip("Confirm button inside the stake modal. Inactive until 2+ credits entered.")]
    public ButtonManager heatStakeConfirmButton;

    [Tooltip("Cancel / Close button inside the stake modal.")]
    public ButtonManager heatStakeCancelButton;

    [Tooltip("Error / Hint label inside the stake modal (e.g. 'Min 2 credits').")]
    public TextMeshProUGUI stakeErrorText;

    [Header("Central Player Status Panel")]
    [Tooltip("Drag the PlayerStatusPanel GameObject (with LobbyPlayerStatusPanel component attached) here.")]
    public LobbyPlayerStatusPanel lobbyPlayerStatusPanel;

    [Tooltip("Fallback root GameObject for the player status panel.")]
    public GameObject playerStatusPanel;

    // =========================================================================
    //  Inspector — Exit Confirmation Modal & Hotkey (Manual Wiring — No Auto-Find)
    // =========================================================================

    [Header("Exit Confirmation Modal & Hotkey (Manual Assignment — No Auto-Find)")]
    [Tooltip("Explicit reference to the Exit Confirmation Modal for Girl Screen (e.g. ExitModal under GirlFlow). No auto-finding.")]
    public ModalWindowManager exitConfirmModal;

    [Tooltip("Explicit reference to the Confirm button inside the exit modal.")]
    public ButtonManager heatExitConfirmButton;

    [Tooltip("Optional explicit reference to the Exit Hotkey indicator/button (e.g. ExitHotKey under GirlFlow).")]
    public HotkeyEvent exitHotkey;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private GameObject                   _modelInstance;
    private CharacterAnimationController _animController;
    private Coroutine                    _readyDelayCoroutine;
    private bool                         _readySent = false;
    private int                          _lastEscapeFrame = -1;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (girlScreenPanel == null)
            girlScreenPanel = gameObject;

        MichskyUIBridge.BindButton(null, heatReadyButton, OnReadyButtonClicked);
        InitExitBindings();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleExitHotkey();
        }
    }

    public void InitExitBindings()
    {
        // Explicit wiring only — no auto-finding!
        if (exitConfirmModal != null)
        {
            exitConfirmModal.onConfirm.RemoveListener(ConfirmExitToHome);
            exitConfirmModal.onConfirm.AddListener(ConfirmExitToHome);
        }

        if (heatExitConfirmButton != null)
        {
            MichskyUIBridge.BindButton(null, heatExitConfirmButton, ConfirmExitToHome);
        }

        if (exitHotkey != null)
        {
            exitHotkey.onHotkeyPress.RemoveListener(HandleExitHotkey);
            exitHotkey.onHotkeyPress.AddListener(HandleExitHotkey);
        }
    }

    /// <summary>
    /// Handles hotkey press (Esc or UI HotKey button).
    /// Prevents double-firing in the same frame if both HotkeyEvent and Keyboard fire.
    /// </summary>
    public void HandleExitHotkey()
    {
        if (Time.frameCount == _lastEscapeFrame) return;
        _lastEscapeFrame = Time.frameCount;

        // 1. If Exit Confirm Modal is already open, cancel/close it
        if (exitConfirmModal != null && exitConfirmModal.isOn)
        {
            exitConfirmModal.CloseWindow();
            return;
        }

        // 2. If Match Stake Modal is open, close/cancel it
        if (matchStakeModal != null && matchStakeModal.isOn)
        {
            OnStakeModalCancelled();
            return;
        }

        // 3. Otherwise, prompt the exit modal if assigned
        RequestExitToHome();
    }

    public void RequestExitToHome()
    {
        if (exitConfirmModal != null)
        {
            if (!exitConfirmModal.gameObject.activeSelf)
                exitConfirmModal.gameObject.SetActive(true);
            exitConfirmModal.OpenWindow();
        }
    }

    public void ConfirmExitToHome()
    {
        if (exitConfirmModal != null)
        {
            exitConfirmModal.CloseWindow();
        }

        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.ConfirmDisconnect();
        }
        else
        {
            DestroyModel();
            SetScreenVisible(false);
        }
    }

    void OnEnable()
    {
        InitExitBindings();
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

        // Ensure CharacterSelectUI and white room are disabled
        CharacterSelectUI selectUI = FindFirstObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
        if (selectUI != null && selectUI.gameObject.activeSelf)
            selectUI.gameObject.SetActive(false);

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();

        SetScreenVisible(true);
        if (girlInfoSection != null) girlInfoSection.SetActive(true);
        if (playerStatusPanel != null) playerStatusPanel.SetActive(false);

        UpdateGirlStatProgressBars();

        SpawnGirlModel();

        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.GirlScreen);

        if (_readyDelayCoroutine != null) StopCoroutine(_readyDelayCoroutine);
        _readyDelayCoroutine = StartCoroutine(EnableReadyButtonAfterDelay());

        // Subscribe to live ready-state updates
        if (PlayerReadyTracker.Instance != null)
        {
            PlayerReadyTracker.Instance.OnReadyStatesUpdated += HandleReadyStatesUpdated;
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated += HandlePlayerLobbyStatesUpdated;
        }
    }

    public void Hide()
    {
        if (_readyDelayCoroutine != null)
        {
            StopCoroutine(_readyDelayCoroutine);
            _readyDelayCoroutine = null;
        }

        if (PlayerReadyTracker.Instance != null)
        {
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated -= HandlePlayerLobbyStatesUpdated;
        }

        DestroyModel();
        SetScreenVisible(false);
    }

    private IEnumerator EnableReadyButtonAfterDelay()
    {
        if (heatReadyButton != null) heatReadyButton.gameObject.SetActive(false);

        yield return new WaitForSecondsRealtime(readyButtonDelay);

        if (heatReadyButton != null) heatReadyButton.gameObject.SetActive(true);

        _readyDelayCoroutine = null;
    }

    // =========================================================================
    //  Match Stake Modal Flow
    // =========================================================================

    private void OnReadyButtonClicked()
    {
        if (_readySent) return;

        if (matchStakeModal != null)
        {
            OpenStakeModal();
        }
        else
        {
            // Direct fallback if no modal assigned
            FinalizeReady(CurrencyConfig.MinimumStake);
        }
    }

    private void EnsureStakeReferences()
    {
        if (stakeErrorText == null && matchStakeModal != null)
        {
            var texts = matchStakeModal.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t.gameObject.name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stakeErrorText = t;
                    break;
                }
            }
        }

        if (stakeErrorText != null)
        {
            stakeErrorText.richText = true;
        }
    }

    private void OpenStakeModal()
    {
        EnsureStakeReferences();

        int balance = CloudCharacterSaveManager.Instance != null
            ? CloudCharacterSaveManager.Instance.CurrentCredits
            : 50;

        // Bind input typing validation
        MichskyUIBridge.BindInputField(null, heatStakeInputField, OnStakeInputChanged);

        // Bind confirm and cancel buttons
        MichskyUIBridge.BindButton(null, heatStakeConfirmButton, OnStakeModalConfirmed);

        if (heatStakeCancelButton != null)
            MichskyUIBridge.BindButton(null, heatStakeCancelButton, OnStakeModalCancelled);

        // Clear input text initially and run initial validation & error state
        MichskyUIBridge.SetInputText(null, heatStakeInputField, string.Empty);
        OnStakeInputChanged(string.Empty);

        matchStakeModal.OpenWindow();
    }

    private void OnStakeInputChanged(string raw)
    {
        EnsureStakeReferences();

        bool isValid = ValidateStakeInput(raw, out int stake, out string error);

        if (stakeErrorText != null)
        {
            if (isValid)
            {
                stakeErrorText.text = $"Ready to stake {CurrencyConfig.Format(stake)}";
                stakeErrorText.gameObject.SetActive(true);
            }
            else
            {
                stakeErrorText.text = error;
                stakeErrorText.gameObject.SetActive(!string.IsNullOrEmpty(error));
            }
        }

        SetStakeConfirmInteractable(isValid);
    }

    private bool ValidateStakeInput(string raw, out int stake, out string errorMessage)
    {
        stake = 0;
        errorMessage = "";

        int balance = CloudCharacterSaveManager.Instance != null
            ? CloudCharacterSaveManager.Instance.CurrentCredits
            : 50;

        if (balance < CurrencyConfig.MinimumStake)
        {
            errorMessage = $"Insufficient credits! You need at least {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol} to participate (Balance: {balance} {CurrencyConfig.CurrencySymbol}).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            errorMessage = $"Enter stake amount ({CurrencyConfig.MinimumStake} - {balance} {CurrencyConfig.CurrencySymbol}) to confirm.";
            return false;
        }

        if (!int.TryParse(raw.Trim(), out stake))
        {
            errorMessage = "Please enter a valid whole number.";
            return false;
        }

        if (stake <= 0)
        {
            errorMessage = $"Stake must be at least {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol}.";
            return false;
        }

        if (stake < CurrencyConfig.MinimumStake)
        {
            errorMessage = $"Stake too low! Minimum required is {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol}.";
            return false;
        }

        if (stake > balance)
        {
            errorMessage = $"Insufficient credits! You cannot stake {stake} {CurrencyConfig.CurrencySymbol} with a balance of {balance} {CurrencyConfig.CurrencySymbol}.";
            return false;
        }

        return true;
    }

    private void SetStakeConfirmInteractable(bool interactable)
    {
        if (heatStakeConfirmButton != null)
        {
            heatStakeConfirmButton.isInteractable = interactable;
            heatStakeConfirmButton.UpdateUI();
        }
        MichskyUIBridge.SetButtonInteractable(null, heatStakeConfirmButton, interactable);
    }

    private void OnStakeModalCancelled()
    {
        if (matchStakeModal != null)
            matchStakeModal.CloseWindow();

        // Player cancelled without inputting/confirming: DO NOT set ready!
    }

    private void OnStakeModalConfirmed()
    {
        string raw = MichskyUIBridge.GetInputText(null, heatStakeInputField);
        if (!ValidateStakeInput(raw, out int stake, out string error))
        {
            if (stakeErrorText != null)
            {
                stakeErrorText.text = error;
                stakeErrorText.gameObject.SetActive(true);
            }
            SetStakeConfirmInteractable(false);
            return;
        }

        if (matchStakeModal != null)
            matchStakeModal.CloseWindow();

        FinalizeReady(stake);
    }

    private void FinalizeReady(int stake)
    {
        if (_readySent) return;

        // ── Show loading screen immediately so the player doesn't see Unity freeze ──
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.ShowImmediate();

        PersistentCharacterSelection.SetSavedMatchStake(stake);
        if (CloudCharacterSaveManager.Instance != null)
        {
            CloudCharacterSaveManager.Instance.SpendCredits(stake);
        }

        _readySent = true;

        // Hide info section and ready button
        if (girlInfoSection != null) girlInfoSection.SetActive(false);
        if (heatReadyButton != null) heatReadyButton.gameObject.SetActive(false);

        if (statProgressBars != null)
        {
            foreach (var bar in statProgressBars)
            {
                if (bar != null) bar.SetVisible(false);
            }
        }

        if (lobbyPlayerStatusPanel != null)
            lobbyPlayerStatusPanel.Show();
        else if (playerStatusPanel != null)
            playerStatusPanel.SetActive(true);

        // Primary path: PlayerReadyTracker
        if (PlayerReadyTracker.Instance != null)
        {
            int localLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetPlayerLevel() : 1;
            PlayerReadyTracker.Instance.ReportGirlReady(localLvl);
        }

        // Legacy fallback: GirlRevealManager
        if (GirlRevealManager.Instance != null)
            GirlRevealManager.Instance.ReportGirlReady();
    }

    private void HandlePlayerLobbyStatesUpdated(Dictionary<ulong, PlayerLobbyInfo> snapshot)
    {
        if (lobbyPlayerStatusPanel != null)
        {
            if (_readySent) lobbyPlayerStatusPanel.Show();
            lobbyPlayerStatusPanel.RefreshLobbyStatusRows(snapshot);
        }
        else if (playerStatusPanel != null)
        {
            playerStatusPanel.SetActive(_readySent);
        }
    }

    private void HandleReadyStatesUpdated(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        if (lobbyPlayerStatusPanel != null)
        {
            if (_readySent) lobbyPlayerStatusPanel.Show();
            lobbyPlayerStatusPanel.RefreshLegacyStatusRows(snapshot);
        }
        else if (playerStatusPanel != null)
        {
            playerStatusPanel.SetActive(_readySent);
        }
    }

    private void SetScreenVisible(bool visible)
    {
        if (girlScreenPanel != null && girlScreenPanel != gameObject)
            girlScreenPanel.SetActive(visible);

        if (girlScreenCamera != null)
            girlScreenCamera.enabled = visible;
    }

    private void SpawnGirlModel()
    {
        DestroyModel();

        Transform pivot = modelPivot;
        if (pivot == null)
        {
            Transform childPivot = transform.Find("ModelPivot");
            pivot = childPivot != null ? childPivot : transform;
        }

        GameObject prefab = girlPrefabOverride;
        if (prefab == null && GameManager.Instance != null)
            prefab = GameManager.Instance.girlPrefab;

        if (prefab == null) return;

        _modelInstance = Instantiate(prefab, pivot.position, pivot.rotation, pivot);

        foreach (MonoBehaviour mb in _modelInstance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is CharacterAnimationController) continue;
            mb.enabled = false;
        }

        _animController = _modelInstance.GetComponent<CharacterAnimationController>();
        if (_animController == null)
            _animController = _modelInstance.AddComponent<CharacterAnimationController>();

        _animController.characterType = CharacterAnimationController.CharacterType.Girl;
        _animController.PlayDanceLoop();
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

    // =========================================================================
    //  Stat Progress Bars
    // =========================================================================

    public void UpdateGirlStatProgressBars()
    {
        if (statProgressBars == null || statProgressBars.Count == 0) return;

        foreach (var bar in statProgressBars)
        {
            if (bar == null) continue;

            bar.SetVisible(true);
            int level = CloudCharacterSaveManager.Instance != null
                ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(bar.statType)
                : 0;
            bar.Refresh(level);
        }
    }
}
