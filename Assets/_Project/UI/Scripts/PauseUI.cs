using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Controls the In-Game Pause UI menu bindings (Resume, Settings, Disconnect/Quit).
/// Adapted to work directly with SlimUI Modern Menu prefab structures (main menu, exit confirmation dialog, SFX).
/// Multiplayer-friendly: UI overlay with cursor unlock without breaking network time.
/// </summary>
public class PauseUI : MonoBehaviour
{
    [Header("Pause UI Panels")]
    [Tooltip("The main root canvas or panel for the pause overlay.")]
    public GameObject pausePanel;

    [Tooltip("The main button menu panel (Resume, Settings, Quit).")]
    public GameObject firstMenuPanel;

    [Tooltip("The exit/disconnect confirmation pop-up panel.")]
    public GameObject exitMenuPanel;

    [Tooltip("Reference to the SettingsUI component for opening settings from pause menu.")]
    public SettingsUI settingsUI;

    [Header("Main Pause Buttons")]
    [Tooltip("Button to resume game.")]
    public Button resumeButton;

    [Tooltip("Button to open settings.")]
    public Button settingsButton;

    [Tooltip("Button to disconnect/quit back to lobby.")]
    public Button disconnectButton;

    [Header("Exit Dialog Confirmation Buttons (Optional)")]
    public Button confirmDisconnectButton;
    public Button cancelDisconnectButton;

    [Header("SlimUI Audio SFX (Optional)")]
    public AudioSource hoverSound;
    public AudioSource swooshSound;

    // Helper properties to check sub-panel state
    public bool IsSettingsOpen => settingsUI != null && settingsUI.IsSettingsOpen;
    public bool IsExitDialogOpen => exitMenuPanel != null && exitMenuPanel.activeSelf;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private PauseManager _pauseManager;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    private void Start()
    {
        _pauseManager = FindFirstObjectByType<PauseManager>();

        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResumePressed);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsPressed);

        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectPressed);

        if (confirmDisconnectButton != null)
            confirmDisconnectButton.onClick.AddListener(ConfirmDisconnect);

        if (cancelDisconnectButton != null)
            cancelDisconnectButton.onClick.AddListener(CloseExitDialog);

        HidePauseMenu();
    }

    // =========================================================================
    //  Public API & Navigation
    // =========================================================================
    public void ShowPauseMenu()
    {
        if (pausePanel != null) pausePanel.SetActive(true);
        if (firstMenuPanel != null) firstMenuPanel.SetActive(true);
        if (exitMenuPanel != null) exitMenuPanel.SetActive(false);
        if (settingsUI != null) settingsUI.HideSettings();
    }

    public void HidePauseMenu()
    {
        if (pausePanel != null) pausePanel.SetActive(false);
        if (firstMenuPanel != null) firstMenuPanel.SetActive(false);
        if (exitMenuPanel != null) exitMenuPanel.SetActive(false);
        if (settingsUI != null) settingsUI.HideSettings();
    }

    public void CloseSettings()
    {
        if (settingsUI != null) settingsUI.HideSettings();
        if (firstMenuPanel != null) firstMenuPanel.SetActive(true);
        PlaySwooshSFX();
    }

    public void CloseExitDialog()
    {
        if (exitMenuPanel != null) exitMenuPanel.SetActive(false);
        if (firstMenuPanel != null) firstMenuPanel.SetActive(true);
    }

    // =========================================================================
    //  Button Actions (Callable by SlimUI UI Buttons)
    // =========================================================================
    public void OnResumePressed()
    {
        if (_pauseManager != null)
            _pauseManager.ResumeGame();
        else
            HidePauseMenu();
    }

    public void OnSettingsPressed()
    {
        PlaySwooshSFX();
        if (firstMenuPanel != null) firstMenuPanel.SetActive(false);
        if (settingsUI != null) settingsUI.ShowSettings();
    }

    public void OnDisconnectPressed()
    {
        PlayHoverSFX();
        // If an exit dialog is assigned, show confirmation first; otherwise directly disconnect
        if (exitMenuPanel != null)
        {
            if (firstMenuPanel != null) firstMenuPanel.SetActive(false);
            exitMenuPanel.SetActive(true);
        }
        else
        {
            ConfirmDisconnect();
        }
    }

    public void ConfirmDisconnect()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.LoadScene("LobbyScene");
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
    }

    // SFX Helpers
    public void PlayHoverSFX()
    {
        if (hoverSound != null) hoverSound.Play();
    }

    public void PlaySwooshSFX()
    {
        if (swooshSound != null) swooshSound.Play();
    }
}
