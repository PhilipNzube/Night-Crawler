using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Controls the In-Game Pause UI using the SlimUI Modern Menu prefab.
///
/// HOW THIS MAPS TO SLIMUI:
///   pauseRootPanel    = the root Canvas / CanvasGroup of the SlimUI prefab (the whole thing)
///   mainMenu          = SlimUI's "mainMenu" GameObject (holds all panels)
///   firstMenu         = SlimUI's "firstMenu" (the first button list: Resume / Settings / Quit)
///   exitMenu          = SlimUI's "exitMenu" (the "Are You Sure?" quit confirmation dialog)
///   settingsMenuCanvas = The GameObject that holds SettingsUI (outside SlimUI's mainMenu)
///   slimUIAnimator    = The Animator on the SlimUI root that drives "Animate" float (camera anim)
///   hoverSound        = SlimUI's AudioSource for hover SFX
///   swooshSound       = SlimUI's AudioSource for swoosh SFX when switching to Settings
///
/// FIELDS REMOVED FROM PREVIOUS VERSION that don't exist in SlimUI:
///   - pausePanel (replaced by pauseRootPanel)
///   - firstMenuPanel (replaced by firstMenu — matches SlimUI exactly)
///   - exitMenuPanel  (replaced by exitMenu — matches SlimUI exactly)
///   confirmDisconnectButton and cancelDisconnectButton remain — they ARE buttons
///   that exist inside SlimUI's exitMenu panel.
/// </summary>
public class PauseUI : MonoBehaviour
{
    [Header("SlimUI Root (the whole prefab)")]
    [Tooltip("The root GameObject of your SlimUI Canvas_DefaultTemplate1 prefab. " +
             "This entire object is enabled/disabled when pausing.")]
    public GameObject pauseRootPanel;

    [Header("SlimUI Menu GameObjects — match names exactly from the prefab hierarchy")]
    [Tooltip("SlimUI 'mainMenu' — the parent that wraps all button panels.")]
    public GameObject mainMenu;

    [Tooltip("SlimUI 'firstMenu' — the initial list of buttons (Resume, Settings, Exit).")]
    public GameObject firstMenu;

    [Tooltip("SlimUI 'exitMenu' — the Are You Sure quit/disconnect confirmation popup.")]
    public GameObject exitMenu;

    [Header("Settings Panel (our SettingsUI — not SlimUI's native settings)")]
    [Tooltip("A separate GameObject in the scene that holds the SettingsUI component. " +
             "It is shown/hidden independently of SlimUI panels.")]
    public SettingsUI settingsUI;

    [Header("SlimUI Camera Animator")]
    [Tooltip("The Animator component on the SlimUI Canvas root. " +
             "SlimUI uses SetFloat('Animate', 1) to move the camera to position 2 (Settings). " +
             "We reuse this same animation to move to the pause camera view.")]
    public Animator slimUIAnimator;

    [Header("Pause Buttons — wire to SlimUI button OnClick events")]
    [Tooltip("Resume button — drag SlimUI's Resume/Play button here.")]
    public Button resumeButton;

    [Tooltip("Settings button — drag SlimUI's Settings button here.")]
    public Button settingsButton;

    [Tooltip("Exit/Disconnect button — drag SlimUI's Exit button here.")]
    public Button disconnectButton;

    [Header("Exit Dialog Buttons — inside SlimUI's exitMenu panel")]
    [Tooltip("'Yes' button inside exitMenu.")]
    public Button confirmDisconnectButton;

    [Tooltip("'No' button inside exitMenu.")]
    public Button cancelDisconnectButton;

    [Header("SlimUI Audio SFX")]
    [Tooltip("AudioSource for hover SFX — found on SlimUI Manager as 'hoverSound'.")]
    public AudioSource hoverSound;

    [Tooltip("AudioSource for swoosh SFX — found on SlimUI Manager as 'swooshSound'.")]
    public AudioSource swooshSound;

    // -------------------------------------------------------------------------
    //  State helpers for PauseManager ESC navigation
    // -------------------------------------------------------------------------
    public bool IsSettingsOpen    => settingsUI != null && settingsUI.IsSettingsOpen;
    public bool IsExitDialogOpen  => exitMenu   != null && exitMenu.activeSelf;

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

        // Start hidden
        HidePauseMenu();
    }

    // =========================================================================
    //  Public API
    // =========================================================================
    public void ShowPauseMenu()
    {
        if (pauseRootPanel != null) pauseRootPanel.SetActive(true);
        if (mainMenu != null)  mainMenu.SetActive(true);
        if (firstMenu != null) firstMenu.SetActive(true);
        if (exitMenu  != null) exitMenu.SetActive(false);

        if (settingsUI != null) settingsUI.HideSettings();

        // Trigger the SlimUI camera animation to move to position 1 (main menu / pause view)
        if (slimUIAnimator != null)
            slimUIAnimator.SetFloat("Animate", 0f);
    }

    public void HidePauseMenu()
    {
        if (pauseRootPanel != null) pauseRootPanel.SetActive(false);
        if (mainMenu  != null) mainMenu.SetActive(false);
        if (firstMenu != null) firstMenu.SetActive(false);
        if (exitMenu  != null) exitMenu.SetActive(false);

        if (settingsUI != null) settingsUI.HideSettings();
    }

    public void CloseSettings()
    {
        if (settingsUI != null) settingsUI.HideSettings();
        if (firstMenu  != null) firstMenu.SetActive(true);
        PlaySwooshSFX();

        // Return SlimUI camera to the main button list position
        if (slimUIAnimator != null)
            slimUIAnimator.SetFloat("Animate", 0f);
    }

    public void CloseExitDialog()
    {
        if (exitMenu  != null) exitMenu.SetActive(false);
        if (firstMenu != null) firstMenu.SetActive(true);
    }

    // =========================================================================
    //  Button Handlers (wire to SlimUI button OnClick events in Inspector)
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
        if (firstMenu != null) firstMenu.SetActive(false);

        // Trigger SlimUI camera anim — same "Animate" = 1 SlimUI uses for settings camera swing
        if (slimUIAnimator != null)
            slimUIAnimator.SetFloat("Animate", 1f);

        if (settingsUI != null) settingsUI.ShowSettings();
    }

    public void OnDisconnectPressed()
    {
        PlayHoverSFX();
        if (exitMenu  != null)
        {
            if (firstMenu != null) firstMenu.SetActive(false);
            exitMenu.SetActive(true);
        }
        else
        {
            ConfirmDisconnect();
        }
    }

    public void ConfirmDisconnect()
    {
        // Unpause game & restore time scale before leaving
        PauseManager pauseMgr = FindFirstObjectByType<PauseManager>();
        if (pauseMgr != null)
            pauseMgr.SetPaused(false);

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.LoadScene("LobbyScene");
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
    }

    // =========================================================================
    //  SFX
    // =========================================================================
    public void PlayHoverSFX()
    {
        if (hoverSound != null) hoverSound.Play();
    }

    public void PlaySwooshSFX()
    {
        if (swooshSound != null) swooshSound.Play();
    }
}
