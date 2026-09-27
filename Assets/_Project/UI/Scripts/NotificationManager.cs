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
    [Tooltip("Root container GameObject of the header (disabled if notification has no header).")]
    public GameObject headerContainer;

    [Tooltip("TMP text component for the header title.")]
    public TMP_Text headerText;

    [Tooltip("Text component to show the message body.")]
    public TMP_Text notificationText;

    [Tooltip("Optional badge or title text (e.g. 'HAZARD WARNING'). Generated dynamically if null.")]
    public TMP_Text categoryBadgeText;

    [Tooltip("Panel or CanvasGroup containing the notification.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Background Image for glassmorphic styling.")]
    public Image panelBackground;

    [Tooltip("Accent indicator line.")]
    public Image accentBar;

    [Header("Heat UI Quest Structure (Optional)")]
    [Tooltip("Michsky Heat UI QuestItem attached to NotificationTextGO for official animations.")]
    public Michsky.UI.Heat.QuestItem questItem;

    [Tooltip("Target RectTransform that animates. Automatically resolves to NotificationTextGO if null.")]
    public RectTransform notificationRect;

    [Header("Audio (Optional)")]
    public AudioClip hazardSound;
    public AudioClip criticalHealthSound;
    public AudioClip healRestoredSound;
    public AudioClip notificationSound;

    private struct NotificationData
    {
        public string header;
        public string message;
        public Color accentColor;
        public float duration;
        public AudioClip sound;
        public bool isPulsing;
    }

    private readonly Queue<NotificationData> _queue = new Queue<NotificationData>();
    private bool _isDisplaying = false;
    private bool _interruptCurrent = false;
    private Vector2 _initialAnchoredPos = new Vector2(0, 30);
    private Vector3 _initialScale = Vector3.one;

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

        if (questItem == null)
        {
            questItem = GetComponentInChildren<Michsky.UI.Heat.QuestItem>(true);
        }

        if (notificationRect == null)
        {
            if (questItem != null) notificationRect = questItem.GetComponent<RectTransform>();
            else notificationRect = transform.Find("NotificationTextGO")?.GetComponent<RectTransform>();
        }

        if (notificationRect != null)
        {
            _initialAnchoredPos = notificationRect.anchoredPosition;
            _initialScale = notificationRect.localScale;
            if (_initialScale == Vector3.zero) _initialScale = Vector3.one;
        }

        if (panelBackground == null) panelBackground = transform.Find("NotificationTextGO/Background")?.GetComponent<Image>();
        if (accentBar == null) accentBar = transform.Find("NotificationTextGO/Indicator")?.GetComponent<Image>();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (questItem != null)
        {
            questItem.defaultState = Michsky.UI.Heat.QuestItem.DefaultState.Minimized;
            questItem.gameObject.SetActive(false);
        }
        else if (notificationRect != null)
        {
            notificationRect.gameObject.SetActive(false);
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
        ShowStyledWarning("HAZARD", message, hazardColor, duration, hazardSound ?? notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Displays an urgent critical health warning when vital signs are failing.
    /// </summary>
    public void ShowCriticalHealthWarning(float currentHp, float maxHp, float duration = 4.5f)
    {
        Color criticalColor = new Color(0.95f, 0.15f, 0.15f, 1f); // Crimson Red
        string formatted = $"Vital signs failing! Health: {currentHp:F0}/{maxHp:F0} HP. Use healing vial or seek Field Medic!";
        ShowStyledWarning("CRITICAL: LOW HEALTH", formatted, criticalColor, duration, criticalHealthSound ?? notificationSound, isPulsing: true);
    }

    /// <summary>
    /// Displays a restorative message when a healing vial or revival is administered.
    /// </summary>
    public void ShowHealthRestored(string message, float healAmount = 50f, float duration = 4f)
    {
        Color healColor = new Color(0.15f, 0.92f, 0.45f, 1f); // Emerald Green
        string baseMsg = !string.IsNullOrEmpty(message) ? message : $"Healing vial administered (+{healAmount:F0} HP)! Vitals stabilized.";
        ShowStyledWarning("VITAL SIGNS RESTORED", baseMsg, healColor, duration, healRestoredSound ?? notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Standard notification banner without a header (disables header container).
    /// </summary>
    public void ShowNotification(string message, float duration = 4f)
    {
        ShowStyledWarning(null, message, new Color(0.3f, 0.8f, 1f, 1f), duration, notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Enqueues and displays a styled warning. If a notification is currently displayed,
    /// the current one animates out first, and then the new notification animates in.
    /// </summary>
    public void ShowStyledWarning(string header, string message, Color accentColor, float duration, AudioClip sound = null, bool isPulsing = false)
    {
        var data = new NotificationData
        {
            header = header,
            message = message,
            accentColor = accentColor,
            duration = duration,
            sound = sound,
            isPulsing = isPulsing
        };

        _queue.Enqueue(data);

        if (_isDisplaying)
        {
            // Interrupt current hold so it begins animating out immediately to show the next one
            _interruptCurrent = true;
        }
        else
        {
            if (_displayCoroutine != null) StopCoroutine(_displayCoroutine);
            _displayCoroutine = StartCoroutine(ProcessQueueRoutine());
        }
    }

    private IEnumerator ProcessQueueRoutine()
    {
        _isDisplaying = true;

        while (_queue.Count > 0)
        {
            NotificationData current = _queue.Dequeue();
            _interruptCurrent = false;

            ApplyNotificationData(current);

            // 1. ANIMATE IN (Smooth Ease-Out Spring)
            yield return AnimateInRoutine();

            // 2. HOLD (cuts short if a new notification enters the queue)
            float holdElapsed = 0f;
            float targetDuration = _queue.Count > 0 ? Mathf.Min(current.duration, 1.4f) : current.duration;
            Color originalBgColor = panelBackground != null ? panelBackground.color : Color.white;

            while (holdElapsed < targetDuration && !_interruptCurrent)
            {
                holdElapsed += Time.deltaTime;
                if (current.isPulsing && panelBackground != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(holdElapsed * 8f);
                    panelBackground.color = Color.Lerp(originalBgColor, new Color(0.4f, 0.05f, 0.05f, originalBgColor.a), pulse);
                }
                yield return null;
            }

            if (panelBackground != null && current.isPulsing)
            {
                panelBackground.color = originalBgColor;
            }

            // 3. ANIMATE OUT (Smooth Ease-In Tuck)
            yield return AnimateOutRoutine();

            // Pacing pause between consecutive notifications
            if (_queue.Count > 0)
            {
                yield return new WaitForSeconds(0.1f);
            }
        }

        _isDisplaying = false;
        _displayCoroutine = null;
    }

    private void ApplyNotificationData(NotificationData data)
    {
        bool hasHeader = !string.IsNullOrWhiteSpace(data.header);

        if (headerContainer != null)
        {
            headerContainer.SetActive(hasHeader);
        }

        if (headerText != null)
        {
            headerText.text = hasHeader ? data.header : "";
        }

        if (notificationText != null)
        {
            notificationText.text = data.message;
        }

        if (categoryBadgeText != null)
        {
            categoryBadgeText.text = data.header;
            categoryBadgeText.color = data.accentColor;
            categoryBadgeText.gameObject.SetActive(hasHeader);
        }

        if (accentBar != null)
        {
            accentBar.color = data.accentColor;
        }

        if (_audioSource != null && data.sound != null)
        {
            _audioSource.PlayOneShot(data.sound);
        }
    }

    private IEnumerator AnimateInRoutine()
    {
        if (notificationRect != null)
        {
            notificationRect.gameObject.SetActive(true);
        }

        if (questItem != null)
        {
            questItem.gameObject.SetActive(true);
            questItem.minimizeAfter = 0;
            questItem.afterMinimize = Michsky.UI.Heat.QuestItem.AfterMinimize.Disable;
            questItem.ExpandQuest();
        }

        float duration = 0.35f;
        float elapsed = 0f;
        Vector2 startPos = _initialAnchoredPos + new Vector2(0f, -25f);
        Vector2 endPos = _initialAnchoredPos;
        Vector3 startScale = _initialScale * 0.88f;
        Vector3 endScale = _initialScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Ease-out back curve (spring-like pop)
            float ease = 1f + 2.70158f * Mathf.Pow(t - 1f, 3) + 1.70158f * Mathf.Pow(t - 1f, 2);

            if (notificationRect != null)
            {
                notificationRect.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, ease);
                notificationRect.localScale = Vector3.LerpUnclamped(startScale, endScale, ease);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Clamp01(t * 1.5f);
            }

            yield return null;
        }

        if (notificationRect != null)
        {
            notificationRect.anchoredPosition = endPos;
            notificationRect.localScale = endScale;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
    }

    private IEnumerator AnimateOutRoutine()
    {
        if (questItem != null)
        {
            questItem.MinimizeQuest();
        }

        float duration = 0.28f;
        float elapsed = 0f;
        Vector2 startPos = _initialAnchoredPos;
        Vector2 endPos = _initialAnchoredPos + new Vector2(0f, -20f);
        Vector3 startScale = _initialScale;
        Vector3 endScale = _initialScale * 0.88f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Smooth ease-in quad
            float ease = t * t;

            if (notificationRect != null)
            {
                notificationRect.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
                notificationRect.localScale = Vector3.Lerp(startScale, endScale, ease);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f - ease;
            }

            yield return null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        if (notificationRect != null)
        {
            notificationRect.anchoredPosition = _initialAnchoredPos;
            notificationRect.localScale = _initialScale;
            if (questItem == null)
            {
                notificationRect.gameObject.SetActive(false);
            }
        }

        if (categoryBadgeText != null)
        {
            categoryBadgeText.gameObject.SetActive(false);
        }
    }
}
