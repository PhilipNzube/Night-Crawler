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

        // Hide initially
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
    /// Shows an onscreen notification banner when another player has died.
    /// </summary>
    public void ShowAllyDeathNotification(string victimName)
    {
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
    }
}
