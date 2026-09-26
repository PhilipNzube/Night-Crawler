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
    [Tooltip("Text hint displaying the hotkey to exit possession, e.g. '[E] Exit Body'.")]
    public TMP_Text exitPromptText;

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

        // Auto-wire missing hierarchy references
        if (hudContainer == null) hudContainer = gameObject;
        if (victimNameText == null) victimNameText = transform.Find("VictimName/PlayerName")?.GetComponent<TMP_Text>();
        if (timeRemainingText == null) timeRemainingText = transform.Find("TimerText/Time")?.GetComponent<TMP_Text>();
        if (exitPromptText == null) exitPromptText = transform.Find("ExitPromptText/NotificationText")?.GetComponent<TMP_Text>();

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
        float rem = _activeGirlPossession.RemainingPool;
        if (timeRemainingText != null)
        {
            timeRemainingText.text = $"{Mathf.CeilToInt(rem)}s left";
        }

        if (_activeGirlPossession.RemainingPool <= 0.05f)
        {
            OnReleaseClicked();
            return;
        }
    }

    public void Show(string victimName, GirlPossession girlPossession)
    {
        _activeGirlPossession = girlPossession;
        _isActive = true;

        if (victimNameText != null)
        {
            victimNameText.text = $"Possessing: {victimName}";
        }

        if (exitPromptText != null)
        {
            exitPromptText.text = "Press [E] to exit body";
        }

        if (hudContainer != null)
        {
            hudContainer.SetActive(true);
        }
        gameObject.SetActive(true);

        // Keep cursor locked during active possession for seamless mouse looking
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void Hide()
    {
        _isActive = false;
        _activeGirlPossession = null;

        if (hudContainer != null)
        {
            hudContainer.SetActive(false);
        }

        // Re-lock cursor when possession ends
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnReleaseClicked()
    {
        if (_activeGirlPossession != null)
        {
            _activeGirlPossession.RequestRelease();
        }
        Hide();
    }
}
