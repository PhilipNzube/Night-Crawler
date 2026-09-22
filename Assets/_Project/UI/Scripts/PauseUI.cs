using System.Collections;
using UnityEngine;
using Unity.Netcode;
using Michsky.UI.Heat;

/// <summary>
/// Controls the In-Game Pause UI using Heat UI components.
/// Handles panel transitions (Pause Menu <-> Settings), background fading,
/// exit confirmation modal, and multiplayer disconnect routine.
/// </summary>
public class PauseUI : MonoBehaviour
{
    [Header("Heat UI Core")]
    [Tooltip("The root Pause Canvas GameObject. Automatically uses this gameObject if unassigned.")]
    public GameObject pauseCanvas;

    [Tooltip("PanelManager on Main Content that manages panel switching between Pause Menu and Settings.")]
    public PanelManager panelManager;

    [Tooltip("The panel name of the Pause Menu buttons in PanelManager (default: 'PauseMenu').")]
    public string pausePanelName = "PauseMenu";

    [Tooltip("The panel name of the Settings panel in PanelManager (default: 'Settings').")]
    public string settingsPanelName = "Settings";

    [Tooltip("The ImageFading component on Background to smoothly fade in/out when pausing.")]
    public ImageFading backgroundFader;

    [Header("Heat UI Pause Buttons (PanelButton)")]
    [Tooltip("The Resume button (PanelButton on Btn_Resume).")]
    public PanelButton resumeButton;

    [Tooltip("The Settings button (PanelButton on Btn_Settings).")]
    public PanelButton settingsButton;

    [Tooltip("The Exit button (PanelButton on Btn_Exit).")]
    public PanelButton exitButton;

    [Header("Alternate Button Managers (Optional fallback)")]
    public ButtonManager altResumeButton;
    public ButtonManager altSettingsButton;
    public ButtonManager altExitButton;

    [Header("Heat UI Exit Modal Window")]
    [Tooltip("The Exit Confirmation Modal Window (ModalWindowManager).")]
    public ModalWindowManager exitModal;

    // -------------------------------------------------------------------------
    //  State helpers for PauseManager ESC navigation
    // -------------------------------------------------------------------------
    public bool IsSettingsOpen => panelManager != null && panelManager.panels.Count > panelManager.currentPanelIndex
                               && panelManager.panels[panelManager.currentPanelIndex].panelName == settingsPanelName;

    public bool IsExitDialogOpen => exitModal != null && exitModal.isOn;

    private PauseManager _pauseManager;

    private void Awake()
    {
        if (pauseCanvas == null)
            pauseCanvas = gameObject;
    }

    private void Start()
    {
        _pauseManager = FindFirstObjectByType<PauseManager>();

        BindButton(OnResumePressed, resumeButton, altResumeButton);
        BindButton(OnSettingsPressed, settingsButton, altSettingsButton);
        BindButton(OnExitPressed, exitButton, altExitButton);

        if (exitModal != null)
        {
            exitModal.onConfirm.AddListener(ConfirmDisconnect);
            exitModal.onCancel.AddListener(CloseExitDialog);
        }

        // Start hidden
        HidePauseMenu();
    }

    private void BindButton(System.Action callback, PanelButton panelBtn, ButtonManager btnMgr)
    {
        if (panelBtn != null)
            panelBtn.onClick.AddListener(() => callback());
        if (btnMgr != null)
            btnMgr.onClick.AddListener(() => callback());
    }

    // =========================================================================
    //  Public API
    // =========================================================================
    public void ShowPauseMenu()
    {
        if (pauseCanvas != null) pauseCanvas.SetActive(true);
        if (backgroundFader != null) backgroundFader.FadeIn();
        if (panelManager != null) panelManager.OpenPanel(pausePanelName);
    }

    public void HidePauseMenu()
    {
        if (backgroundFader != null) backgroundFader.FadeOut();
        if (exitModal != null && exitModal.isOn) exitModal.CloseWindow();
        if (panelManager != null) panelManager.HideCurrentPanel();
    }

    public void CloseSettings()
    {
        if (panelManager != null) panelManager.OpenPanel(pausePanelName);
    }

    public void CloseExitDialog()
    {
        if (exitModal != null && exitModal.isOn) exitModal.CloseWindow();
    }

    // =========================================================================
    //  Button Handlers
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
        if (panelManager != null)
            panelManager.OpenPanel(settingsPanelName);
    }

    public void OnExitPressed()
    {
        if (exitModal != null)
            exitModal.OpenWindow();
        else
            ConfirmDisconnect();
    }

    public void ConfirmDisconnect()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (GameManager.Instance != null && GameManager.Instance.gameObject.activeInHierarchy)
        {
            GameManager.Instance.StartCoroutine(DisconnectRoutine());
        }
        else
        {
            StartCoroutine(DisconnectRoutine());
        }
    }

    private IEnumerator DisconnectRoutine()
    {
        if (panelManager != null) panelManager.HideCurrentPanel();

        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsServer && GameManager.Instance != null)
            {
                try
                {
                    GameManager.Instance.NotifyHostLeavingClientRpc();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[PauseUI] Broadcast error: {ex.Message}");
                }
                yield return new WaitForSecondsRealtime(0.15f);
            }

            try
            {
                NetworkManager.Singleton.Shutdown();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PauseUI] Shutdown exception: {ex.Message}");
            }
        }

        yield return new WaitForSecondsRealtime(0.05f);

        if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.LoadScene("LobbyScene");
        }
        else
        {
            LoadingScreen.TargetSceneToLoad = "LobbyScene";
            UnityEngine.SceneManagement.SceneManager.LoadScene("LoadingScene");
        }
    }
}
