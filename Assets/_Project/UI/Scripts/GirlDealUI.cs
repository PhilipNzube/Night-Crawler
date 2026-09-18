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
/// Supported pact card templates available to the Vengeful Spirit.
/// </summary>
public enum PactCardType
{
    KillPlayer,
    LootCorpse,
    ManipulateSquad,
    LeadToShadows,
    CustomPact
}

/// <summary>
/// SOLID — SRP: Scrollable Card Deal Creation Interface for the Vengeful Spirit (Girl).
/// Features selectable pact cards (Kill Player, Loot a Body, Manipulate, etc.)
/// with dynamic subject dropdowns, strictly bounded time limit sliders (60s-180s),
/// and automatic stake deduction penalty configuration.
/// </summary>
public class GirlDealUI : MonoBehaviour
{
    public static GirlDealUI Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject mainPanel;
    public CanvasGroup canvasGroup;

    [Header("Recipient Selection")]
    [Tooltip("Dropdown to select which living investigator receives the dark pact.")]
    public TMP_Dropdown recipientDropdown;

    [Header("Scrollable Card Container")]
    [Tooltip("Parent with HorizontalLayoutGroup where pact cards are spawned.")]
    public Transform cardsContainer;
    public GameObject pactCardPrefab;

    [Header("Dynamic Sub-Configuration Panel")]
    public GameObject subConfigPanel;
    public TextMeshProUGUI selectedPactTitleText;
    public TextMeshProUGUI subjectDropdownLabel;
    public TMP_Dropdown subjectDropdown; // Used for "Target Player to Kill" or "Dead Corpse to Loot"
    public TMP_InputField customTitleInput;
    public TMP_InputField customTermsInput;
    [Tooltip("Michsky Heat / Dark UI Inputs")]
    public InputFieldManager heatCustomTitleInput;
    public InputFieldManager heatCustomTermsInput;

    [Header("Time Limit & Penalty (Controlled Bounded Slider)")]
    [Tooltip("Slider for pact timer. Clamped between 60s and 180s to prevent unfair timeframes.")]
    public Slider timeLimitSlider;
    [Tooltip("Michsky Heat / Dark UI Slider Manager")]
    public SliderManager heatTimeLimitSlider;
    public TextMeshProUGUI timeLimitText;
    public TextMeshProUGUI penaltyPreviewText;
    public Toggle grantWeaponToggle;

    [Header("Action Buttons")]
    public Button sendDealButton;
    public Button closeButton;
    [Tooltip("Michsky Heat / Dark UI Action Buttons")]
    public ButtonManager heatSendDealButton;
    public ButtonManager heatCloseButton;

    [Header("Hotkeys")]
    [Tooltip("Primary toggle hotkey (default [B] for Bargain/Pact).")]
    public Key toggleKey = Key.B;

    private readonly List<ulong> _livingPlayerIds = new List<ulong>();
    private readonly List<ulong> _targetSubjectIds = new List<ulong>();
    private PactCardType _selectedCard = PactCardType.KillPlayer;
    private int _selectedTimeLimit = 120;
    private const int PENALTY_CREDITS = 15;
    private bool _isOpen = false;
    private readonly List<Button> _cardButtons = new List<Button>();
    private readonly List<Image> _cardFrames = new List<Image>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        MichskyUIBridge.BindButton(sendDealButton, heatSendDealButton, OnSendDealClicked);
        MichskyUIBridge.BindButton(closeButton, heatCloseButton, CloseUI);

        if (timeLimitSlider != null)
        {
            timeLimitSlider.minValue = 60f;
            timeLimitSlider.maxValue = 180f;
            timeLimitSlider.value = 120f;
            timeLimitSlider.onValueChanged.AddListener(OnTimeLimitChanged);
        }

        if (heatTimeLimitSlider != null)
        {
            heatTimeLimitSlider.minValue = 60f;
            heatTimeLimitSlider.maxValue = 180f;
            heatTimeLimitSlider.currentValue = 120f;
            heatTimeLimitSlider.onValueChanged.AddListener(OnTimeLimitChanged);
            heatTimeLimitSlider.UpdateUI();
        }

        SetVisible(false);
    }

    private void Start()
    {
        BuildPactCards();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen => _isOpen;

    public static bool IsAnyInputFocused()
    {
        if (UnityEngine.EventSystems.EventSystem.current == null) return false;
        var currentObj = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
        if (currentObj == null) return false;
        return currentObj.GetComponent<TMP_InputField>() != null 
            || currentObj.GetComponent<InputField>() != null;
    }

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
            bool keyMatch = (toggleKey != Key.T && Keyboard.current[toggleKey].wasPressedThisFrame);
            bool pressed = keyMatch || Keyboard.current.bKey.wasPressedThisFrame;

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

        if (mainPanel != null && mainPanel != gameObject)
        {
            mainPanel.SetActive(visible);
        }

        if (visible)
        {
            RefreshLivingPlayers();
            SelectPactCard(_selectedCard);
            UpdateTimeLimitLabel();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnTimeLimitChanged(float val)
    {
        _selectedTimeLimit = Mathf.RoundToInt(val);
        UpdateTimeLimitLabel();
    }

    private void UpdateTimeLimitLabel()
    {
        if (timeLimitText != null)
        {
            timeLimitText.text = $"⏱ Time to Complete: <b>{_selectedTimeLimit}s</b> <size=11><color=#BDC3C7>(Allowed: 60s – 180s)</color></size>";
        }

        if (penaltyPreviewText != null)
        {
            penaltyPreviewText.text = $"⚠ <color=#E74C3C>Failure Penalty: -{PENALTY_CREDITS} {CurrencyConfig.CurrencySymbol}</color> (Deducted from recipient's Stake)";
        }
    }

    private void RefreshLivingPlayers()
    {
        _livingPlayerIds.Clear();
        if (recipientDropdown == null || NetworkManager.Singleton == null) return;

        recipientDropdown.ClearOptions();
        List<string> options = new List<string>();

        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            ulong id = kvp.Key;
            if (id == localId) continue;

            var clientObj = kvp.Value.PlayerObject;
            if (clientObj == null) continue;

            if (clientObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0))
                continue;
            if (clientObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead)
                continue;

            string pName = PlayerNameManager.GetPlayerName(id);
            if (string.IsNullOrEmpty(pName)) pName = $"Investigator {id}";

            _livingPlayerIds.Add(id);
            options.Add(pName);
        }

        if (options.Count == 0)
        {
            options.Add("No living investigators");
            MichskyUIBridge.SetButtonInteractable(sendDealButton, heatSendDealButton, false);
        }
        else
        {
            MichskyUIBridge.SetButtonInteractable(sendDealButton, heatSendDealButton, true);
        }

        recipientDropdown.AddOptions(options);
    }

    // =========================================================================
    //  Card Generation & Selection
    // =========================================================================

    private void BuildPactCards()
    {
        if (cardsContainer == null) return;

        foreach (Transform child in cardsContainer)
            Destroy(child.gameObject);

        _cardButtons.Clear();
        _cardFrames.Clear();

        CreateCardInstance(PactCardType.KillPlayer, "BLOOD PACT", "Eliminate a squad member within the time limit.");
        CreateCardInstance(PactCardType.LootCorpse, "GRAVE ROBBER", "Locate and loot a fallen investigator's corpse.");
        CreateCardInstance(PactCardType.ManipulateSquad, "THE BETRAYER", "Sow chaos and guide investigators into danger.");
        CreateCardInstance(PactCardType.LeadToShadows, "DARK GUIDE", "Lure squad members into dark unlit mine tunnels.");
        CreateCardInstance(PactCardType.CustomPact, "CUSTOM PACT", "Type custom dark terms for the investigator.");

        SelectPactCard(PactCardType.KillPlayer);
    }

    private void CreateCardInstance(PactCardType type, string title, string description)
    {
        GameObject cardObj = null;
        if (pactCardPrefab != null)
        {
            cardObj = Instantiate(pactCardPrefab, cardsContainer);
        }
        else
        {
            cardObj = new GameObject($"Card_{type}", typeof(RectTransform), typeof(Image), typeof(Button));
            cardObj.transform.SetParent(cardsContainer, false);

            var rect = cardObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(150, 190);

            var img = cardObj.GetComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);

            var vGroup = cardObj.AddComponent<VerticalLayoutGroup>();
            vGroup.padding = new RectOffset(10, 10, 10, 10);
            vGroup.spacing = 8f;
            vGroup.childControlWidth = true;
            vGroup.childControlHeight = false;

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(cardObj.transform, false);
            var titleTxt = titleGo.GetComponent<TextMeshProUGUI>();
            titleTxt.text = $"<b>{title}</b>";
            titleTxt.fontSize = 13;
            titleTxt.color = new Color(0.95f, 0.8f, 0.3f);
            titleTxt.alignment = TextAlignmentOptions.Center;

            var descGo = new GameObject("Desc", typeof(RectTransform), typeof(TextMeshProUGUI));
            descGo.transform.SetParent(cardObj.transform, false);
            var descTxt = descGo.GetComponent<TextMeshProUGUI>();
            descTxt.text = description;
            descTxt.fontSize = 10;
            descTxt.color = new Color(0.8f, 0.8f, 0.8f);
            descTxt.alignment = TextAlignmentOptions.Top;
            descTxt.enableWordWrapping = true;
        }

        var btn = cardObj.GetComponent<Button>();
        var frameImg = cardObj.GetComponent<Image>();
        if (btn != null)
        {
            btn.onClick.AddListener(() => SelectPactCard(type));
            _cardButtons.Add(btn);
        }
        if (frameImg != null)
        {
            _cardFrames.Add(frameImg);
        }
    }

    public void SelectPactCard(PactCardType type)
    {
        _selectedCard = type;
        if (selectedPactTitleText != null)
        {
            selectedPactTitleText.text = $"SELECTED: <color=#F1C40F>{type}</color>";
        }

        bool needsSubject = (type == PactCardType.KillPlayer || type == PactCardType.LootCorpse);
        bool isCustom = (type == PactCardType.CustomPact);

        if (subjectDropdown != null) subjectDropdown.gameObject.SetActive(needsSubject);
        if (subjectDropdownLabel != null) subjectDropdownLabel.gameObject.SetActive(needsSubject);

        if (customTitleInput != null) customTitleInput.gameObject.SetActive(isCustom);
        if (heatCustomTitleInput != null) heatCustomTitleInput.gameObject.SetActive(isCustom);
        if (customTermsInput != null) customTermsInput.gameObject.SetActive(isCustom || type == PactCardType.ManipulateSquad);
        if (heatCustomTermsInput != null) heatCustomTermsInput.gameObject.SetActive(isCustom || type == PactCardType.ManipulateSquad);

        if (needsSubject)
        {
            RefreshSubjectDropdown(type);
        }
    }

    private void RefreshSubjectDropdown(PactCardType type)
    {
        if (subjectDropdown == null) return;
        subjectDropdown.ClearOptions();
        List<string> options = new List<string>();

        if (type == PactCardType.KillPlayer)
        {
            if (subjectDropdownLabel != null) subjectDropdownLabel.text = "Target to Assassinate:";
            foreach (ulong id in _livingPlayerIds)
            {
                string pName = PlayerNameManager.GetPlayerName(id);
                if (string.IsNullOrEmpty(pName)) pName = $"Player {id}";
                options.Add(pName);
            }
            if (options.Count == 0) options.Add("No valid targets");
        }
        else if (type == PactCardType.LootCorpse)
        {
            if (subjectDropdownLabel != null) subjectDropdownLabel.text = "Corpse to Desecrate/Loot:";
            // Search dead corpses in scene
            var allHealths = FindObjectsByType<TargetHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var h in allHealths)
            {
                if (h.isCorpse.Value || h.CurrentHealth <= 0)
                {
                    options.Add(h.gameObject.name.Replace("(Clone)", ""));
                }
            }
            if (options.Count == 0) options.Add("No corpses discovered");
        }

        subjectDropdown.AddOptions(options);
    }

    // =========================================================================
    //  Dispatch Deal
    // =========================================================================

    public void OnSendDealClicked()
    {
        if (NetworkManager.Singleton == null) return;
        if (_livingPlayerIds.Count == 0)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("No living investigators available to receive pact!", 3f);
            }
            return;
        }

        int recipientIdx = recipientDropdown != null ? recipientDropdown.value : 0;
        if (recipientIdx < 0 || recipientIdx >= _livingPlayerIds.Count) return;

        ulong targetRecipientId = _livingPlayerIds[recipientIdx];

        string title = "DARK PACT";
        string terms = "";
        string reward = grantWeaponToggle != null && grantWeaponToggle.isOn ? "Melee Pickaxe / Weapon" : "Immunity";
        bool grantWeapon = grantWeaponToggle != null ? grantWeaponToggle.isOn : true;

        string subjectName = "a teammate";
        if (subjectDropdown != null && subjectDropdown.options.Count > 0)
        {
            int subIdx = Mathf.Clamp(subjectDropdown.value, 0, subjectDropdown.options.Count - 1);
            subjectName = subjectDropdown.options[subIdx].text;
        }

        switch (_selectedCard)
        {
            case PactCardType.KillPlayer:
                title = $"BLOOD PACT: Eliminate {subjectName}";
                terms = $"Eliminate your fellow investigator '{subjectName}' before the timer expires.\nReward: Melee Axe Weapon.";
                break;

            case PactCardType.LootCorpse:
                title = $"GRAVE ROBBER: Loot {subjectName}";
                terms = $"Locate and loot all supplies from the corpse of '{subjectName}' before the timer expires.";
                break;

            case PactCardType.ManipulateSquad:
                title = "THE BETRAYER: Mislead the Squad";
                string termsInput = MichskyUIBridge.GetInputText(customTermsInput, heatCustomTermsInput);
                string extraTerms = !string.IsNullOrEmpty(termsInput) ? termsInput : "Separate from your squad and guide them away from the ritual site.";
                terms = $"{extraTerms}";
                break;

            case PactCardType.LeadToShadows:
                title = "DARK GUIDE: Lure into the Deep";
                terms = "Guide investigators into the unlit tunnels of the mine.";
                break;

            case PactCardType.CustomPact:
                string customT = MichskyUIBridge.GetInputText(customTitleInput, heatCustomTitleInput);
                title = !string.IsNullOrEmpty(customT) ? customT : "CUSTOM PACT";
                terms = MichskyUIBridge.GetInputText(customTermsInput, heatCustomTermsInput);
                break;
        }

        // Clamp time limit strictly between 60s and 180s
        int clampedTime = Mathf.Clamp(_selectedTimeLimit, 60, 180);

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.SendDeal(targetRecipientId, title, terms, reward, grantWeapon, clampedTime, PENALTY_CREDITS);
        }

        string recipientName = recipientDropdown.options[recipientIdx].text;
        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"Dark Pact dispatched to {recipientName} with {clampedTime}s timer!", 3.5f);
        }

        CloseUI();
    }
}
