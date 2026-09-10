using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Dedicated Possession interface for the Vengeful Spirit (Girl).
/// Allows viewing living investigators, selecting a target to possess, monitoring the
/// possession time bank, and triggering possession or ejecting.
/// Designed to sit neatly inside DemonPanel alongside GirlDealPanel.
/// </summary>
public class GirlPossessionUI : MonoBehaviour
{
    public static GirlPossessionUI Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject mainPanel;

    [Header("Target Selection")]
    public TMP_Dropdown targetDropdown;

    [Header("Possession Time Pool")]
    public TMP_Text timeBankText;
    public Slider timeBankSlider;

    [Header("Action Buttons")]
    public Button possessButton;
    public Button releaseButton;
    public Button closeButton;

    [Header("Hotkeys")]
    [Tooltip("Primary toggle hotkey (default [P] for Possession).")]
    public Key toggleKey = Key.P;

    private readonly List<ulong> _targetClientIds = new List<ulong>();
    private CanvasGroup _canvasGroup;
    private bool _isOpen = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (possessButton != null) possessButton.onClick.AddListener(OnPossessClicked);
        if (releaseButton != null) releaseButton.onClick.AddListener(OnReleaseClicked);
        if (closeButton != null) closeButton.onClick.AddListener(CloseUI);

        // Auto-build visual UI if unassigned in Inspector
        if (mainPanel == null)
        {
            BuildDefaultPossessionUI();
        }

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PauseManager.IsGamePaused)
        {
            if (_isOpen) CloseUI();
            return;
        }

        // Toggle possession menu with hotkey [P]
        if (Keyboard.current != null)
        {
            bool pressed = (Keyboard.current[toggleKey].wasPressedThisFrame);

            if (pressed && IsLocalPlayerGirl())
            {
                ToggleUI();
            }
            else if (_isOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseUI();
            }
        }

        // Continually enforce cursor retention and update time bank while open
        if (_isOpen)
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;

            UpdateTimeBankDisplay();
        }
    }

    private bool IsLocalPlayerGirl()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return false;
        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null) return false;

        return playerObj.GetComponent<GirlStealth>() != null 
            || playerObj.GetComponent<GirlMaterialController>() != null 
            || playerObj.GetComponent<GirlPossession>() != null;
    }

    private GirlPossession GetLocalGirlPossession()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return null;
        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        return playerObj != null ? playerObj.GetComponent<GirlPossession>() : null;
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

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        if (mainPanel != null && mainPanel != gameObject)
        {
            mainPanel.SetActive(visible);
        }

        if (visible)
        {
            RefreshLivingTargets();
            UpdateTimeBankDisplay();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetPlayerLookInputs(false);
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SetPlayerLookInputs(true);
        }
    }

    private void SetPlayerLookInputs(bool allowLookAndLock)
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var inputs = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.cursorLocked = allowLookAndLock;
                inputs.cursorInputForLook = allowLookAndLock;
            }
        }
    }

    private void RefreshLivingTargets()
    {
        _targetClientIds.Clear();
        if (targetDropdown == null || NetworkManager.Singleton == null) return;

        targetDropdown.ClearOptions();
        List<string> options = new List<string>();

        ulong localId = NetworkManager.Singleton.LocalClientId;
        var girlPossession = GetLocalGirlPossession();
        bool isAlreadyPossessing = girlPossession != null && girlPossession.isPossessing.Value;

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            ulong clientId = kvp.Key;
            if (clientId == localId) continue; // Skip self

            var clientObj = kvp.Value.PlayerObject;
            if (clientObj == null) continue;

            // Check if investigator is dead
            if (clientObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0))
            {
                continue; // Skip dead bodies
            }

            string charName = GirlRevealManager.GetRegisteredPlayerName(clientId);
            if (string.IsNullOrEmpty(charName)) charName = PlayerNameManager.GetPlayerName(clientId);
            if (string.IsNullOrEmpty(charName)) charName = $"Investigator {clientId}";

            string roleName = clientObj.name.Replace("(Clone)", "").Trim();

            _targetClientIds.Add(clientId);
            options.Add($"{charName} ({roleName})");
        }

        if (options.Count == 0)
        {
            options.Add("No living investigators nearby");
            if (possessButton != null) possessButton.interactable = false;
        }
        else
        {
            if (possessButton != null) possessButton.interactable = !isAlreadyPossessing;
        }

        targetDropdown.AddOptions(options);

        // Update button states depending on whether girl is already possessing someone
        if (possessButton != null) possessButton.gameObject.SetActive(!isAlreadyPossessing);
        if (releaseButton != null) releaseButton.gameObject.SetActive(isAlreadyPossessing);
    }

    private void UpdateTimeBankDisplay()
    {
        var girlPossession = GetLocalGirlPossession();
        if (girlPossession == null) return;

        float remaining = girlPossession.RemainingPool;
        float maxTime = girlPossession.maxPossessionTimePool;

        if (timeBankText != null)
        {
            timeBankText.text = $"Possession Energy: {Mathf.CeilToInt(remaining)}s / {Mathf.CeilToInt(maxTime)}s";
        }

        if (timeBankSlider != null)
        {
            timeBankSlider.maxValue = maxTime;
            timeBankSlider.value = remaining;
        }

        if (remaining <= 0f && possessButton != null)
        {
            possessButton.interactable = false;
        }
    }

    public void OnPossessClicked()
    {
        if (_targetClientIds.Count == 0 || targetDropdown == null) return;

        int selectedIdx = targetDropdown.value;
        if (selectedIdx < 0 || selectedIdx >= _targetClientIds.Count) return;

        ulong targetClientId = _targetClientIds[selectedIdx];
        Debug.Log($"[GirlPossessionUI] Initiating possession on Client {targetClientId}");

        var girlPossession = GetLocalGirlPossession();
        if (girlPossession != null)
        {
            girlPossession.PossessTargetByClientId(targetClientId);
        }

        CloseUI();
    }

    public void OnReleaseClicked()
    {
        Debug.Log("[GirlPossessionUI] Releasing possession of target.");

        var girlPossession = GetLocalGirlPossession();
        if (girlPossession != null)
        {
            girlPossession.RequestRelease();
        }

        CloseUI();
    }

    /// <summary>
    /// Procedurally constructs a sleek dark-crimson Possession Panel if one was not
    /// manually designed in the Scene.
    /// </summary>
    private void BuildDefaultPossessionUI()
    {
        // Try to attach under DemonPanel, else HUDCanvas, else any Canvas
        Transform parentTransform = null;
        var demonPanel = GameObject.Find("DemonPanel");
        if (demonPanel != null) parentTransform = demonPanel.transform;

        if (parentTransform == null)
        {
            var hudRoot = GameObject.Find("HUDRoot");
            if (hudRoot != null) parentTransform = hudRoot.transform;
        }

        if (parentTransform == null)
        {
            var hudCanvas = GameObject.Find("HUDCanvas");
            if (hudCanvas != null) parentTransform = hudCanvas.transform;
        }

        if (parentTransform == null)
        {
            Canvas c = FindFirstObjectByType<Canvas>();
            if (c != null) parentTransform = c.transform;
        }

        if (parentTransform == null) return;

        // Create Panel Container
        GameObject panelObj = new GameObject("GirlPossessionPanel");
        panelObj.transform.SetParent(parentTransform, false);

        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 320f);
        rt.anchoredPosition = Vector2.zero;

        Image bg = panelObj.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.03f, 0.05f, 0.95f); // Deep dark sinister crimson-black

        // Header Title
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(panelObj.transform, false);
        RectTransform titleRt = titleObj.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -15f);
        titleRt.sizeDelta = new Vector2(400f, 40f);

        TextMeshProUGUI titleTxt = titleObj.AddComponent<TextMeshProUGUI>();
        titleTxt.text = "SPIRIT POSSESSION";
        titleTxt.alignment = TextAlignmentOptions.Center;
        titleTxt.fontSize = 24f;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.color = new Color(0.95f, 0.2f, 0.2f, 1f);

        // Subtitle
        GameObject subObj = new GameObject("SubtitleText");
        subObj.transform.SetParent(panelObj.transform, false);
        RectTransform subRt = subObj.AddComponent<RectTransform>();
        subRt.anchorMin = new Vector2(0f, 1f);
        subRt.anchorMax = new Vector2(1f, 1f);
        subRt.pivot = new Vector2(0.5f, 1f);
        subRt.anchoredPosition = new Vector2(0f, -50f);
        subRt.sizeDelta = new Vector2(400f, 25f);

        TextMeshProUGUI subTxt = subObj.AddComponent<TextMeshProUGUI>();
        subTxt.text = "Take control of a living investigator's body.";
        subTxt.alignment = TextAlignmentOptions.Center;
        subTxt.fontSize = 13f;
        subTxt.color = new Color(0.75f, 0.75f, 0.8f, 0.85f);

        // Time Bank Text
        GameObject bankObj = new GameObject("TimeBankText");
        bankObj.transform.SetParent(panelObj.transform, false);
        RectTransform bankRt = bankObj.AddComponent<RectTransform>();
        bankRt.anchorMin = new Vector2(0.5f, 1f);
        bankRt.anchorMax = new Vector2(0.5f, 1f);
        bankRt.pivot = new Vector2(0.5f, 1f);
        bankRt.anchoredPosition = new Vector2(0f, -85f);
        bankRt.sizeDelta = new Vector2(360f, 25f);

        timeBankText = bankObj.AddComponent<TextMeshProUGUI>();
        timeBankText.text = "Possession Energy: 300s / 300s";
        timeBankText.alignment = TextAlignmentOptions.Center;
        timeBankText.fontSize = 14f;
        timeBankText.color = new Color(0.85f, 0.3f, 0.3f, 1f);

        // Target Dropdown Container
        GameObject dropObj = new GameObject("TargetDropdown");
        dropObj.transform.SetParent(panelObj.transform, false);
        RectTransform dropRt = dropObj.AddComponent<RectTransform>();
        dropRt.anchorMin = new Vector2(0.5f, 0.5f);
        dropRt.anchorMax = new Vector2(0.5f, 0.5f);
        dropRt.pivot = new Vector2(0.5f, 0.5f);
        dropRt.anchoredPosition = new Vector2(0f, 10f);
        dropRt.sizeDelta = new Vector2(340f, 40f);

        Image dropBg = dropObj.AddComponent<Image>();
        dropBg.color = new Color(0.15f, 0.12f, 0.18f, 1f);

        targetDropdown = dropObj.AddComponent<TMP_Dropdown>();
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(dropObj.transform, false);
        RectTransform lblRt = labelObj.AddComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.sizeDelta = new Vector2(-20f, 0f);
        TextMeshProUGUI lblTxt = labelObj.AddComponent<TextMeshProUGUI>();
        lblTxt.alignment = TextAlignmentOptions.MidlineLeft;
        lblTxt.fontSize = 14f;
        lblTxt.color = Color.white;
        targetDropdown.captionText = lblTxt;

        // Action Button: POSSESS
        GameObject btnObj = new GameObject("PossessButton");
        btnObj.transform.SetParent(panelObj.transform, false);
        RectTransform btnRt = btnObj.AddComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0f);
        btnRt.anchorMax = new Vector2(0.5f, 0f);
        btnRt.pivot = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = new Vector2(0f, 50f);
        btnRt.sizeDelta = new Vector2(280f, 42f);

        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.75f, 0.15f, 0.15f, 1f);
        possessButton = btnObj.AddComponent<Button>();
        possessButton.onClick.AddListener(OnPossessClicked);

        GameObject btnTxtObj = new GameObject("Text");
        btnTxtObj.transform.SetParent(btnObj.transform, false);
        RectTransform btRt = btnTxtObj.AddComponent<RectTransform>();
        btRt.anchorMin = Vector2.zero;
        btRt.anchorMax = Vector2.one;
        TextMeshProUGUI btnTxt = btnTxtObj.AddComponent<TextMeshProUGUI>();
        btnTxt.text = "POSSESS TARGET";
        btnTxt.alignment = TextAlignmentOptions.Center;
        btnTxt.fontSize = 16f;
        btnTxt.fontStyle = FontStyles.Bold;
        btnTxt.color = Color.white;

        // Release Button (initially hidden)
        GameObject relObj = new GameObject("ReleaseButton");
        relObj.transform.SetParent(panelObj.transform, false);
        RectTransform relRt = relObj.AddComponent<RectTransform>();
        relRt.anchorMin = new Vector2(0.5f, 0f);
        relRt.anchorMax = new Vector2(0.5f, 0f);
        relRt.pivot = new Vector2(0.5f, 0f);
        relRt.anchoredPosition = new Vector2(0f, 50f);
        relRt.sizeDelta = new Vector2(280f, 42f);

        Image relImg = relObj.AddComponent<Image>();
        relImg.color = new Color(0.3f, 0.3f, 0.35f, 1f);
        releaseButton = relObj.AddComponent<Button>();
        releaseButton.onClick.AddListener(OnReleaseClicked);

        GameObject relTxtObj = new GameObject("Text");
        relTxtObj.transform.SetParent(relObj.transform, false);
        RectTransform rltRt = relTxtObj.AddComponent<RectTransform>();
        rltRt.anchorMin = Vector2.zero;
        rltRt.anchorMax = Vector2.one;
        TextMeshProUGUI relTxt = relTxtObj.AddComponent<TextMeshProUGUI>();
        relTxt.text = "RELEASE BODY";
        relTxt.alignment = TextAlignmentOptions.Center;
        relTxt.fontSize = 16f;
        relTxt.fontStyle = FontStyles.Bold;
        relTxt.color = Color.white;
        relObj.SetActive(false);

        // Close Button [X]
        GameObject closeObj = new GameObject("CloseButton");
        closeObj.transform.SetParent(panelObj.transform, false);
        RectTransform closeRt = closeObj.AddComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-10f, -10f);
        closeRt.sizeDelta = new Vector2(30f, 30f);

        Image closeImg = closeObj.AddComponent<Image>();
        closeImg.color = new Color(0.3f, 0.1f, 0.1f, 0.8f);
        closeButton = closeObj.AddComponent<Button>();
        closeButton.onClick.AddListener(CloseUI);

        GameObject closeTxtObj = new GameObject("Text");
        closeTxtObj.transform.SetParent(closeObj.transform, false);
        RectTransform ctRt = closeTxtObj.AddComponent<RectTransform>();
        ctRt.anchorMin = Vector2.zero;
        ctRt.anchorMax = Vector2.one;
        TextMeshProUGUI closeTxt = closeTxtObj.AddComponent<TextMeshProUGUI>();
        closeTxt.text = "X";
        closeTxt.alignment = TextAlignmentOptions.Center;
        closeTxt.fontSize = 14f;
        closeTxt.fontStyle = FontStyles.Bold;
        closeTxt.color = Color.white;

        mainPanel = panelObj;
        mainPanel.SetActive(false);
    }
}
