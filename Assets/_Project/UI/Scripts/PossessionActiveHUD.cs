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
    [Tooltip("Text hint displaying the hotkey to exit possession, e.g. '[E] Exit Body'. Replaces the release button.")]
    public TMP_Text exitPromptText;
    [Tooltip("Optional button if kept, otherwise exit is performed with the hotkey.")]
    public Button releaseButton;

    [Header("Michsky Heat / Dark UI")]
    [Tooltip("Optional: Michsky ButtonManager to release possession.")]
    public Michsky.UI.Heat.ButtonManager heatReleaseButton;

    [Tooltip("Optional: Michsky ProgressBar to show possession time remaining.")]
    public Michsky.UI.Heat.ProgressBar heatTimeRemainingBar;

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

        NightCrawler.UI.MichskyUIBridge.BindButton(releaseButton, heatReleaseButton, OnReleaseClicked);

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

        NightCrawler.UI.MichskyUIBridge.SetProgress(heatTimeRemainingBar, Mathf.Clamp01(rem / 15f));

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
