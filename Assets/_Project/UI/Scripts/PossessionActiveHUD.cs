using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Renders the active possession HUD overlay on the Girl player's display.
/// Shows target name, remaining time, and a prominent on-screen "RELEASE BODY [E]" button.
/// Appears only while actively possessing an investigator.
/// </summary>
public class PossessionActiveHUD : MonoBehaviour
{
    private static PossessionActiveHUD _instance;
    public static PossessionActiveHUD Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<PossessionActiveHUD>(FindObjectsInactive.Include);
                if (_instance != null && !_instance.gameObject.activeInHierarchy)
                {
                    _instance.gameObject.SetActive(true);
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("UI Elements")]
    public GameObject hudContainer;
    public TMP_Text victimNameText;
    public TMP_Text timeRemainingText;
    public Button releaseButton;

    private GirlPossession _activeGirlPossession;
    private bool _isActive = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (releaseButton != null)
        {
            releaseButton.onClick.AddListener(OnReleaseClicked);
        }

        if (hudContainer == null)
        {
            BuildDefaultActiveHUD();
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!_isActive || _activeGirlPossession == null) return;

        // Update remaining time
        if (timeRemainingText != null)
        {
            float rem = _activeGirlPossession.RemainingPool;
            timeRemainingText.text = $"{Mathf.CeilToInt(rem)}s left";
        }

        // Hotkey [E] releases possession directly
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            OnReleaseClicked();
        }
    }

    public void Show(string victimName, GirlPossession girlPossession)
    {
        _activeGirlPossession = girlPossession;
        _isActive = true;

        if (victimNameText != null)
        {
            victimNameText.text = $"☠ POSSESSING: {victimName}";
        }

        if (hudContainer != null)
        {
            hudContainer.SetActive(true);
        }
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        _isActive = false;
        _activeGirlPossession = null;

        if (hudContainer != null)
        {
            hudContainer.SetActive(false);
        }
    }

    public void OnReleaseClicked()
    {
        if (_activeGirlPossession != null)
        {
            _activeGirlPossession.RequestRelease();
        }
        Hide();
    }

    /// <summary>
    /// Procedurally constructs a sleek top-center on-screen release widget
    /// on the HUDCanvas if not manually placed in the Inspector.
    /// </summary>
    private void BuildDefaultActiveHUD()
    {
        Transform parentTransform = null;
        var hudRoot = GameObject.Find("HUDRoot");
        if (hudRoot != null) parentTransform = hudRoot.transform;

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

        // Container (Top Center)
        GameObject container = new GameObject("PossessionActiveHUD_TopBar");
        container.transform.SetParent(parentTransform, false);

        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -25f);
        rt.sizeDelta = new Vector2(460f, 65f);

        Image bg = container.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.02f, 0.04f, 0.88f); // Dark translucent crimson

        HorizontalLayoutGroup hlg = container.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(15, 15, 10, 10);
        hlg.spacing = 15f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // Target Name Text
        GameObject nameObj = new GameObject("VictimName");
        nameObj.transform.SetParent(container.transform, false);
        RectTransform nRt = nameObj.AddComponent<RectTransform>();
        nRt.sizeDelta = new Vector2(180f, 40f);

        victimNameText = nameObj.AddComponent<TextMeshProUGUI>();
        victimNameText.text = "☠ POSSESSING: Investigator";
        victimNameText.fontSize = 14f;
        victimNameText.fontStyle = FontStyles.Bold;
        victimNameText.color = new Color(0.95f, 0.3f, 0.3f, 1f);
        victimNameText.alignment = TextAlignmentOptions.MidlineLeft;

        // Timer Text
        GameObject timeObj = new GameObject("TimeText");
        timeObj.transform.SetParent(container.transform, false);
        RectTransform tRt = timeObj.AddComponent<RectTransform>();
        tRt.sizeDelta = new Vector2(70f, 40f);

        timeRemainingText = timeObj.AddComponent<TextMeshProUGUI>();
        timeRemainingText.text = "300s left";
        timeRemainingText.fontSize = 13f;
        timeRemainingText.color = new Color(0.85f, 0.85f, 0.9f, 0.9f);
        timeRemainingText.alignment = TextAlignmentOptions.Center;

        // Release Button [E]
        GameObject btnObj = new GameObject("ReleaseButton");
        btnObj.transform.SetParent(container.transform, false);
        RectTransform bRt = btnObj.AddComponent<RectTransform>();
        bRt.sizeDelta = new Vector2(160f, 42f);

        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.8f, 0.15f, 0.15f, 1f);

        releaseButton = btnObj.AddComponent<Button>();
        releaseButton.onClick.AddListener(OnReleaseClicked);

        GameObject btnTxtObj = new GameObject("Text");
        btnTxtObj.transform.SetParent(btnObj.transform, false);
        RectTransform btRt = btnTxtObj.AddComponent<RectTransform>();
        btRt.anchorMin = Vector2.zero;
        btRt.anchorMax = Vector2.one;

        TextMeshProUGUI btnTxt = btnTxtObj.AddComponent<TextMeshProUGUI>();
        btnTxt.text = "RELEASE [E]";
        btnTxt.alignment = TextAlignmentOptions.Center;
        btnTxt.fontSize = 14f;
        btnTxt.fontStyle = FontStyles.Bold;
        btnTxt.color = Color.white;

        hudContainer = container;
        hudContainer.SetActive(false);
    }
}
