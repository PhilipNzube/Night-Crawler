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

        if (timerText == null)
        {
            // Auto-locate 'Total' child
            var totalChild = transform.Find("Total");
            if (totalChild != null)
            {
                timerText = totalChild.GetComponent<TextMeshProUGUI>();
            }
            if (timerText == null)
            {
                timerText = GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) _instance = null;
    }

    private void Update()
    {
        if (!_isCountingDown) return;

        if (_timeRemaining > 0f)
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
        if (onlyVisibleToGirl)
        {
            // Check if local player is the Girl
            var localGirl = FindFirstObjectByType<GirlMaterialController>();
            if (localGirl == null || !localGirl.IsOwner)
            {
                return;
            }
        }

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
}
