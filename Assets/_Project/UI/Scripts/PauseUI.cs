using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Controls the In-Game Pause UI menu bindings (Resume, Settings, Disconnect).
/// Multiplayer-friendly: UI overlay with cursor unlock without breaking network time.
/// </summary>
public class PauseUI : MonoBehaviour
{
    [Header("Pause UI Panels")]
    [Tooltip("The main root panel for the pause overlay.")]
    public GameObject pausePanel;

    [Tooltip("Reference to the SettingsUI component for opening settings from pause menu.")]
    public SettingsUI settingsUI;

    [Header("Buttons")]
    [Tooltip("Button to resume game.")]
    public Button resumeButton;

    [Tooltip("Button to open settings.")]
    public Button settingsButton;

    [Tooltip("Button to disconnect/quit back to lobby or main menu.")]
    public Button disconnectButton;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private PauseManager _pauseManager;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Start()
    {
        _pauseManager = FindFirstObjectByType<PauseManager>();

        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResumePressed);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsPressed);

        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectPressed);

        HidePauseMenu();
    }

    // =========================================================================
    //  Public API
    // =========================================================================
    public void ShowPauseMenu()
    {
        if (pausePanel != null) pausePanel.SetActive(true);
    }

    public void HidePauseMenu()
    {
        if (pausePanel != null) pausePanel.SetActive(false);
        if (settingsUI != null) settingsUI.HideSettings();
    }

    // =========================================================================
    //  Button Actions
    // =========================================================================
    private void OnResumePressed()
    {
        if (_pauseManager != null)
            _pauseManager.ResumeGame();
        else
            HidePauseMenu();
    }

    private void OnSettingsPressed()
    {
        if (settingsUI != null)
            settingsUI.ShowSettings();
    }

    private void OnDisconnectPressed()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.LoadScene("LobbyScene");
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
    }
}
