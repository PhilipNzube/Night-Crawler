using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: High-priority Environmental & Combat Warning HUD System.
/// Specializes in displaying critical alerts:
/// 1. Poisonous air / toxic atmosphere (Suffocation)
/// 2. Critical low-health warnings (Vital signs failing)
/// 3. Revival and healing vial administrations (Vitals restored)
/// 4. Risen dead monster alerts & Dark Pact broadcasts
/// Fully drives Michsky Heat UI QuestItem with glassmorphism badging and smooth animations.
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
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Core UI Wiring")]
    [Tooltip("CanvasGroup on NotificationManager driving visibility and opacity.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Michsky Heat UI QuestItem component on NotificationTextGO.")]
    public Michsky.UI.Heat.QuestItem questItem;

    [Tooltip("Text component to show the message body (drag NotificationTextGO/Content/Text here).")]
    public TMP_Text notificationText;

    [Header("Header Elements")]
    [Tooltip("Root container GameObject of the header (enabled when an alert has a header, hidden for plain notifications).")]
    public GameObject headerContainer;

    [Tooltip("TMP text component for the header title (drag Header/Text or Header/Header here).")]
    public TMP_Text headerText;

    [Header("Styling Elements (Optional)")]
    [Tooltip("Background Image on NotificationTextGO/Background for pulse animations.")]
    public Image panelBackground;

    [Tooltip("Accent indicator line on NotificationTextGO/Indicator.")]
    public Image accentBar;

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
    private string _currentActiveMessage = null;
    private string _currentActiveHeader = null;
    private float _currentHoldElapsed = 0f;

    private AudioSource _audioSource;
    private Coroutine _displayCoroutine;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (questItem != null)
        {
            questItem.defaultState = Michsky.UI.Heat.QuestItem.DefaultState.Minimized;
            questItem.minimizeAfter = 0; // NotificationManager handles display duration
            questItem.afterMinimize = Michsky.UI.Heat.QuestItem.AfterMinimize.Disable;

            // Safeguard: Ensure questTextObj on QuestItem is assigned so Heat UI won't throw NRE
            var field = typeof(Michsky.UI.Heat.QuestItem).GetField("questTextObj", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null && field.GetValue(questItem) == null && notificationText != null)
            {
                field.SetValue(questItem, notificationText as TMPro.TextMeshProUGUI);
            }

            questItem.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
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
    /// Standard notification banner. If message contains 'Header: Text' or '[Header]: Text',
    /// it automatically parses the title into the dedicated header display and formats the message body.
    /// </summary>
    public void ShowNotification(string message, float duration = 4f)
    {
        string header = null;
        string body = message;

        if (!string.IsNullOrEmpty(message))
        {
            int colonIdx = message.IndexOf(':');
            if (colonIdx > 0 && colonIdx < 40)
            {
                string candidateHeader = message.Substring(0, colonIdx).Trim();
                string candidateBody = message.Substring(colonIdx + 1).Trim();
                if (candidateHeader.StartsWith("[") && candidateHeader.EndsWith("]"))
                {
                    candidateHeader = candidateHeader.Substring(1, candidateHeader.Length - 2).Trim();
                }
                header = candidateHeader;
                body = candidateBody;
            }
        }

        ShowStyledWarning(header, body, new Color(0.3f, 0.8f, 1f, 1f), duration, notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Standard notification with a header title. Keep the title out of the body text —
    /// the header row displays it.
    /// </summary>
    public void ShowNotification(string header, string message, float duration = 4f)
    {
        ShowStyledWarning(header, message, new Color(0.3f, 0.8f, 1f, 1f), duration, notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Standard notification with a header title and custom accent colour.
    /// </summary>
    public void ShowNotification(string header, string message, Color accentColor, float duration = 4f)
    {
        ShowStyledWarning(header, message, accentColor, duration, notificationSound, isPulsing: false);
    }

    /// <summary>
    /// Enqueues and displays a styled warning. If it is the exact same notification as the one
    /// currently displaying or already in queue, it will NOT trigger a re-animation or duplicate queue buildup.
    /// Only after the notification has animated out can the same notification animate in again.
    /// </summary>
    public void ShowStyledWarning(string header, string message, Color accentColor, float duration, AudioClip sound = null, bool isPulsing = false)
    {
        // 1. If identical notification is already waiting in queue, ignore it
        foreach (var item in _queue)
        {
            if (string.Equals(item.message, message, System.StringComparison.Ordinal) &&
                string.Equals(item.header, header, System.StringComparison.Ordinal))
            {
                return;
            }
        }

        // 2. If identical notification is currently displaying/animating, refresh hold timer and do not re-animate
        if (_isDisplaying &&
            string.Equals(_currentActiveMessage, message, System.StringComparison.Ordinal) &&
            string.Equals(_currentActiveHeader, header, System.StringComparison.Ordinal))
        {
            _currentHoldElapsed = 0f;
            return;
        }

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
            // Interrupt current hold only if a DIFFERENT notification has arrived
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
            _currentActiveMessage = current.message;
            _currentActiveHeader = current.header;
            _currentHoldElapsed = 0f;

            ApplyNotificationData(current);

            // 1. ANIMATE IN
            yield return AnimateInRoutine();

            // 2. HOLD (cuts short if a different notification enters the queue)
            float targetDuration = _queue.Count > 0 ? Mathf.Min(current.duration, 1.4f) : current.duration;
            Color originalBgColor = panelBackground != null ? panelBackground.color : Color.white;

            while (_currentHoldElapsed < targetDuration && !_interruptCurrent)
            {
                _currentHoldElapsed += Time.deltaTime;
                if (current.isPulsing && panelBackground != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(_currentHoldElapsed * 8f);
                    panelBackground.color = Color.Lerp(originalBgColor, new Color(0.4f, 0.05f, 0.05f, originalBgColor.a), pulse);
                }
                yield return null;
            }

            if (panelBackground != null && current.isPulsing)
            {
                panelBackground.color = originalBgColor;
            }

            // 3. ANIMATE OUT (Notification remains active until it has fully finished animating out)
            yield return AnimateOutRoutine();

            // Clear active tracking only after animation out has fully concluded
            _currentActiveMessage = null;
            _currentActiveHeader = null;

            // Pacing pause between consecutive notifications
            if (_queue.Count > 0)
            {
                yield return new WaitForSeconds(0.1f);
            }
        }

        _isDisplaying = false;
        _displayCoroutine = null;
        _currentActiveMessage = null;
        _currentActiveHeader = null;
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
            headerText.text = hasHeader ? data.header : string.Empty;
            headerText.color = data.accentColor;
        }

        if (notificationText != null)
        {
            notificationText.text = data.message;
        }

        if (questItem != null)
        {
            questItem.minimizeAfter = 0;
            questItem.questText = data.message;
            questItem.UpdateUI();
        }

        if (accentBar != null)
        {
            accentBar.color = data.accentColor;
        }

        if (_audioSource != null && data.sound != null)
        {
            _audioSource.PlayOneShot(data.sound, GameSettingsManager.UIVolumeVal);
        }
    }

    private IEnumerator AnimateInRoutine()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        if (questItem != null)
        {
            questItem.gameObject.SetActive(true);
            if (questItem.gameObject.activeInHierarchy && questItem.enabled)
            {
                questItem.ExpandQuest();
                // Allow Heat UI Quest In animation transition
                yield return new WaitForSeconds(0.35f);
            }
        }
        else
        {
            float duration = 0.3f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (canvasGroup != null) canvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
                yield return null;
            }
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
    }

    private IEnumerator AnimateOutRoutine()
    {
        if (questItem != null && questItem.gameObject.activeInHierarchy && questItem.enabled)
        {
            questItem.MinimizeQuest();
            yield return new WaitForSeconds(0.35f);
        }

        if (questItem != null && questItem.gameObject.activeSelf)
        {
            questItem.gameObject.SetActive(false);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }
}
