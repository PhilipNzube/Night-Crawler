using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Manages the death overlay screen when a player dies.
/// Displays a cinematic "YOU DIED" screen for the local victim, and provides
/// ally death notification support.
/// Includes dynamic runtime UI generation so it works out-of-the-box even before
/// the designer manually wires elements in the Inspector.
/// </summary>
public class DeathUI : MonoBehaviour
{
    public static DeathUI Instance { get; private set; }

    [Header("Death Screen UI (Local Victim)")]
    [Tooltip("Root GameObject or panel for the local death screen.")]
    public GameObject deathPanel;

    [Tooltip("CanvasGroup controlling death screen alpha fade.")]
    public CanvasGroup deathCanvasGroup;

    [Tooltip("Title text element (e.g. 'YOU DIED').")]
    public TMP_Text titleText;

    [Tooltip("Subtitle text element explaining corpse lootability / match status.")]
    public TMP_Text subtitleText;

    [Header("Optional Ally Death Banner (Other Players)")]
    [Tooltip("Banner displayed when a teammate dies.")]
    public GameObject allyDeathBanner;
    public TMP_Text allyDeathText;

    [Header("Ally Alert Feed (Bottom-Right Scrollable)")]
    [Tooltip("ScrollRect container for the bottom-right ally alert feed.")]
    public ScrollRect allyAlertScrollRect;

    [Tooltip("Content transform inside the ScrollRect where alert cards are appended.")]
    public Transform allyAlertContent;

    [Tooltip("Optional custom prefab for alert cards. If null, a sleek card is generated procedurally.")]
    public GameObject alertItemPrefab;

    [Tooltip("Maximum number of alerts kept in history before oldest is culled.")]
    public int maxAlerts = 8;

    [Header("Animation Settings")]
    public float fadeInDuration = 1.2f;

    private Coroutine _fadeCoroutine;
    private Coroutine _allyBannerCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-generate visual elements if nothing is hooked up in Inspector
        if (deathPanel == null && deathCanvasGroup == null)
        {
            BuildDefaultDeathScreenUI();
        }

        // Hide death screen initially
        if (deathCanvasGroup != null)
        {
            deathCanvasGroup.alpha = 0f;
            deathCanvasGroup.interactable = false;
            deathCanvasGroup.blocksRaycasts = false;
        }

        if (deathPanel != null)
        {
            deathPanel.SetActive(false);
        }

        if (allyDeathBanner != null)
        {
            allyDeathBanner.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Displays the death overlay for the local player who just died.
    /// </summary>
    public void ShowDeathScreen(string title = "YOU DIED", string subtitle = "Your soul has fallen. Allies can still loot your body.")
    {
        if (deathPanel != null) deathPanel.SetActive(true);
        if (titleText != null) titleText.text = title;
        if (subtitleText != null) subtitleText.text = subtitle;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeInDeathScreenRoutine());
    }

    private IEnumerator FadeInDeathScreenRoutine()
    {
        if (deathCanvasGroup == null) yield break;

        deathCanvasGroup.gameObject.SetActive(true);
        deathCanvasGroup.blocksRaycasts = false; // Don't block camera orbit

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            deathCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }
        deathCanvasGroup.alpha = 1f;
    }

    /// <summary>
    /// Shows an onscreen notification banner and adds an entry into the bottom-right scrollable feed.
    /// </summary>
    public void ShowAllyDeathNotification(string victimName)
    {
        // 1. Add into bottom-right scrollable alert feed
        AddAllyAlertEntry($"☠ {victimName} has fallen", new Color(0.9f, 0.2f, 0.2f, 1f));

        // 2. Banner / toast
        if (allyDeathBanner != null && allyDeathText != null)
        {
            if (_allyBannerCoroutine != null) StopCoroutine(_allyBannerCoroutine);
            _allyBannerCoroutine = StartCoroutine(AllyBannerRoutine(victimName));
        }
        else if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"☠ {victimName} has died.", 4.5f);
        }
    }

    /// <summary>
    /// Appends a new sleek alert item to the bottom-right scrollable feed.
    /// </summary>
    public void AddAllyAlertEntry(string message, Color? accentColor = null)
    {
        Color accent = accentColor ?? new Color(0.9f, 0.2f, 0.2f, 1f);

        if (allyAlertContent == null)
        {
            BuildDefaultAllyAlertFeed();
        }

        if (allyAlertContent == null) return;

        // Cull oldest if max reached
        while (allyAlertContent.childCount >= maxAlerts && allyAlertContent.childCount > 0)
        {
            Destroy(allyAlertContent.GetChild(0).gameObject);
        }

        GameObject entryObj;
        if (alertItemPrefab != null)
        {
            entryObj = Instantiate(alertItemPrefab, allyAlertContent);
            var txt = entryObj.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = message;
        }
        else
        {
            entryObj = CreateAlertCardVisual(allyAlertContent, message, accent);
        }

        StartCoroutine(ScrollToBottomRoutine());
    }

    private GameObject CreateAlertCardVisual(Transform parent, string message, Color accent)
    {
        GameObject card = new GameObject("AlertEntry");
        card.transform.SetParent(parent, false);

        RectTransform rt = card.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 38f);

        Image bg = card.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.12f, 0.85f); // Dark translucent card

        HorizontalLayoutGroup hlg = card.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        ContentSizeFitter csf = card.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Left Accent Bar / Indicator
        GameObject bar = new GameObject("AccentBar");
        bar.transform.SetParent(card.transform, false);
        RectTransform barRt = bar.AddComponent<RectTransform>();
        barRt.sizeDelta = new Vector2(4f, 26f);
        Image barImg = bar.AddComponent<Image>();
        barImg.color = accent;

        // Text element
        GameObject txtObj = new GameObject("AlertText");
        txtObj.transform.SetParent(card.transform, false);
        RectTransform txtRt = txtObj.AddComponent<RectTransform>();
        txtRt.sizeDelta = new Vector2(250f, 30f);

        TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(0.95f, 0.95f, 0.95f, 0.95f);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = true;

        // Fade in animation
        CanvasGroup cg = card.AddComponent<CanvasGroup>();
        StartCoroutine(FadeInCardRoutine(cg));

        return card;
    }

    private IEnumerator FadeInCardRoutine(CanvasGroup cg)
    {
        if (cg == null) yield break;
        cg.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Clamp01(elapsed / 0.25f);
            yield return null;
        }
        cg.alpha = 1f;
    }

    private IEnumerator ScrollToBottomRoutine()
    {
        yield return null; // Wait 1 frame for Layout rebuild
        if (allyAlertScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            allyAlertScrollRect.verticalNormalizedPosition = 0f; // Scroll to newest entry
        }
    }

    private IEnumerator AllyBannerRoutine(string victimName)
    {
        allyDeathText.text = $"☠ {victimName} has fallen";
        allyDeathBanner.SetActive(true);

        yield return new WaitForSeconds(4f);

        allyDeathBanner.SetActive(false);
        _allyBannerCoroutine = null;
    }

    public void HideDeathScreen()
    {
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        if (deathCanvasGroup != null)
        {
            deathCanvasGroup.alpha = 0f;
            deathCanvasGroup.blocksRaycasts = false;
        }
        if (deathPanel != null) deathPanel.SetActive(false);
    }

    /// <summary>
    /// Creates a default dark, cinematic "YOU DIED" UI hierarchy at runtime
    /// if the user has not wired up custom UI in the Inspector yet.
    /// </summary>
    private void BuildDefaultDeathScreenUI()
    {
        // Find or create Canvas
        Canvas targetCanvas = GetComponentInParent<Canvas>();
        if (targetCanvas == null)
        {
            targetCanvas = FindFirstObjectByType<Canvas>();
        }

        if (targetCanvas == null)
        {
            GameObject canvasObj = new GameObject("DeathUICanvas");
            targetCanvas = canvasObj.AddComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 950;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // Create Panel Container
        GameObject panelObj = new GameObject("DeathUIPanel");
        panelObj.transform.SetParent(targetCanvas.transform, false);

        RectTransform panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        deathCanvasGroup = panelObj.AddComponent<CanvasGroup>();
        deathCanvasGroup.alpha = 0f;
        deathCanvasGroup.blocksRaycasts = false;

        // Dark Blood-Red Vignette Overlay Background
        Image bgImage = panelObj.AddComponent<Image>();
        bgImage.color = new Color(0.12f, 0.01f, 0.01f, 0.65f); // Deep atmospheric red-black

        // Title Text
        GameObject titleObj = new GameObject("DeathTitleText");
        titleObj.transform.SetParent(panelObj.transform, false);
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 0.5f);
        titleRect.anchorMax = new Vector2(1f, 0.5f);
        titleRect.sizeDelta = new Vector2(0f, 100f);
        titleRect.anchoredPosition = new Vector2(0f, 40f);

        titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "YOU DIED";
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.fontSize = 68f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = new Color(0.9f, 0.12f, 0.12f, 1f); // Menacing Crimson Red

        // Subtitle Text
        GameObject subObj = new GameObject("DeathSubtitleText");
        subObj.transform.SetParent(panelObj.transform, false);
        RectTransform subRect = subObj.AddComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0f, 0.5f);
        subRect.anchorMax = new Vector2(1f, 0.5f);
        subRect.sizeDelta = new Vector2(0f, 60f);
        subRect.anchoredPosition = new Vector2(0f, -30f);

        subtitleText = subObj.AddComponent<TextMeshProUGUI>();
        subtitleText.text = "Your soul has fallen. Allies can still loot your body.";
        subtitleText.alignment = TextAlignmentOptions.Center;
        subtitleText.fontSize = 24f;
        subtitleText.fontStyle = FontStyles.Normal;
        subtitleText.color = new Color(0.85f, 0.85f, 0.9f, 0.9f);

        deathPanel = panelObj;
        deathPanel.SetActive(false);

        // Also build default bottom-right alert feed
        BuildDefaultAllyAlertFeed(targetCanvas);
    }

    /// <summary>
    /// Constructs a clean, aesthetic bottom-right ScrollRect feed on the Canvas
    /// if the user has not wired custom references in the Inspector.
    /// </summary>
    public void BuildDefaultAllyAlertFeed(Canvas targetCanvas = null)
    {
        if (allyAlertContent != null && allyAlertScrollRect != null) return;

        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null) targetCanvas = FindFirstObjectByType<Canvas>();
        }
        if (targetCanvas == null) return;

        // 1. Root Scroll Container (Anchored Bottom-Right)
        GameObject feedRoot = new GameObject("AllyAlertFeed_BottomRight");
        feedRoot.transform.SetParent(targetCanvas.transform, false);

        RectTransform feedRt = feedRoot.AddComponent<RectTransform>();
        feedRt.anchorMin = new Vector2(1f, 0f);
        feedRt.anchorMax = new Vector2(1f, 0f);
        feedRt.pivot = new Vector2(1f, 0f);
        feedRt.anchoredPosition = new Vector2(-20f, 20f);
        feedRt.sizeDelta = new Vector2(320f, 180f);

        ScrollRect scroll = feedRoot.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        // 2. Viewport with RectMask2D
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(feedRoot.transform, false);

        RectTransform viewRt = viewport.AddComponent<RectTransform>();
        viewRt.anchorMin = Vector2.zero;
        viewRt.anchorMax = Vector2.one;
        viewRt.sizeDelta = Vector2.zero;
        viewRt.pivot = new Vector2(0f, 1f);
        viewport.AddComponent<RectMask2D>();

        // 3. Content Panel with VerticalLayoutGroup and ContentSizeFitter
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);

        RectTransform contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 0f);
        contentRt.anchorMax = new Vector2(1f, 0f);
        contentRt.pivot = new Vector2(0f, 0f);
        contentRt.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.LowerLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewRt;
        scroll.content = contentRt;

        allyAlertScrollRect = scroll;
        allyAlertContent = content.transform;
    }
}
