using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.InputSystem;
using NightCrawler.Economy;
using NightCrawler.Systems;
using NightCrawler.UI;
using Michsky.UI.Heat;

/// <summary>
/// SOLID — SRP: Modern Heat UI Deal and Pact System for the Vengeful Spirit (Girl).
/// Powers:
/// 1. Card Selection inside DealPanel (e.g. Kill Player, Loot Body).
/// 2. Direct title & description binding to DealModal without duplicate fields.
/// 3. Modal Configuration with Player Horizontal Selector, Bounded Time Limit (30s-180s),
///    Reward Slider (max 60% of Girl's total credits), and Penalty Slider (max 50% of reward).
/// 4. Error Modal popups that dynamically receive error title and description when constraints are violated.
/// 5. Clean deactivation of cards and backgrounds at match start until toggled.
/// </summary>
public class GirlDealUI : MonoBehaviour
{
    public static GirlDealUI Instance { get; private set; }

    [Header("Main Panels")]
    [Tooltip("The DealPanel containing the scrollable deal cards and background.")]
    public GameObject dealPanel;
    public CanvasGroup canvasGroup;

    [Header("Heat UI Modals")]
    [Tooltip("The DealModal (ModalWindowManager) that pops up when a card is selected.")]
    public ModalWindowManager dealModal;

    [Tooltip("The ErrorModal (ModalWindowManager) displayed when parameters violate rules.")]
    public ModalWindowManager errorModal;

    [Header("Deal Modal Config Controls")]
    [Tooltip("Horizontal Selector for selecting the target living investigator.")]
    public HorizontalSelector playerSelector;

    [Tooltip("SliderManager for the deal reward amount.")]
    public SliderManager rewardSlider;

    [Tooltip("SliderManager for the credit penalty amount.")]
    public SliderManager penaltySlider;

    [Tooltip("SliderManager for completion time (in seconds).")]
    public SliderManager timeSlider;

    [Header("Deal Modal Buttons")]
    public ButtonManager sendButton;
    public ButtonManager cancelButton;

    [Header("Cards Container")]
    [Tooltip("Container where deal cards live (e.g. DealPanel/Deals/Content/List/Layout Group).")]
    public Transform cardsContainer;

    [Header("Hotkeys")]
    [Tooltip("Toggle hotkey (default [B] for Bargain/Deals).")]
    public Key toggleKey = Key.B;

    private readonly List<ulong> _livingPlayerIds = new List<ulong>();
    private string _currentCardTitle = "DEAL PACT";
    private string _currentCardDesc = "";
    private bool _isOpen = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null && dealPanel != null) canvasGroup = dealPanel.GetComponent<CanvasGroup>();

        // Bind buttons
        if (sendButton != null)
        {
            sendButton.onClick.RemoveAllListeners();
            sendButton.onClick.AddListener(OnSendDealClicked);
        }
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(CloseDealModal);
        }

        // Configure sliders
        if (timeSlider != null && timeSlider.mainSlider != null)
        {
            timeSlider.mainSlider.minValue = 30f;
            timeSlider.mainSlider.maxValue = 180f;
            timeSlider.mainSlider.wholeNumbers = true;
            timeSlider.mainSlider.value = 120f;
        }

        // Clean up any default ExitGame calls on Modals
        SanitizeModal(dealModal);
        SanitizeModal(errorModal);

        // Deactivate cards, background, and modals when game starts
        SetVisible(false);
    }

    private void Start()
    {
        HookCardButtons();
        // Ensure strictly hidden and closed at start of match
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen => _isOpen;

    private void Update()
    {
        if (PauseManager.IsGamePaused)
        {
            if (_isOpen) CloseUI();
            return;
        }

        if (IsAnyInputFocused())
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && _isOpen)
            {
                CloseUI();
            }
            return;
        }

        if (Keyboard.current != null)
        {
            bool pressed = Keyboard.current[toggleKey].wasPressedThisFrame;

            if (pressed && IsLocalPlayerGirl())
            {
                ToggleUI();
            }
            else if (_isOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseUI();
            }
        }
    }

    public static bool IsAnyInputFocused()
    {
        if (UnityEngine.EventSystems.EventSystem.current == null) return false;
        var currentObj = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
        if (currentObj == null) return false;
        return currentObj.GetComponent<TMP_InputField>() != null 
            || currentObj.GetComponent<InputField>() != null;
    }

    private bool IsLocalPlayerGirl()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return false;
        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null) return false;

        if (playerObj.TryGetComponent<GirlPossession>(out var possession) && possession.isPossessing.Value)
        {
            return false;
        }

        return playerObj.GetComponent<GirlStealth>() != null 
            || playerObj.GetComponent<GirlMaterialController>() != null 
            || playerObj.GetComponent<GirlPossession>() != null;
    }

    public void ToggleUI()
    {
        _isOpen = !_isOpen;
        SetVisible(_isOpen);
    }

    public void CloseUI()
    {
        _isOpen = false;
        SetVisible(false);
        CloseDealModal();
        CloseErrorModal();
    }

    private void SetVisible(bool visible)
    {
        _isOpen = visible;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        if (dealPanel != null)
        {
            if (dealPanel == gameObject)
            {
                // Deactivate children (Background, Deals) so Update() continues checking hotkeys
                foreach (Transform child in transform)
                {
                    child.gameObject.SetActive(visible);
                }
            }
            else
            {
                dealPanel.SetActive(visible);
            }
        }

        if (visible)
        {
            RefreshLivingPlayers();
            HookCardButtons();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            CloseDealModal();
            CloseErrorModal();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // =========================================================================
    //  Card Binding & Selection
    // =========================================================================

    public void HookCardButtons()
    {
        Transform container = cardsContainer;
        if (container == null && dealPanel != null)
        {
            var lg = dealPanel.GetComponentInChildren<LayoutGroup>(true);
            if (lg != null) container = lg.transform;
        }

        if (container == null) return;

        // Find all ShopButtonManagers on cards
        var shopButtons = container.GetComponentsInChildren<ShopButtonManager>(true);
        foreach (var card in shopButtons)
        {
            string title = !string.IsNullOrEmpty(card.buttonTitle) ? card.buttonTitle : card.gameObject.name;
            string desc = card.buttonDescription;

            // 1. Hook purchaseButton (the "Select" button on the card)
            if (card.purchaseButton != null)
            {
                card.purchaseButton.onClick.RemoveListener(() => OnCardSelected(title, desc));
                card.purchaseButton.onClick.AddListener(() => OnCardSelected(title, desc));
            }

            // 2. Hook onPurchaseClick on ShopButtonManager
            card.onPurchaseClick.RemoveListener(() => OnCardSelected(title, desc));
            card.onPurchaseClick.AddListener(() => OnCardSelected(title, desc));

            // 3. Hook root card onClick
            card.onClick.RemoveListener(() => OnCardSelected(title, desc));
            card.onClick.AddListener(() => OnCardSelected(title, desc));
        }

        // Also check plain UI Buttons if ShopButtonManager not used
        var plainButtons = container.GetComponentsInChildren<Button>(true);
        foreach (var btn in plainButtons)
        {
            if (btn.GetComponent<ShopButtonManager>() != null) continue;
            string title = btn.gameObject.name;
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null && !string.IsNullOrEmpty(tmp.text)) title = tmp.text;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnCardSelected(title, ""));
        }
    }

    public void OnCardSelected(string cardTitle, string cardDesc)
    {
        _currentCardTitle = cardTitle;
        _currentCardDesc = cardDesc;

        OpenDealModal();
    }

    public void OpenDealModal()
    {
        if (dealModal == null) return;

        // Automatically pass selected card's title and description to DealModal
        dealModal.titleText = _currentCardTitle.ToUpper();
        dealModal.descriptionText = _currentCardDesc;
        dealModal.useLocalization = false;
        dealModal.UpdateUI();

        if (dealModal.windowTitle != null) dealModal.windowTitle.text = _currentCardTitle.ToUpper();
        if (dealModal.windowDescription != null) dealModal.windowDescription.text = _currentCardDesc;

        // Refresh player list in selector
        RefreshLivingPlayers();

        // Configure Sliders
        int girlCredits = GetGirlTotalCredits();
        int maxAllowedReward = Mathf.Max(10, Mathf.FloorToInt(girlCredits * 0.60f));

        if (rewardSlider != null && rewardSlider.mainSlider != null)
        {
            rewardSlider.mainSlider.minValue = 10f;
            rewardSlider.mainSlider.maxValue = Mathf.Max(maxAllowedReward, 100f);
            rewardSlider.mainSlider.wholeNumbers = true;
            rewardSlider.mainSlider.value = Mathf.Min(30f, maxAllowedReward);
        }

        if (penaltySlider != null && penaltySlider.mainSlider != null)
        {
            penaltySlider.mainSlider.minValue = 5f;
            penaltySlider.mainSlider.maxValue = 100f;
            penaltySlider.mainSlider.wholeNumbers = true;
            penaltySlider.mainSlider.value = 15f;
        }

        if (timeSlider != null && timeSlider.mainSlider != null)
        {
            timeSlider.mainSlider.minValue = 30f;
            timeSlider.mainSlider.maxValue = 180f;
            timeSlider.mainSlider.wholeNumbers = true;
            timeSlider.mainSlider.value = 120f;
        }

        dealModal.OpenWindow();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseDealModal()
    {
        if (dealModal != null)
        {
            dealModal.CloseWindow();
        }
    }

    public void CloseErrorModal()
    {
        if (errorModal != null)
        {
            errorModal.CloseWindow();
        }
    }

    // =========================================================================
    //  Player Selector Management
    // =========================================================================

    private void RefreshLivingPlayers()
    {
        _livingPlayerIds.Clear();

        if (playerSelector == null || NetworkManager.Singleton == null) return;

        playerSelector.items.Clear();

        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            ulong id = kvp.Key;
            if (id == localId) continue; // Skip Girl herself

            var clientObj = kvp.Value.PlayerObject;
            if (clientObj == null) continue;

            if (clientObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0))
                continue;
            if (clientObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead)
                continue;

            string pName = PlayerNameManager.GetPlayerName(id);
            if (string.IsNullOrEmpty(pName)) pName = $"Investigator {id}";

            _livingPlayerIds.Add(id);
            playerSelector.CreateNewItem(pName);
        }

        if (playerSelector.items.Count == 0)
        {
            playerSelector.CreateNewItem("No Living Investigators");
        }

        playerSelector.useLocalization = false;
        playerSelector.index = 0;
        playerSelector.UpdateUI();
    }

    // =========================================================================
    //  Deal Validation & Dispatch
    // =========================================================================

    public void OnSendDealClicked()
    {
        if (NetworkManager.Singleton == null) return;

        if (_livingPlayerIds.Count == 0)
        {
            ShowError("NO INVESTIGATORS", "There are no living investigators available to receive a dark pact.");
            return;
        }

        int selectedIdx = playerSelector != null ? playerSelector.index : 0;
        if (selectedIdx < 0 || selectedIdx >= _livingPlayerIds.Count)
        {
            ShowError("INVALID TARGET", "Please select a valid living investigator.");
            return;
        }

        ulong targetRecipientId = _livingPlayerIds[selectedIdx];
        string targetPlayerName = playerSelector != null && playerSelector.items.Count > selectedIdx
            ? playerSelector.items[selectedIdx].itemTitle
            : $"Investigator {targetRecipientId}";

        // 1. Read input values
        int rewardAmount = rewardSlider != null && rewardSlider.mainSlider != null
            ? Mathf.RoundToInt(rewardSlider.mainSlider.value)
            : 30;

        int penaltyAmount = penaltySlider != null && penaltySlider.mainSlider != null
            ? Mathf.RoundToInt(penaltySlider.mainSlider.value)
            : 15;

        int completionTime = timeSlider != null && timeSlider.mainSlider != null
            ? Mathf.RoundToInt(timeSlider.mainSlider.value)
            : 120;

        // 2. Validate Rule 1: Reward cannot exceed 60% of Girl's total credit amount
        int girlCredits = GetGirlTotalCredits();
        int maxAllowedReward = Mathf.FloorToInt(girlCredits * 0.60f);

        if (rewardAmount > maxAllowedReward)
        {
            ShowError("REWARD TOO HIGH", 
                $"The reward amount ({rewardAmount} credits) cannot exceed 60% of your total credit balance ({maxAllowedReward} credits).\n\nYour Total Credits: {girlCredits}");
            return;
        }

        // 3. Validate Rule 2: Penalty cannot exceed 50% of the reward amount
        int maxAllowedPenalty = Mathf.FloorToInt(rewardAmount * 0.50f);

        if (penaltyAmount > maxAllowedPenalty)
        {
            ShowError("PENALTY TOO HIGH", 
                $"The credit penalty ({penaltyAmount} credits) cannot exceed 50% of the offered reward ({maxAllowedPenalty} credits).\n\nOffered Reward: {rewardAmount} credits");
            return;
        }

        // 4. Validate Rule 3: Completion time must be between 30s and 180s (3 minutes)
        if (completionTime < 30 || completionTime > 180)
        {
            ShowError("INVALID TIME LIMIT", 
                $"The completion time ({completionTime}s) must be between 30 seconds and 3 minutes (180 seconds).");
            return;
        }

        // All constraints passed! Dispatch deal
        string dealTitle = !string.IsNullOrEmpty(_currentCardTitle) ? _currentCardTitle : "DARK PACT";
        string dealTerms = !string.IsNullOrEmpty(_currentCardDesc) 
            ? _currentCardDesc 
            : $"Complete the objective before the timer expires.\nReward: {rewardAmount} Credits.\nPenalty on failure: -{penaltyAmount} Credits.";
        string rewardStr = $"{rewardAmount} Credits";
        bool grantWeapon = true;

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.SendDeal(targetRecipientId, dealTitle, dealTerms, rewardStr, grantWeapon, completionTime, penaltyAmount);
        }

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"Dark Pact dispatched to {targetPlayerName} ({completionTime}s timer, {rewardAmount}cr reward)!", 3.5f);
        }

        // Close modals and panel
        CloseDealModal();
        CloseUI();
    }

    // =========================================================================
    //  Error Modal Popup
    // =========================================================================

    public void ShowError(string title, string message)
    {
        if (errorModal == null)
        {
            Debug.LogError($"[GirlDealUI] Error: {title} - {message}");
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"<color=#E74C3C>{title}:</color> {message}", 4f);
            }
            return;
        }

        // Automatically pass the exact error title and description into ErrorModal
        errorModal.titleText = title.ToUpper();
        errorModal.descriptionText = message;
        errorModal.useLocalization = false;
        errorModal.UpdateUI();

        if (errorModal.windowTitle != null) errorModal.windowTitle.text = title.ToUpper();
        if (errorModal.windowDescription != null) errorModal.windowDescription.text = message;

        errorModal.OpenWindow();
    }

    private int GetGirlTotalCredits()
    {
        if (CloudCharacterSaveManager.Instance != null)
        {
            return CloudCharacterSaveManager.Instance.GetCredits();
        }
        return 500; // Safe fallback for testing if offline
    }

    private void SanitizeModal(ModalWindowManager modal)
    {
        if (modal == null) return;

        modal.useLocalization = false;
        modal.closeOnCancel = true;
        modal.closeOnConfirm = true;

        // Remove any ExitGame listeners from onConfirm
        modal.onConfirm.RemoveAllListeners();
    }
}
