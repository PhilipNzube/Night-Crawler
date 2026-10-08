using UnityEngine;
using TMPro;

/// <summary>
/// SOLID — SRP: Controls the ManifestationHUD countdown overlay.
/// When the Girl character manifests (becomes visible), this HUD activates
/// and displays a countdown timer formatted as '00:00' (MM:SS) until manifestation ends.
/// </summary>
public class ManifestationHUD : MonoBehaviour
{
    private static ManifestationHUD _instance;
    public static ManifestationHUD Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ManifestationHUD>(FindObjectsInactive.Include);
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
    [Tooltip("Root container of the Manifestation HUD. If null, uses this gameObject.")]
    public GameObject hudContainer;

    [Tooltip("Text component displaying the countdown (formatted 00:00). Found on child 'Total'.")]
    public TextMeshProUGUI timerText;

    [Tooltip("Optional CanvasGroup for smooth fade or visibility control.")]
    public CanvasGroup canvasGroup;

    [Header("Audience Filter")]
    [Tooltip("If true, only the Girl player sees ManifestationHUD. If false, everyone in the match sees it.")]
    public bool onlyVisibleToGirl = false;

    private float _timeRemaining = 0f;
    private bool _isCountingDown = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (hudContainer == null)
        {
            hudContainer = gameObject;
        }

        EnsureOnScreen();

        if (timerText == null)
        {
            Debug.LogWarning("[ManifestationHUD] timerText is not assigned in the Inspector. Please assign the TextMeshProUGUI component in the Editor.");
        }

        Hide();
    }

    private void EnsureOnScreen()
    {
        if (transform is RectTransform rect)
        {
            // If anchored to the top of the canvas, positive Y pushes the HUD offscreen above the viewport.
            if (rect.anchorMin.y >= 0.8f && rect.anchoredPosition.y > 0f)
            {
                Debug.LogWarning($"[ManifestationHUD] RectTransform anchoredPosition.y was offscreen ({rect.anchoredPosition.y}). Adjusting to -60f.");
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -60f);
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) _instance = null;
    }

    private GirlMaterialController _girlController;

    private void Update()
    {
        if (!_isCountingDown) return;

        if (_girlController == null)
        {
            _girlController = FindFirstObjectByType<GirlMaterialController>();
        }

        if (_girlController != null)
        {
            _timeRemaining = _girlController.remainingManifestTime.Value;
            UpdateTimerText(_timeRemaining);
            if (!_girlController.isManifested.Value || _timeRemaining <= 0f)
            {
                Hide();
            }
        }
        else if (_timeRemaining > 0f)
        {
            _timeRemaining -= Time.deltaTime;
            if (_timeRemaining < 0f) _timeRemaining = 0f;

            UpdateTimerText(_timeRemaining);

            if (_timeRemaining <= 0f)
            {
                Hide();
            }
        }
    }

    /// <summary>
    /// Shows the manifestation countdown timer for the specified duration (seconds).
    /// </summary>
    public void Show(float duration)
    {
        if (onlyVisibleToGirl && !IsLocalPlayerGirl())
        {
            return;
        }

        EnsureOnScreen();

        _timeRemaining = duration;
        _isCountingDown = true;

        UpdateTimerText(_timeRemaining);

        if (hudContainer != null && !hudContainer.activeSelf)
        {
            hudContainer.SetActive(true);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    /// <summary>
    /// Hides the manifestation countdown HUD immediately.
    /// </summary>
    public void Hide()
    {
        _isCountingDown = false;
        _timeRemaining = 0f;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (hudContainer != null && hudContainer.activeSelf)
        {
            hudContainer.SetActive(false);
        }
    }

    private void UpdateTimerText(float seconds)
    {
        if (timerText == null) return;

        int totalSec = Mathf.CeilToInt(seconds);
        int mins = totalSec / 60;
        int secs = totalSec % 60;
        timerText.text = $"{mins:00}:{secs:00}";
    }

    private bool IsLocalPlayerGirl()
    {
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            var localClient = Unity.Netcode.NetworkManager.Singleton.LocalClient;
            if (localClient?.PlayerObject != null)
            {
                if (localClient.PlayerObject.GetComponent<GirlMaterialController>() != null ||
                    localClient.PlayerObject.name.ToLower().Contains("girl") ||
                    localClient.PlayerObject.name.ToLower().Contains("wraith"))
                {
                    return true;
                }
            }
        }

        var girls = FindObjectsByType<GirlMaterialController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var girl in girls)
        {
            if (girl != null && girl.IsOwner) return true;
        }

        if (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            return true;
        }

        return false;
    }
}
