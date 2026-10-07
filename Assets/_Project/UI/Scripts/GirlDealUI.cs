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
    [Tooltip("Animator on the Deals child object running SubPanel.controller.")]
    public Animator dealsAnimator;

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

    [Header("Deal Capacity & Slots")]
    [Tooltip("Capsule GameObject displaying remaining deal slots (DealSlotsLeft).")]
    public GameObject dealSlotsLeftCapsule;
    [Tooltip("Text displaying remaining deal slots (e.g. Total).")]
    public TextMeshProUGUI dealSlotsLeftText;

    [Header("Cards Container")]
    [Tooltip("Container where deal cards live (e.g. DealPanel/Deals/Content/List/Layout Group).")]
    public Transform cardsContainer;

    [Header("Progression & Inspector Testing (Dev Purposes)")]
    [Tooltip("If >= 0, overrides the max deals capacity with this custom number for dev/testing purposes (-1 uses normal upgrade level).")]
    public int debugOverrideMaxDeals = -1;

    [Tooltip("Override deal capacity upgrade level for testing (-1 uses real save level, 0 to 5 forces specific level).")]
    [Range(-1, 5)]
    public int debugOverrideDealLevel = -1;

    [Tooltip("If checked, gives infinite deal sending (remaining deals never depletes).")]
    public bool debugInfiniteDeals = false;

    [Header("Hotkeys")]
    [Tooltip("Toggle hotkey (default [B] for Bargain/Deals).")]
    public Key toggleKey = Key.B;

    private readonly List<ulong> _livingPlayerIds = new List<ulong>();
    private string _currentCardTitle = "DARK DEAL";
    private string _currentCardDesc = "";
    private bool _isOpen = false;
    private bool _isGirl = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // In the scene, dealPanel may be assigned to DemonPanel (this gameObject) or null.
        // Resolve it specifically to the child "DealPanel" so we never deactivate or hide sibling modals.
        if (dealPanel == null || dealPanel == gameObject)
        {
            Transform childDealPanel = transform.Find("DealPanel");
            if (childDealPanel != null) dealPanel = childDealPanel.gameObject;
        }

        // DealPanel visibility is controlled via CanvasGroup (alpha: 0 when closed, 1 when open)
        if (canvasGroup == null && dealPanel != null)
        {
            canvasGroup = dealPanel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = dealPanel.AddComponent<CanvasGroup>();
        }

        // Find the SubPanel Animator on Deals (child of DealPanel)
        if (dealsAnimator == null && dealPanel != null)
        {
            dealsAnimator = dealPanel.GetComponentInChildren<Animator>(true);
        }

        // Start hidden via CanvasGroup — do NOT call SetActive(false) on panels/parents
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (dealsAnimator != null)
        {
            dealsAnimator.Play("Panel Instant Out");
        }

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
            timeSlider.useRoundValue = true;
            timeSlider.mainSlider.minValue = 30f;
            timeSlider.mainSlider.maxValue = 180f;
            timeSlider.mainSlider.wholeNumbers = true;
            timeSlider.mainSlider.value = 120f;
        }
        if (rewardSlider != null) rewardSlider.useRoundValue = true;
        if (penaltySlider != null) penaltySlider.useRoundValue = true;



        // Clean up any default ExitGame calls on Modals
        SanitizeModal(dealModal);
        SanitizeModal(errorModal);
    }

    private void Start()
    {
        HookCardButtons();

        // Ensure deal panel starts hidden via CanvasGroup
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (dealsAnimator != null)
        {
            dealsAnimator.Play("Panel Instant Out");
        }
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

        if (SpectatorController.IsAnySpectating)
        {
            if (_isOpen) CloseUI();
            return;
        }

        if (IsAnyInputFocused())
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && _isOpen)
                CloseUI();
            return;
        }

        bool triggerDeal = KeybindingManager.IsActionTriggered("Deal") 
            || (KeybindingManager.Instance == null && Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame);
        if (triggerDeal && IsLocalPlayerGirl())
        {
            if (!_isOpen)
            {
                // Cannot open Deal UI if another panel or modal is currently active!
                if (IsAnyPanelOrModalOpen()) return;
                OpenUI();
            }
            else
            {
                CloseUI();
            }
        }
        else if (_isOpen && (KeybindingManager.IsActionTriggered("SpectateExit") || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)))
        {
            CloseUI();
        }
    }

    public static bool IsAnyPanelOrModalOpen()
    {
        if (PauseManager.IsGamePaused) return true;
        if (IsAnyInputFocused()) return true;

        if (Instance != null && Instance._isOpen) return true;
        if (NightCrawler.Monsters.GirlMonsterSummonHUD.Instance != null && NightCrawler.Monsters.GirlMonsterSummonHUD.Instance.IsOpen) return true;

        if (Instance != null)
        {
            if (Instance.dealModal != null && Instance.dealModal.isOn) return true;
            if (Instance.errorModal != null && Instance.errorModal.isOn) return true;
        }

        if (NightCrawler.Monsters.GirlMonsterSummonHUD.Instance != null)
        {
            var summon = NightCrawler.Monsters.GirlMonsterSummonHUD.Instance;
            if (summon.selectionConfirmationModal != null && summon.selectionConfirmationModal.IsOpen) return true;
            if (summon.confirmationModal != null && summon.confirmationModal.isOn) return true;
        }

        return false;
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

        // If currently possessing an investigator, cannot open deals
        if (playerObj.TryGetComponent<GirlPossession>(out var possession) && possession.isPossessing.Value)
            return false;

        if (_isGirl) return true;

        bool result = playerObj.GetComponent<GirlStealth>() != null
            || playerObj.GetComponent<GirlMaterialController>() != null
            || playerObj.GetComponent<GirlPossession>() != null;

        if (result) _isGirl = true;
        return result;
    }

    public void ToggleUI()
    {
        if (_isOpen) CloseUI();
        else OpenUI();
    }

    public void OpenUI()
    {
        _isOpen = true;
        RefreshLivingPlayers();
        HookCardButtons();

        // 1. Reveal DealPanel via CanvasGroup (alpha = 1)
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        // 2. Play 'Panel In' on the Deals Animator (SubPanel.controller) so the cards animate in
        if (dealsAnimator != null)
        {
            dealsAnimator.Play("Panel In");
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseUI()
    {
        _isOpen = false;

        // Close sub-modals if open
        if (dealModal  != null) dealModal.CloseWindow();
        if (errorModal != null) errorModal.CloseWindow();

        // Animate Deals cards out
        if (dealsAnimator != null)
        {
            dealsAnimator.Play("Panel Instant Out");
        }

        // Hide DealPanel via CanvasGroup
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // =========================================================================
    //  Card Binding & Selection
    // =========================================================================

    // =========================================================================
    //  Card Hooking & Setup
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

        var handledButtons = new HashSet<Button>();

        // 1. Find all ShopButtonManagers on cards
        var shopButtons = container.GetComponentsInChildren<ShopButtonManager>(true);
        foreach (var card in shopButtons)
        {
            // Ensure Michsky Heat localization does not overwrite card title/description with sample keys
            card.useLocalization = false;
            card.titleLocalizationKey = string.Empty;
            card.descriptionLocalizationKey = string.Empty;

            string title = ResolveCardTitle(card.gameObject, card);
            string desc = ResolveCardDesc(card.gameObject, card);

            // Hook purchaseButton (the action button on the card)
            if (card.purchaseButton != null)
            {
                var pBtn = card.purchaseButton.GetComponentInChildren<Button>(true);
                if (pBtn != null) handledButtons.Add(pBtn);

                card.purchaseButton.onClick.RemoveAllListeners();
                card.purchaseButton.onClick.AddListener(() => OnCardSelected(title, desc));
            }

            // Hook onPurchaseClick on ShopButtonManager
            card.onPurchaseClick.RemoveAllListeners();
            card.onPurchaseClick.AddListener(() => OnCardSelected(title, desc));

            // Hook root card onClick
            card.onClick.RemoveAllListeners();
            card.onClick.AddListener(() => OnCardSelected(title, desc));

            var rootBtn = card.GetComponent<Button>();
            if (rootBtn != null) handledButtons.Add(rootBtn);
        }

        // 2. Also check plain UI Buttons if ShopButtonManager not used
        var plainButtons = container.GetComponentsInChildren<Button>(true);
        foreach (var btn in plainButtons)
        {
            if (handledButtons.Contains(btn)) continue;
            // Ignore any child buttons belonging to a ShopButtonManager
            if (btn.GetComponentInParent<ShopButtonManager>() != null) continue;

            // Resolve parent card root
            GameObject cardObj = btn.gameObject;
            if (btn.transform.parent != null && btn.transform.parent != container)
            {
                cardObj = btn.transform.parent.gameObject;
            }

            string title = ResolveCardTitle(cardObj, null);
            string desc = ResolveCardDesc(cardObj, null);

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnCardSelected(title, desc));
        }
    }

    private string ResolveCardTitle(GameObject cardObj, ShopButtonManager shopBtn)
    {
        // 1. Check titleObj on ShopButtonManager
        if (shopBtn != null && shopBtn.titleObj != null && !string.IsNullOrWhiteSpace(shopBtn.titleObj.text))
        {
            string t = shopBtn.titleObj.text.Trim();
            if (!IsGenericButtonLabel(t)) return t;
        }

        // 2. Check buttonTitle on ShopButtonManager
        if (shopBtn != null && !string.IsNullOrWhiteSpace(shopBtn.buttonTitle))
        {
            string t = shopBtn.buttonTitle.Trim();
            if (!IsGenericButtonLabel(t)) return t;
        }

        // 3. Search child TextMeshProUGUI with "title", "header", or "name" in GameObject name
        var texts = cardObj.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in texts)
        {
            if (txt == null || string.IsNullOrWhiteSpace(txt.text)) continue;
            string t = txt.text.Trim();
            if (IsGenericButtonLabel(t)) continue;
            string goName = txt.gameObject.name.ToLowerInvariant();
            if (goName.Contains("title") || goName.Contains("header") || goName.Contains("name"))
            {
                return t;
            }
        }

        // 4. Check all child texts for the first meaningful non-generic label
        foreach (var txt in texts)
        {
            if (txt == null || string.IsNullOrWhiteSpace(txt.text)) continue;
            string t = txt.text.Trim();
            if (!IsGenericButtonLabel(t)) return t;
        }

        // 5. Fallback to card GameObject name
        string objName = cardObj.name.Replace("(Clone)", "").Trim();
        if (!IsGenericButtonLabel(objName)) return objName;

        // 6. Intelligent Fallback by sibling index or card hints
        int siblingIdx = cardObj.transform.GetSiblingIndex();
        if (siblingIdx == 1 || objName.IndexOf("loot", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "LOOT CORPSE";
        }

        return "ELIMINATE INVESTIGATOR";
    }

    private string ResolveCardDesc(GameObject cardObj, ShopButtonManager shopBtn)
    {
        if (shopBtn != null && shopBtn.descriptionObj != null && !string.IsNullOrWhiteSpace(shopBtn.descriptionObj.text))
            return shopBtn.descriptionObj.text.Trim();

        if (shopBtn != null && !string.IsNullOrWhiteSpace(shopBtn.buttonDescription))
            return shopBtn.buttonDescription.Trim();

        var texts = cardObj.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in texts)
        {
            if (txt == null || string.IsNullOrWhiteSpace(txt.text)) continue;
            string goName = txt.gameObject.name.ToLowerInvariant();
            if (goName.Contains("desc") || goName.Contains("detail") || goName.Contains("term"))
            {
                return txt.text.Trim();
            }
        }

        // Intelligent Fallback by sibling index or card name
        int siblingIdx = cardObj.transform.GetSiblingIndex();
        string objName = cardObj.name.ToLowerInvariant();
        if (siblingIdx == 1 || objName.Contains("loot") || objName.Contains("corpse"))
        {
            return "Locate and loot a fallen investigator's corpse to claim your bounty.";
        }

        return "Eliminate an investigator to claim your reward.";
    }

    private static bool IsGenericButtonLabel(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        string lower = s.ToLowerInvariant().Trim();
        return lower == "select" || lower == "selected" || lower == "buy" || lower == "purchase" ||
               lower == "button" || lower == "btn" || lower == "click" || lower == "submit";
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

        // Configure Sliders (whole numbers only, no decimal places, typing disabled)
        int girlCredits = GetGirlTotalCredits();
        int maxAllowedReward = Mathf.Max(10, Mathf.FloorToInt(girlCredits * 0.60f));

        if (rewardSlider != null && rewardSlider.mainSlider != null)
        {
            rewardSlider.mainSlider.minValue = 10f;
            rewardSlider.mainSlider.maxValue = Mathf.Max(maxAllowedReward, 100f);
            rewardSlider.mainSlider.value = Mathf.Min(30f, maxAllowedReward);
            ConfigureSliderIntegers(rewardSlider);
        }

        if (penaltySlider != null && penaltySlider.mainSlider != null)
        {
            penaltySlider.mainSlider.minValue = 5f;
            penaltySlider.mainSlider.maxValue = 100f;
            penaltySlider.mainSlider.value = 15f;
            ConfigureSliderIntegers(penaltySlider);
        }

        if (timeSlider != null && timeSlider.mainSlider != null)
        {
            timeSlider.mainSlider.minValue = 30f;
            timeSlider.mainSlider.maxValue = 180f;
            timeSlider.mainSlider.value = 120f;
            ConfigureSliderIntegers(timeSlider);
        }

        // Update slots left
        UpdateDealSlotsDisplay();

        // Ensure modal is active before opening
        if (!dealModal.gameObject.activeSelf)
            dealModal.gameObject.SetActive(true);
        dealModal.OpenWindow();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ConfigureSliderIntegers(SliderManager sm)
    {
        if (sm == null || sm.mainSlider == null) return;
        sm.useRoundValue = true;
        sm.mainSlider.wholeNumbers = true;

        var si = sm.GetComponentInChildren<SliderInput>(true);
        if (si != null)
        {
            si.decimals = 0;
            si.enabled = false; // Prevents any miswired SliderInput in inspector from hijacking valueText
        }

        var inputField = sm.GetComponentInChildren<TMP_InputField>(true);
        if (inputField != null)
        {
            inputField.readOnly = true;
            inputField.interactable = false;
        }

        sm.UpdateUI();
    }

    private int _dealsSentThisMatch = 0;

    public int GetEffectiveDealCapacity()
    {
        if (debugOverrideMaxDeals >= 0) return debugOverrideMaxDeals;
        int capLvl = debugOverrideDealLevel >= 0 
            ? debugOverrideDealLevel 
            : (CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.DealCapacity) : 0);
        return UpgradeStatFormulas.GetGirlDealCapacity(capLvl);
    }

    public void UpdateDealSlotsDisplay()
    {
        int maxDeals = GetEffectiveDealCapacity();
        int remainingSlots = debugInfiniteDeals ? maxDeals : Mathf.Max(0, maxDeals - _dealsSentThisMatch);

        if (dealSlotsLeftCapsule != null)
        {
            dealSlotsLeftCapsule.SetActive(true);
        }

        if (dealSlotsLeftText != null)
        {
            dealSlotsLeftText.text = $"{remainingSlots} / {maxDeals}";
        }

        if (sendButton != null)
        {
            sendButton.Interactable(debugInfiniteDeals || remainingSlots > 0);
        }
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

    // ForceClose* kept for backward compat
    private void ForceCloseDealModal()  => CloseDealModal();
    private void ForceCloseErrorModal() => CloseErrorModal();

    // =========================================================================
    //  Player Selector Management
    // =========================================================================

    private void RefreshLivingPlayers()
    {
        _livingPlayerIds.Clear();

        if (playerSelector == null || NetworkManager.Singleton == null) return;

        playerSelector.items.Clear();

        ulong localId = NetworkManager.Singleton.LocalClientId;
        var candidateIds = new HashSet<ulong>();

        // 1. From ConnectedClientsIds (available on both Host and Client in Netcode for GameObjects)
        if (NetworkManager.Singleton.ConnectedClientsIds != null)
        {
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (id != localId) candidateIds.Add(id);
            }
        }

        // 2. From ConnectedClients (Server / Host only)
        if (NetworkManager.Singleton.IsServer && NetworkManager.Singleton.ConnectedClients != null)
        {
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                if (kvp.Key != localId) candidateIds.Add(kvp.Key);
            }
        }

        // 3. Fallback: inspect SpawnManager for all player NetworkObjects
        if (NetworkManager.Singleton.SpawnManager != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects != null)
        {
            foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
            {
                if (netObj != null && netObj.IsPlayerObject && netObj.OwnerClientId != localId)
                {
                    candidateIds.Add(netObj.OwnerClientId);
                }
            }
        }

        foreach (ulong id in candidateIds)
        {
            NetworkObject clientObj = null;
            if (NetworkManager.Singleton.IsServer)
            {
                if (NetworkManager.Singleton.ConnectedClients != null &&
                    NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var client))
                {
                    clientObj = client.PlayerObject;
                }
                if (clientObj == null && NetworkManager.Singleton.SpawnManager != null)
                {
                    clientObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(id);
                }
            }
            else
            {
                if (id == NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.SpawnManager != null)
                {
                    clientObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                }
            }

            // Fallback for both server and client: inspect replicated SpawnedObjects
            if (clientObj == null && NetworkManager.Singleton.SpawnManager != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects != null)
            {
                foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
                {
                    if (netObj != null && netObj.OwnerClientId == id &&
                        (netObj.IsPlayerObject || netObj.GetComponent<NetworkPlayerName>() != null || netObj.CompareTag("Player")))
                    {
                        clientObj = netObj;
                        break;
                    }
                }
            }

            if (clientObj != null)
            {
                if (clientObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0))
                    continue;
                if (clientObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead)
                    continue;
            }

            string pName = PlayerNameManager.GetPlayerName(id);
            if ((string.IsNullOrEmpty(pName) || pName.StartsWith("Player ") || pName.StartsWith("Investigator ")) && clientObj != null)
            {
                var netName = clientObj.GetComponent<NetworkPlayerName>();
                if (netName != null && !string.IsNullOrEmpty(netName.playerName.Value.ToString()))
                {
                    pName = netName.playerName.Value.ToString();
                }
            }
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
            ShowError("NO INVESTIGATORS", "There are no living investigators available to receive a dark deal.");
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

        // Rule: If target player already has a pending offer, block sending until they accept or decline
        if (DealSystemNet.Instance != null && DealSystemNet.Instance.HasPendingOffer(targetRecipientId))
        {
            ShowError("OFFER PENDING", $"{targetPlayerName} has not accepted or declined your previous offer yet.\n\nPlease wait for their response before offering another deal.");
            return;
        }

        // Rule: If target player already has an active deal in progress, block sending another deal
        if (DealSystemNet.Instance != null && DealSystemNet.Instance.HasActiveDeal(targetRecipientId))
        {
            ShowError("DEAL IN PROGRESS", $"{targetPlayerName} already has an active dark deal in progress.\n\nYou cannot offer another deal to this investigator until their current deal is completed or expires.");
            return;
        }

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

        int maxDeals = GetEffectiveDealCapacity();
        int remainingSlots = debugInfiniteDeals ? maxDeals : Mathf.Max(0, maxDeals - _dealsSentThisMatch);

        if (remainingSlots <= 0 && !debugInfiniteDeals)
        {
            ShowError("DEAL CAPACITY REACHED", 
                $"You have exhausted all {maxDeals} dark deal slots for this match.\n\nUpgrade Deal Capacity in the store to offer more deals per match.");
            return;
        }

        // CRITICAL: Loot Body deal must NOT grant any weapon abilities! Only assassination/kill deals grant weapons.
        bool isLootDeal = (!string.IsNullOrEmpty(_currentCardTitle) && _currentCardTitle.ToLower().Contains("loot"))
            || (!string.IsNullOrEmpty(_currentCardDesc) && _currentCardDesc.ToLower().Contains("loot"));
        bool grantWeapon = !isLootDeal;

        // All constraints passed! Dispatch deal
        string dealTitle = !string.IsNullOrWhiteSpace(_currentCardTitle) 
            ? _currentCardTitle 
            : (isLootDeal ? "LOOT CORPSE" : "ELIMINATE INVESTIGATOR");

        string dealTerms = !string.IsNullOrWhiteSpace(_currentCardDesc) 
            ? _currentCardDesc 
            : (isLootDeal 
                ? "Locate and loot a fallen investigator's corpse to claim your bounty." 
                : "Eliminate an investigator to claim your reward.");

        string rewardStr = $"{rewardAmount} Credits";

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.SendDeal(targetRecipientId, dealTitle, dealTerms, rewardStr, grantWeapon, completionTime, penaltyAmount);
        }

        _dealsSentThisMatch++;
        UpdateDealSlotsDisplay();

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"Dark Deal dispatched to {targetPlayerName} ({completionTime}s timer, {rewardAmount}cr reward)!", 3.5f);
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
                NotificationManager.Instance.ShowNotification(title, message, 4f);
            }
            return;
        }

        // Automatically pass the exact title and description into ErrorModal
        string cleanTitle = title.ToUpper();
        errorModal.titleText = cleanTitle;
        errorModal.descriptionText = message;
        errorModal.useLocalization = false;
        errorModal.titleKey = string.Empty;
        errorModal.descriptionKey = string.Empty;

        if (errorModal.windowTitle != null) errorModal.windowTitle.text = cleanTitle;
        if (errorModal.windowDescription != null) errorModal.windowDescription.text = message;

        try { errorModal.UpdateUI(); } catch { }

        errorModal.OpenWindow();

        // Re-enforce windowTitle after OpenWindow to prevent any animator/localization reverts
        if (errorModal.windowTitle != null) errorModal.windowTitle.text = cleanTitle;
    }

    public void ShowWarningModal(string title, string message) => ShowError(title, message);

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
