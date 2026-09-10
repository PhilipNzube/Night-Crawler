using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// SOLID — SRP: Handles onscreen banner and alert notifications (e.g. Suffocation warning, Deal alerts).
/// Singleton pattern allows any system to post messages cleanly.
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
    [Tooltip("Text component to show the message.")]
    public TMP_Text notificationText;

    [Tooltip("Panel or CanvasGroup containing the notification.")]
    public CanvasGroup canvasGroup;

    [Header("Audio (Optional)")]
    public AudioClip notificationSound;
    private AudioSource _audioSource;

    private Coroutine _displayCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null && notificationSound != null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
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

    public void ShowNotification(string message, float duration = 4f)
    {
        if (_displayCoroutine != null)
        {
            StopCoroutine(_displayCoroutine);
        }
        _displayCoroutine = StartCoroutine(DisplayRoutine(message, duration));
    }

    private IEnumerator DisplayRoutine(string message, float duration)
    {
        if (notificationText != null)
        {
            notificationText.text = message;
        }

        if (_audioSource != null && notificationSound != null)
        {
            _audioSource.PlayOneShot(notificationSound);
        }

        // Fade In
        float elapsed = 0f;
        float fadeTime = 0.3f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeTime);
            yield return null;
        }

        if (canvasGroup != null) canvasGroup.alpha = 1f;

        yield return new WaitForSeconds(duration);

        // Fade Out
        elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Clamp01(1f - (elapsed / fadeTime));
            yield return null;
        }

        if (canvasGroup != null) canvasGroup.alpha = 0f;
        _displayCoroutine = null;
    }
}
