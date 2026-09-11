using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Dedicated Health & Hazard Warning HUD System.
/// Specializes in displaying high-priority physical and environmental alerts:
/// 1. Poisonous air / toxic atmosphere (Suffocation system)
/// 2. Critical low-health warnings (Vital signs failing)
/// 3. Revival and healing vial administrations (Vitals restored)
/// Features modern glassmorphism aesthetics, dynamic color badging, and smooth spring animations.
/// </summary>
public class NotificationManager : MonoBehaviour
{
    private static NotificationManager _instance;
    public static NotificationManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<NotificationManager>(FindObjectsInactive.Include);
                if (_instance != null && !_instance.gameObject.activeInHierarchy)
                {
                    _instance.gameObject.SetActive(true);
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("UI References")]
    [Tooltip("Text component to show the message body.")]
    public TMP_Text notificationText;

    [Tooltip("Optional badge or title text (e.g. '[HAZARD WARNING]'). Generated dynamically if null.")]
    public TMP_Text categoryBadgeText;

    [Tooltip("Panel or CanvasGroup containing the notification.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Background Image for glassmorphic styling.")]
    public Image panelBackground;

    [Tooltip("Accent indicator line.")]
    public Image accentBar;

    [Header("Audio (Optional)")]
    public AudioClip hazardSound;
    public AudioClip criticalHealthSound;
    public AudioClip healRestoredSound;
    public AudioClip notificationSound;

    private AudioSource _audioSource;
    private Coroutine _displayCoroutine;
    private RectTransform _rectTransform;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _rectTransform = GetComponent<RectTransform>();
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }


        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Displays a toxic atmosphere / poisonous air hazard banner.
    /// </summary>
    public void ShowHazardWarning(string message, float duration = 5.5f)
    {
        Color hazardColor = new Color(1f, 0.72f, 0.1f, 1f); // Toxic Amber
        string formatted = $"<color=#FFB800>| [HAZARD]</color> {message}";
        ShowStyledWarning("[ENVIRONMENT HAZARD]", formatted, hazardColor, duration, hazardSound ?? notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Displays an urgent critical health warning when vital signs are failing.
    /// </summary>
    public void ShowCriticalHealthWarning(float currentHp, float maxHp, float duration = 4.5f)
    {
        Color criticalColor = new Color(0.95f, 0.15f, 0.15f, 1f); // Crimson Red
        string formatted = $"<color=#FF2222>| [CRITICAL]</color> Vital signs failing! Health: {currentHp:F0}/{maxHp:F0} HP. Use healing vial or seek Field Medic!";
        ShowStyledWarning("[CRITICAL: LOW HEALTH]", formatted, criticalColor, duration, criticalHealthSound ?? notificationSound, isPulsing: true);
    }

    /// <summary>
    /// Displays a restorative message when a healing vial or revival is administered.
    /// </summary>
    public void ShowHealthRestored(string message, float healAmount = 50f, float duration = 4f)
    {
        Color healColor = new Color(0.15f, 0.92f, 0.45f, 1f); // Emerald Green
        string baseMsg = !string.IsNullOrEmpty(message) ? message : $"Healing vial administered (+{healAmount:F0} HP)! Vitals stabilized.";
        string formatted = $"<color=#00E676>| [RESTORED]</color> {baseMsg}";
        ShowStyledWarning("[VITAL SIGNS RESTORED]", formatted, healColor, duration, healRestoredSound ?? notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Standard notification banner (backwards compatibility).
    /// </summary>
    public void ShowNotification(string message, float duration = 4f)
    {
        string formatted = $"<color=#00E5FF>|</color> {message}";
        ShowStyledWarning("[STATUS NOTICE]", formatted, new Color(0.3f, 0.8f, 1f, 1f), duration, notificationSound, isPulsing: false);
    }

    public void ShowStyledWarning(string badge, string message, Color accentColor, float duration, AudioClip sound = null, bool isPulsing = false)
    {
        if (_displayCoroutine != null)
        {
            StopCoroutine(_displayCoroutine);
        }
        _displayCoroutine = StartCoroutine(DisplayRoutine(badge, message, accentColor, duration, sound, isPulsing));
    }

    private IEnumerator DisplayRoutine(string badge, string message, Color accentColor, float duration, AudioClip sound, bool isPulsing)
    {
        if (categoryBadgeText != null)
        {
            categoryBadgeText.text = badge;
            categoryBadgeText.color = accentColor;
            categoryBadgeText.gameObject.SetActive(true);
        }

        if (notificationText != null)
        {
            notificationText.text = message;
        }

        if (accentBar != null)
        {
            accentBar.color = accentColor;
        }

        if (_audioSource != null && sound != null)
        {
            _audioSource.PlayOneShot(sound);
        }

        // Fade In using CanvasGroup
        float elapsed = 0f;
        float fadeTime = 0.25f;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);
            if (canvasGroup != null) canvasGroup.alpha = t;
            yield return null;
        }

        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // Hold duration (with subtle background tint pulse if configured)
        float holdElapsed = 0f;
        Color originalBgColor = panelBackground != null ? panelBackground.color : Color.white;

        while (holdElapsed < duration)
        {
            holdElapsed += Time.deltaTime;
            if (isPulsing && panelBackground != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(holdElapsed * 8f);
                panelBackground.color = Color.Lerp(originalBgColor, new Color(0.4f, 0.05f, 0.05f, originalBgColor.a), pulse);
            }
            yield return null;
        }

        if (panelBackground != null && isPulsing)
        {
            panelBackground.color = originalBgColor;
        }

        // Fade Out
        elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);
            if (canvasGroup != null) canvasGroup.alpha = 1f - t;
            yield return null;
        }

        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (categoryBadgeText != null) categoryBadgeText.gameObject.SetActive(false);
        _displayCoroutine = null;
    }
}
