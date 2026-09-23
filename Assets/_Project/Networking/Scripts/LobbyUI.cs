using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Michsky.UI.Heat;
using NightCrawler.UI;
using NightCrawler.Economy;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Manages the pre-game lobby UI flow using Michsky Heat UI.
///
/// Flow:
///   1. Splash Screen / Launch  -> Shows Name Entry if first time (with Cancel 'X' disabled),
///                                 or straight to Connection Panel if player has a saved name.
///   2. Bottom Profile Bar      -> Shows callsign if saved; hidden completely if not saved.
///   3. Connection Panel        -> Choose Host or Join.
///   4. Join Code Panel         -> Client types/pastes 6-char Relay code.
///   5. Host Lobby Panel        -> Host waiting room with code, copy button, player counter, start match.
///   6. Client Lobby Panel      -> Client waiting room with room code and player counter.
///   7. Exit Modal Triggers     -> Multiple buttons (Top 'X', Bottom Quit, etc.) open the Exit Window.
///   8. Match Start             -> Root Canvas hides completely, handing over smoothly to PreGameCanvas.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    public enum NetworkMode
    {
        Relay,      // Online multiplayer via Unity Relay Join Codes
        LocalLAN    // Direct IP / LAN connection (127.0.0.1) for local testing
    }

    // -------------------------------------------------------------------------
    //  Inspector — Networking Mode
    // -------------------------------------------------------------------------
    [Header("Networking Mode  ← Switch to LocalLAN for quick local dev")]
    [Tooltip("Select Relay for online Join Codes, or LocalLAN for fast offline/local testing.")]
    public NetworkMode networkMode = NetworkMode.Relay;

    [Tooltip("Direct IP used when NetworkMode is LocalLAN (default 127.0.0.1).")]
    public string localIpAddress = "127.0.0.1";

    [Tooltip("Port used when NetworkMode is LocalLAN (default 7777).")]
    public ushort localPort = 7777;

    // -------------------------------------------------------------------------
    //  Inspector — 1. Name Entry Panel
    // -------------------------------------------------------------------------
    [Header("1. Name Entry Panel  ← Shown first if no name saved")]
    [Tooltip("Root panel for name entry. Shown before Connection Panel if player has no saved name.")]
    public GameObject nameEntryPanel;

    [Tooltip("Michsky Heat Modal Window for Name Entry. Opens cleanly as a popup without hiding the background!")]
    public ModalWindowManager heatNameEntryModal;

    [Tooltip("Michsky Input Field where the player types their name.")]
    public InputFieldManager heatNameEntryInputField;

    [Tooltip("Check this box to activate accepting emojis in the name input field. Uncheck to disallow emojis.")]
    public bool allowEmojisInName = false;

    [Tooltip("Placeholder text inside the input field (e.g. 'Enter your name...')")]
    public TextMeshProUGUI nameEntryPlaceholder;

    [Tooltip("Error/hint label shown when the player tries to confirm with an empty name.")]
    public TextMeshProUGUI nameEntryErrorText;

    [Tooltip("Button that confirms the entered name and advances to the Connection Panel.")]
    public ButtonManager heatNameConfirmButton;

    [Tooltip("Close / Cancel 'X' button on the Name Entry modal.")]
    public GameObject nameEntryCancelButton;

    [Tooltip("If checked, cancelling Name Entry prompts the 'Are You Sure' exit modal. If unchecked, it closes the modal directly without asking.")]
    public bool showExitConfirmOnNameCancel = false;

    // -------------------------------------------------------------------------
    //  Inspector — 2. Connection Panel (Host / Join Choice)
    // -------------------------------------------------------------------------
    [Header("2. Connection Panel  ← Main menu to choose Host or Join")]
    [Tooltip("Root panel shown after name confirmation (Host / Join choice screen).")]
    public GameObject connectionPanel;

    [Tooltip("Box Button for Start Host.")]
    public BoxButtonManager heatBoxStartHostButton;

    [Tooltip("Box Button for Join Game.")]
    public BoxButtonManager heatBoxStartClientButton;

    // -------------------------------------------------------------------------
    //  Inspector — 3. Bottom Profile Bar
    // -------------------------------------------------------------------------
    [Header("3. Bottom Profile Bar & Credits")]
    [Tooltip("The Profile GameObject in the Bottom Panel. Hidden until the player enters a valid name.")]
    public GameObject profileSection;

    [Tooltip("Text label inside the Profile section displaying the player's name.")]
    public TextMeshProUGUI profileNameText;

    [Tooltip("Text label inside the Profile section displaying the player's level (e.g. 'Lv. 14 • Specialist').")]
    public TextMeshProUGUI profileLevelText;

    [Tooltip("Optional text label in the Bottom Panel (or header) displaying the player's credit balance.")]
    public TextMeshProUGUI creditBalanceText;

    [Tooltip("The main PanelManager for top tab navigation (Main Content). If unassigned, will be auto-found.")]
    public PanelManager mainPanelManager;

    // -------------------------------------------------------------------------
    //  Inspector — 4. Exit / Leave Confirmation Modal
    // -------------------------------------------------------------------------
    [Header("4. Exit / Leave Confirmation Modal")]
    [Tooltip("Michsky Modal Window Manager for confirming exit / disconnect.")]
    public ModalWindowManager exitConfirmModal;

    [Tooltip("Confirm button inside the Exit Confirmation modal (calls ConfirmDisconnect).")]
    public ButtonManager heatExitConfirmButton;

    [Tooltip("Optional single button that triggers the Exit Confirmation Modal (e.g. Top 'X' button).")]
    public ButtonManager heatExitTriggerButton;

    [Tooltip("Multiple Heat Buttons that trigger the Exit Confirmation Modal (e.g. Top 'X', Quit Button, Leave Button). Drag any number of buttons here!")]
    public List<ButtonManager> exitTriggerButtons = new List<ButtonManager>();

    [Tooltip("Multiple Heat Box Buttons that trigger the Exit Confirmation Modal.")]
    public List<BoxButtonManager> boxExitTriggerButtons = new List<BoxButtonManager>();

    [Tooltip("Multiple standard UI Buttons that trigger the Exit Confirmation Modal.")]
    public List<Button> standardExitTriggerButtons = new List<Button>();

    // -------------------------------------------------------------------------
    //  Inspector — 5. Dedicated Client Join Code Panel
    // -------------------------------------------------------------------------
    [Header("5. Join Code Panel (Client)  ← Dedicated screen to enter code")]
    [Tooltip("Dedicated panel where client enters the Join Code. Opened when clicking 'Join Game' in Relay mode.")]
    public GameObject joinCodePanel;

    [Tooltip("Michsky Input field where client enters / pastes the 6-character Join Code.")]
    public InputFieldManager heatJoinCodeInputField;

    [Tooltip("Button inside the Join Code panel that connects to the Relay room.")]
    public ButtonManager heatJoinCodeSubmitButton;

    [Tooltip("Button inside the Join Code panel that goes back to the main Connection screen.")]
    public ButtonManager heatJoinCodeBackButton;

    [Tooltip("Error text displayed on the Join Code modal when the code is empty, invalid, or connection fails.")]
    public TextMeshProUGUI joinCodeErrorText;

    // -------------------------------------------------------------------------
    //  Inspector — 6. Dedicated Host Lobby Panel
    // -------------------------------------------------------------------------
    [Header("6. Host Lobby Panel  ← Dedicated screen for Host with code & start")]
    [Tooltip("Dedicated panel shown to the host after creating a room.")]
    public GameObject hostLobbyPanel;

    [Tooltip("Displays the generated Join Code on the host panel (e.g. 'LOBBY CODE: A9F3X2').")]
    public TextMeshProUGUI hostJoinCodeText;

    [Tooltip("Button that copies the session Join Code to the host's clipboard.")]
    public ButtonManager heatHostCopyCodeButton;

    [Tooltip("Displays connected player count on the host panel, e.g. '2 / 6 players'.")]
    public TextMeshProUGUI hostPlayerCountText;

    [Tooltip("Status message on host panel, e.g. 'Waiting for players...' or 'Ready to start!'.")]
    public TextMeshProUGUI hostStatusText;

    [Tooltip("'START MATCH' button — active when enough players are connected.")]
    public ButtonManager heatHostStartMatchButton;

    [Tooltip("Button that lets the host cancel and return to the connection screen.")]
    public ButtonManager heatHostDisconnectButton;

    // -------------------------------------------------------------------------
    //  Inspector — 7. Dedicated Client Lobby Panel (Waiting Room)
    // -------------------------------------------------------------------------
    [Header("7. Client Lobby Panel  ← Dedicated waiting room for Clients")]
    [Tooltip("Dedicated panel shown to clients while waiting for the host.")]
    public GameObject clientLobbyPanel;

    [Tooltip("Displays connected room code to client (e.g. 'ROOM: A9F3X2').")]
    public TextMeshProUGUI clientJoinCodeText;

    [Tooltip("Displays connected player count on client panel.")]
    public TextMeshProUGUI clientPlayerCountText;

    [Tooltip("Status message shown to client, e.g. 'Waiting for host to start...'")]
    public TextMeshProUGUI clientStatusText;

    [Tooltip("Button that lets client leave and return to the connection screen.")]
    public ButtonManager heatClientDisconnectButton;

    // -------------------------------------------------------------------------
    //  Inspector — 8. Root Canvas & Scene Settings
    // -------------------------------------------------------------------------
    [Header("8. Root Canvas & Scene Settings")]
    [Tooltip("The root Canvas / Main Menu GameObject to hide completely when the match begins, revealing PreGameCanvas.")]
    public GameObject lobbyCanvasRoot;

    [Tooltip("Minimum connected players required to enable 'START MATCH'. Set to 1 for solo testing, or 2+ for multiplayer builds.")]
    public int minPlayers = 1;

    [Tooltip("Maximum allowed players in the lobby (e.g. 6: 1 Vengeful Spirit + 5 Investigators).")]
    public int maxPlayers = 6;

    [Tooltip("The name of the Game Scene containing GameManager and map spawn points.")]
    public string gameSceneName = "GameScene";

    // -------------------------------------------------------------------------
    //  Inspector — 9. Loading Overlay
    // -------------------------------------------------------------------------
    [Header("9. Loading Overlay")]
    [Tooltip("Full-screen semi-transparent overlay that covers the UI during code generation and connection.")]
    public GameObject loadingOverlayPanel;

    [Tooltip("Spinner GameObject (Michsky Spinner / Heat Loader).")]
    public GameObject loadingSpinner;

    [Tooltip("TextMeshProUGUI explaining what is currently loading (e.g. 'Generating Relay lobby code...').")]
    public TextMeshProUGUI loadingStatusText;

    [Tooltip("If enabled, LobbyUI will continuously spin the spinner via code. Turn OFF if your spinner already has an Animator/Animation.")]
    public bool spinSpinnerInCode = true;

    [Tooltip("Rotation speed in degrees per second when spinSpinnerInCode is enabled (default: 250).")]
    public float spinnerRotationSpeed = 250f;

    public static LobbyUI Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private float _refreshInterval = 0.5f;
    private float _refreshTimer;
    private bool  _isHidden = false;
    private Coroutine _copyFeedbackCoroutine;
    private int _lastEscapeFrame = -1;

    private enum ActiveView { None, Home, NameEntry, JoinCode, HostLobby, ClientLobby }
    private ActiveView _activeView = ActiveView.Home;

    private enum PendingStartAction { None, Host, Client }
    private PendingStartAction _pendingStartAction = PendingStartAction.None;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        Instance = this;
        EnsureRelayManager();
    }

    private void OnEnable()
    {
        CloudCharacterSaveManager.OnProfileLoaded += HandleProfileLoaded;
        CloudCharacterSaveManager.OnCreditsChanged += HandleCreditsChanged;
    }

    private void OnDisable()
    {
        CloudCharacterSaveManager.OnProfileLoaded -= HandleProfileLoaded;
        CloudCharacterSaveManager.OnCreditsChanged -= HandleCreditsChanged;
    }

    private void HandleCreditsChanged(int newBalance)
    {
        UpdateCreditsUI(newBalance);
    }

    public void UpdateCreditsUI(int newBalance = -1)
    {
        if (creditBalanceText != null)
        {
            int bal = newBalance >= 0 ? newBalance : (CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.CurrentCredits : 60);
            creditBalanceText.text = CurrencyConfig.FormatBalance(bal);
        }
    }

    private void HandleProfileLoaded(PlayerProfileData profile)
    {
        if (profile == null) return;
        string curName = MichskyUIBridge.GetInputText(null, heatNameEntryInputField);
        if (string.IsNullOrEmpty(curName) || curName == "Investigator")
        {
            MichskyUIBridge.SetInputText(null, heatNameEntryInputField, PlayerNameManager.SanitizePlayerName(profile.playerName, allowEmojisInName));
        }
        UpdateCreditsUI();
        if (PlayerNameManager.HasSavedName() && nameEntryPanel != null && nameEntryPanel.activeSelf)
        {
            ShowConnectionPanel();
        }
    }

    void Start()
    {
        WireButtonListeners();

        MichskyUIBridge.SetInputText(null, heatNameEntryInputField, PlayerNameManager.GetPlayerName(allowEmojisInName));

        if (nameEntryErrorText != null)
            nameEntryErrorText.gameObject.SetActive(false);

        // Ensure Name Entry modal/panel is closed on startup so it does not block raycasts
        if (heatNameEntryModal != null)
            heatNameEntryModal.CloseWindow();
        else if (nameEntryPanel != null)
            nameEntryPanel.SetActive(false);

        UpdateProfileUI();
        UpdateCreditsUI();
        HideLoading();

        // Always show the home connection panel on start
        ShowConnectionPanel();
    }

    void Update()
    {
        if (_isHidden) return;

        // Escape Key Handling for Lobby Modals / Panels
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleLobbyEscape();
        }

        // Smooth code-driven spinner rotation (independent of periodic lobby refresh timer)
        if (spinSpinnerInCode && loadingSpinner != null && loadingSpinner.activeInHierarchy)
        {
            loadingSpinner.transform.Rotate(0f, 0f, -spinnerRotationSpeed * Time.deltaTime);
        }

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer > 0f) return;
        _refreshTimer = _refreshInterval;

        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            RefreshLobbyPanels();
    }

    /// <summary>
    /// Coordinates pressing Esc on any lobby popup (Name Entry, Join Code, Exit Confirm, or Host/Client Lobby).
    /// </summary>
    public void HandleLobbyEscape()
    {
        if (Time.frameCount == _lastEscapeFrame) return;
        _lastEscapeFrame = Time.frameCount;

        // 1. If Exit Confirmation Modal is currently open, cancel it and keep user in their active view
        if (exitConfirmModal != null && exitConfirmModal.isOn)
        {
            OnCancelExitConfirm();
            return;
        }

        // 2. If Name Entry Modal is currently open, cancel it and cleanly revert to Home
        if (heatNameEntryModal != null && (heatNameEntryModal.isOn || (nameEntryPanel != null && nameEntryPanel.activeSelf)))
        {
            OnCancelNameEntry();
            return;
        }

        // 3. If Join Code Panel/Modal is currently open, close it and cleanly revert to Home
        var joinModal = joinCodePanel != null ? joinCodePanel.GetComponent<ModalWindowManager>() : null;
        if ((joinModal != null && joinModal.isOn) || (joinCodePanel != null && joinCodePanel.activeSelf))
        {
            if (joinModal != null && joinModal.isOn)
                joinModal.CloseWindow();
            ShowConnectionPanel();
            return;
        }

        // 4. If Host Lobby or Client Lobby is open, request disconnect (prompts exit confirm)
        if (_activeView == ActiveView.HostLobby || _activeView == ActiveView.ClientLobby ||
            (hostLobbyPanel != null && hostLobbyPanel.activeSelf) || (clientLobbyPanel != null && clientLobbyPanel.activeSelf))
        {
            RequestDisconnect();
            return;
        }

        // Note: When on the Home screen and no modal is open, Esc intentionally does nothing.
    }

    private void EnsureRelayManager()
    {
        if (RelayManager.Instance == null)
        {
            var existing = FindFirstObjectByType<RelayManager>();
            if (existing == null)
            {
                var go = new GameObject("RelayManager");
                go.AddComponent<RelayManager>();
            }
        }
    }

    // =========================================================================
    //  Button Wiring
    // =========================================================================
    private void WireButtonListeners()
    {
        // 1. Name Entry
        MichskyUIBridge.BindButton(null, heatNameConfirmButton, OnConfirmName);
        MichskyUIBridge.BindInputField(null, heatNameEntryInputField, OnNameInputChanged);

        if (heatNameEntryInputField != null && heatNameEntryInputField.inputText != null)
        {
            heatNameEntryInputField.inputText.onValidateInput = ValidateNameChar;
            var filter = heatNameEntryInputField.GetComponent<EmojiInputFilter>();
            if (filter != null)
            {
                filter.AllowEmojis = allowEmojisInName;
            }
        }

        ModalWindowManager nameModal = heatNameEntryModal != null
            ? heatNameEntryModal
            : (nameEntryPanel != null ? nameEntryPanel.GetComponent<ModalWindowManager>() : null);

        if (nameModal != null)
        {
            nameModal.closeOnCancel = false;
            nameModal.onCancel.RemoveListener(OnCancelNameEntry);
            nameModal.onCancel.AddListener(OnCancelNameEntry);
            nameModal.onClose.RemoveListener(OnCancelNameEntry);
            nameModal.onClose.AddListener(OnCancelNameEntry);
        }

        ModalWindowManager joinModal = joinCodePanel != null ? joinCodePanel.GetComponent<ModalWindowManager>() : null;
        if (joinModal != null)
        {
            joinModal.onCancel.RemoveListener(ShowConnectionPanel);
            joinModal.onCancel.AddListener(ShowConnectionPanel);
            joinModal.onClose.RemoveListener(ShowConnectionPanel);
            joinModal.onClose.AddListener(ShowConnectionPanel);
        }

        if (nameEntryCancelButton != null)
        {
            var btnMgr = nameEntryCancelButton.GetComponent<ButtonManager>();
            var uBtn = nameEntryCancelButton.GetComponent<Button>();
            if (btnMgr != null)
            {
                btnMgr.onClick.RemoveAllListeners();
                btnMgr.onClick.AddListener(OnCancelNameEntry);
            }
            else if (uBtn != null)
            {
                uBtn.onClick.RemoveAllListeners();
                uBtn.onClick.AddListener(OnCancelNameEntry);
            }
        }

        // 2. Connection Panel
        MichskyUIBridge.BindButton(null, heatBoxStartHostButton, OnStartHost);
        MichskyUIBridge.BindButton(null, heatBoxStartClientButton, OnStartClientChoice);

        // 3. Join Code Panel (Client)
        MichskyUIBridge.BindButton(null, heatJoinCodeSubmitButton, OnSubmitJoinCode);
        MichskyUIBridge.BindButton(null, heatJoinCodeBackButton, ShowConnectionPanel);
        MichskyUIBridge.BindInputField(null, heatJoinCodeInputField, OnJoinCodeInputChanged);

        // 4. Host Lobby Panel
        MichskyUIBridge.BindButton(null, heatHostStartMatchButton, OnStartMatch);
        MichskyUIBridge.BindButton(null, heatHostDisconnectButton, RequestDisconnect);
        MichskyUIBridge.BindButton(null, heatHostCopyCodeButton, OnCopyJoinCode);

        if (hostLobbyPanel != null)
        {
            var hostModal = hostLobbyPanel.GetComponent<ModalWindowManager>();
            if (hostModal != null)
            {
                hostModal.closeOnCancel = false;
                hostModal.onCancel.RemoveListener(RequestDisconnect);
                hostModal.onCancel.AddListener(RequestDisconnect);
                if (hostModal.cancelButton != null)
                {
                    MichskyUIBridge.BindButton(null, hostModal.cancelButton, RequestDisconnect);
                }
            }

            foreach (var he in hostLobbyPanel.GetComponentsInChildren<HotkeyEvent>(true))
            {
                he.enabled = false;
            }
        }

        // 5. Client Lobby Panel
        MichskyUIBridge.BindButton(null, heatClientDisconnectButton, RequestDisconnect);

        if (clientLobbyPanel != null)
        {
            var clientModal = clientLobbyPanel.GetComponent<ModalWindowManager>();
            if (clientModal != null)
            {
                clientModal.closeOnCancel = false;
                clientModal.onCancel.RemoveListener(RequestDisconnect);
                clientModal.onCancel.AddListener(RequestDisconnect);
                if (clientModal.cancelButton != null)
                {
                    MichskyUIBridge.BindButton(null, clientModal.cancelButton, RequestDisconnect);
                }
            }

            foreach (var he in clientLobbyPanel.GetComponentsInChildren<HotkeyEvent>(true))
            {
                he.enabled = false;
            }
        }

        // 6. Exit Confirmation Modal Actions
        MichskyUIBridge.BindButton(null, heatExitConfirmButton, ConfirmDisconnect);

        if (exitConfirmModal != null)
        {
            exitConfirmModal.closeOnCancel = false;
            exitConfirmModal.onCancel.RemoveListener(OnCancelExitConfirm);
            exitConfirmModal.onCancel.AddListener(OnCancelExitConfirm);
            if (exitConfirmModal.cancelButton != null)
            {
                MichskyUIBridge.BindButton(null, exitConfirmModal.cancelButton, OnCancelExitConfirm);
            }

            foreach (var he in exitConfirmModal.GetComponentsInChildren<HotkeyEvent>(true))
            {
                he.onHotkeyPress.RemoveAllListeners();
                he.onHotkeyPress.AddListener(OnCancelExitConfirm);
            }
        }

        // Exit Triggers (Single + Lists of any number of buttons)
        MichskyUIBridge.BindButton(null, heatExitTriggerButton, RequestDisconnect);

        if (exitTriggerButtons != null)
        {
            foreach (var btn in exitTriggerButtons)
            {
                if (btn != null) MichskyUIBridge.BindButton(btn, RequestDisconnect);
            }
        }

        if (boxExitTriggerButtons != null)
        {
            foreach (var btn in boxExitTriggerButtons)
            {
                if (btn != null) MichskyUIBridge.BindButton(btn, RequestDisconnect);
            }
        }

        if (standardExitTriggerButtons != null)
        {
            foreach (var btn in standardExitTriggerButtons)
            {
                if (btn != null) MichskyUIBridge.BindButton(btn, RequestDisconnect);
            }
        }
    }

    // =========================================================================
    //  Profile Visibility Helper
    // =========================================================================
    public void UpdateProfileUI()
    {
        bool hasName = PlayerNameManager.HasSavedName();
        if (profileSection != null)
        {
            profileSection.SetActive(hasName);
        }

        if (hasName && profileNameText != null)
        {
            profileNameText.text = PlayerNameManager.GetPlayerName();
        }

        if (hasName && profileLevelText != null)
        {
            int level = CloudCharacterSaveManager.Instance != null
                ? CloudCharacterSaveManager.Instance.GetPlayerLevel()
                : 1;
            string rank = CloudCharacterSaveManager.Instance != null
                ? CloudCharacterSaveManager.Instance.GetPlayerRankTitle(level)
                : "Recruit";
            profileLevelText.text = $"Lv. {level} • {rank}";
        }
    }

    // =========================================================================
    //  Exit / Leave Modal Actions
    // =========================================================================
    /// <summary>
    /// Prompts the Exit Window modal to confirm leaving or disconnecting.
    /// Can be called directly from UnityEvent on ANY button in the scene!
    /// </summary>
    public void RequestDisconnect()
    {
        if (lobbyCanvasRoot != null && !lobbyCanvasRoot.activeSelf)
        {
            lobbyCanvasRoot.SetActive(true);
        }

        if (exitConfirmModal != null)
        {
            if (!exitConfirmModal.gameObject.activeSelf)
                exitConfirmModal.gameObject.SetActive(true);
            exitConfirmModal.OpenWindow();
        }
        else
        {
            ConfirmDisconnect();
        }

        UnlockCursor();
    }

    /// <summary>
    /// Executes the actual disconnection and returns to the connection screen.
    /// </summary>
    public void ConfirmDisconnect()
    {
        if (exitConfirmModal != null)
        {
            exitConfirmModal.CloseWindow();
        }

        // 1. Clean up Character Selection 3D white room environment if active
        if (CharacterSceneController.Instance != null)
        {
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();
        }

        // 2. Hide post-reveal flow roots (Investigator & Girl flows)
        if (GirlRevealManager.Instance != null)
        {
            if (GirlRevealManager.Instance.investigatorFlow != null)
                GirlRevealManager.Instance.investigatorFlow.SetActive(false);
            if (GirlRevealManager.Instance.girlFlow != null)
                GirlRevealManager.Instance.girlFlow.SetActive(false);
        }

        // 3. Hide CharacterSelectUI if it was active
        var charSelect = FindFirstObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
        if (charSelect != null && charSelect.characterSelectPanel != null)
        {
            charSelect.characterSelectPanel.SetActive(false);
        }

        // 4. Hide GirlPlayerScreen and destroy dancing model if active
        var girlScreen = FindFirstObjectByType<GirlPlayerScreen>(FindObjectsInactive.Include);
        if (girlScreen != null)
        {
            girlScreen.Hide();
        }

        // 5. Restore camera to main Lobby establishing shot
        if (LobbyCameraController.Instance != null)
        {
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.Lobby);
        }

        // 6. Reset persistent role selection
        PersistentCharacterSelection.SetIsVengefulSpirit(false);

        // 7. Complete network shutdown and return to Home Panel
        OnDisconnect();
    }

    /// <summary>
    /// Cancels the exit confirmation modal and cleanly restores the player's active lobby view.
    /// </summary>
    public void OnCancelExitConfirm()
    {
        if (exitConfirmModal != null && exitConfirmModal.isOn)
        {
            exitConfirmModal.CloseWindow();
        }

        if (_activeView == ActiveView.HostLobby || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer))
        {
            if (hostLobbyPanel != null)
            {
                var hostModal = hostLobbyPanel.GetComponent<ModalWindowManager>();
                if (hostModal != null)
                {
                    if (!hostModal.isOn) hostModal.OpenWindow();
                }
                else
                {
                    hostLobbyPanel.SetActive(true);
                }
            }
        }
        else if (_activeView == ActiveView.ClientLobby || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient))
        {
            if (clientLobbyPanel != null)
            {
                var clientModal = clientLobbyPanel.GetComponent<ModalWindowManager>();
                if (clientModal != null)
                {
                    if (!clientModal.isOn) clientModal.OpenWindow();
                }
                else
                {
                    clientLobbyPanel.SetActive(true);
                }
            }
        }

        UnlockCursor();
    }

    // =========================================================================
    //  Name Entry Actions
    // =========================================================================
    private bool _isSanitizingInput = false;

    private char ValidateNameChar(string text, int charIndex, char addedChar)
    {
        if (!allowEmojisInName)
        {
            if (char.IsSurrogate(addedChar)) return '\0';
            if (PlayerNameManager.IsEmojiCodePoint((int)addedChar)) return '\0';
        }
        return addedChar;
    }

    private void OnNameInputChanged(string value)
    {
        if (_isSanitizingInput) return;

        if (!allowEmojisInName && PlayerNameManager.ContainsEmoji(value))
        {
            _isSanitizingInput = true;
            try
            {
                string stripped = PlayerNameManager.SanitizePlayerName(value, allowEmojis: false);
                MichskyUIBridge.SetInputText(null, heatNameEntryInputField, stripped);
                if (heatNameEntryInputField != null && heatNameEntryInputField.inputText != null)
                {
                    heatNameEntryInputField.inputText.caretPosition = stripped.Length;
                }

                if (nameEntryErrorText != null)
                {
                    nameEntryErrorText.text = "Emojis are not allowed in player names.";
                    nameEntryErrorText.gameObject.SetActive(true);
                }
            }
            finally
            {
                _isSanitizingInput = false;
            }
            return;
        }

        if (nameEntryErrorText != null && nameEntryErrorText.gameObject.activeSelf)
        {
            nameEntryErrorText.gameObject.SetActive(false);
        }
    }

    private void OnConfirmName()
    {
        string rawName = MichskyUIBridge.GetInputText(null, heatNameEntryInputField);

        if (!PlayerNameManager.ValidatePlayerName(rawName, out string sanitizedName, out string errorMessage, allowEmojisInName))
        {
            if (nameEntryErrorText != null)
            {
                nameEntryErrorText.text = errorMessage;
                nameEntryErrorText.gameObject.SetActive(true);
            }
            return;
        }

        PlayerNameManager.SetPlayerName(sanitizedName);
        UpdateProfileUI();

        if (nameEntryErrorText != null)
            nameEntryErrorText.gameObject.SetActive(false);

        if (heatNameEntryModal != null)
            heatNameEntryModal.CloseWindow();
        else if (nameEntryPanel != null)
            nameEntryPanel.SetActive(false);

        if (_pendingStartAction == PendingStartAction.Host)
        {
            _pendingStartAction = PendingStartAction.None;
            OnStartHost();
        }
        else if (_pendingStartAction == PendingStartAction.Client)
        {
            _pendingStartAction = PendingStartAction.None;
            OnStartClientChoice();
        }
        else
        {
            ShowConnectionPanel();
        }
    }

    /// <summary>
    /// Handles cancelling the Name Entry popup.
    /// If showExitConfirmOnNameCancel is true, opens the 'Are You Sure' exit modal.
    /// Otherwise, simply closes the Name Entry modal directly and returns to Home.
    /// </summary>
    public void OnCancelNameEntry()
    {
        if (showExitConfirmOnNameCancel)
        {
            RequestDisconnect();
            return;
        }

        _pendingStartAction = PendingStartAction.None;

        if (nameEntryErrorText != null)
            nameEntryErrorText.gameObject.SetActive(false);

        if (heatNameEntryModal != null && heatNameEntryModal.isOn)
            heatNameEntryModal.CloseWindow();
        else if (nameEntryPanel != null)
            nameEntryPanel.SetActive(false);

        ShowConnectionPanel();
    }

    // =========================================================================
    //  Connection Panel Actions
    // =========================================================================
    private async void OnStartHost()
    {
        if (!PlayerNameManager.HasSavedName())
        {
            _pendingStartAction = PendingStartAction.Host;
            ShowNameEntryPanel();
            return;
        }

        SetConnectionButtonsInteractable(false);

        if (networkMode == NetworkMode.LocalLAN)
        {
            ShowLoading($"Starting local host session ({localIpAddress}:{localPort})...");

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
                transport.SetConnectionData(localIpAddress, localPort);

            bool success = NetworkManager.Singleton.StartHost();
            HideLoading();

            if (success)
            {
                ShowHostLobby($"LOCAL LAN ({localIpAddress}:{localPort})");
            }
            else
            {
                Debug.LogWarning("[LobbyUI] Failed to start local host.");
                SetConnectionButtonsInteractable(true);
            }
        }
        else // Relay mode
        {
            EnsureRelayManager();
            ShowLoading("Connecting to Relay & generating lobby code...");

            string joinCode = await RelayManager.Instance.StartRelayHostAsync(maxPlayers);

            if (!string.IsNullOrEmpty(joinCode))
            {
                ShowLoading("Finalizing lobby session & determining player count...");
                await WaitForRelayConnectionAsync(10f);
                HideLoading();
                ShowHostLobby(joinCode);
                GirlRevealManager.Instance?.SubmitLocalPlayerName();
                RefreshLobbyPanels();
            }
            else
            {
                HideLoading();
                Debug.LogWarning("[LobbyUI] Failed to create Relay session. Check internet & dashboard.");
                SetConnectionButtonsInteractable(true);
            }
        }
    }

    private void OnStartClientChoice()
    {
        if (!PlayerNameManager.HasSavedName())
        {
            _pendingStartAction = PendingStartAction.Client;
            ShowNameEntryPanel();
            return;
        }

        if (networkMode == NetworkMode.LocalLAN)
        {
            ConnectLocalClient();
        }
        else
        {
            ShowJoinCodePanel();
        }
    }

    private void ConnectLocalClient()
    {
        SetConnectionButtonsInteractable(false);
        ShowLoading($"Connecting to local host at {localIpAddress}...");

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData(localIpAddress, localPort);

        bool success = NetworkManager.Singleton.StartClient();
        HideLoading();

        if (success)
        {
            ShowClientLobby($"LOCAL LAN ({localIpAddress})");
            GirlRevealManager.Instance?.SubmitLocalPlayerName();
        }
        else
        {
            Debug.LogWarning("[LobbyUI] Failed to connect to local host.");
            SetConnectionButtonsInteractable(true);
        }
    }

    // =========================================================================
    //  Join Code Panel Actions (Client)
    // =========================================================================
    private void OnJoinCodeInputChanged(string value)
    {
        HideJoinCodeError();
    }

    private async void OnSubmitJoinCode()
    {
        string code = MichskyUIBridge.GetInputText(null, heatJoinCodeInputField).Trim().ToUpper();

        if (string.IsNullOrWhiteSpace(code))
        {
            ShowJoinCodeError("Please enter a join code.");
            return;
        }

        if (code.Length < 6)
        {
            ShowJoinCodeError("Join code must be at least 6 characters.");
            return;
        }

        HideJoinCodeError();
        SetJoinCodeButtonsInteractable(false);
        ShowLoading($"Connecting to Relay room '{code}'...");

        EnsureRelayManager();
        bool success = await RelayManager.Instance.StartRelayClientAsync(code);

        if (success)
        {
            ShowLoading("Establishing session & determining player count...");
            bool connected = await WaitForRelayConnectionAsync(15f);
            HideLoading();

            if (connected)
            {
                ShowClientLobby(code);
                GirlRevealManager.Instance?.SubmitLocalPlayerName();
                RefreshLobbyPanels();
            }
            else
            {
                Debug.LogWarning("[LobbyUI] Connection timed out. Please check the code and try again.");
                ShowJoinCodeError("Connection timed out. Check the code and try again.");
                SetJoinCodeButtonsInteractable(true);
                if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            }
        }
        else
        {
            HideLoading();
            Debug.LogWarning("[LobbyUI] Failed to join. Check the code and try again.");
            ShowJoinCodeError("Invalid room code or room not found.");
            SetJoinCodeButtonsInteractable(true);
        }
    }

    private void ShowJoinCodeError(string message)
    {
        EnsureJoinCodeErrorText();

        if (joinCodeErrorText == null) return;

        var rt = joinCodeErrorText.rectTransform;
        if (rt != null)
        {
            // Ensure width is visible if it was set to 0 in inspector
            if (rt.sizeDelta.x <= 0f)
            {
                rt.sizeDelta = new Vector2(500f, rt.sizeDelta.y > 0 ? rt.sizeDelta.y : 40f);
            }

            // If anchors were left at default bottom-left (0,0), center it below the input field
            if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.zero && rt.anchoredPosition == Vector2.zero)
            {
                if (heatJoinCodeInputField != null)
                {
                    var inputRt = heatJoinCodeInputField.GetComponent<RectTransform>();
                    if (inputRt != null)
                    {
                        rt.anchorMin = inputRt.anchorMin;
                        rt.anchorMax = inputRt.anchorMax;
                        rt.pivot = inputRt.pivot;
                        rt.anchoredPosition = new Vector2(inputRt.anchoredPosition.x, inputRt.anchoredPosition.y - inputRt.rect.height / 2f - 25f);
                    }
                }
            }

            rt.SetAsLastSibling();
        }

        joinCodeErrorText.text = message;
        joinCodeErrorText.gameObject.SetActive(true);
    }

    private void HideJoinCodeError()
    {
        EnsureJoinCodeErrorText();

        if (joinCodeErrorText != null)
        {
            joinCodeErrorText.gameObject.SetActive(false);
        }
    }

    private void EnsureJoinCodeErrorText()
    {
        if (joinCodeErrorText == null && joinCodePanel != null)
        {
            var texts = joinCodePanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t.gameObject.name.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    joinCodeErrorText = t;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Waits asynchronously until NetworkManager confirms connection and initial player count.
    /// </summary>
    private async Task<bool> WaitForRelayConnectionAsync(float timeoutSeconds = 15f)
    {
        float elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            await Task.Delay(150);
            elapsed += 0.15f;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                await Task.Delay(250);
                return true;
            }
        }
        return false;
    }

    // =========================================================================
    //  Copy Join Code Action
    // =========================================================================
    private void OnCopyJoinCode()
    {
        string codeToCopy = string.Empty;

        if (RelayManager.Instance != null && !string.IsNullOrEmpty(RelayManager.Instance.CurrentJoinCode))
        {
            codeToCopy = RelayManager.Instance.CurrentJoinCode;
        }

        if (!string.IsNullOrEmpty(codeToCopy))
        {
            GUIUtility.systemCopyBuffer = codeToCopy;
            Debug.Log($"[LobbyUI] Copied join code '{codeToCopy}' to clipboard.");

            if (_copyFeedbackCoroutine != null)
                StopCoroutine(_copyFeedbackCoroutine);
            _copyFeedbackCoroutine = StartCoroutine(ShowCopyFeedbackRoutine());
        }
    }

    private IEnumerator ShowCopyFeedbackRoutine()
    {
        MichskyUIBridge.SetButtonText(heatHostCopyCodeButton, "COPIED! \u2713");

        yield return new WaitForSeconds(1.5f);

        MichskyUIBridge.SetButtonText(heatHostCopyCodeButton, "COPY CODE");
    }

    private void SetConnectionButtonsInteractable(bool interactable)
    {
        MichskyUIBridge.SetButtonInteractable(heatBoxStartHostButton, interactable);
        MichskyUIBridge.SetButtonInteractable(heatBoxStartClientButton, interactable);
    }

    private void SetJoinCodeButtonsInteractable(bool interactable)
    {
        MichskyUIBridge.SetButtonInteractable(heatJoinCodeSubmitButton, interactable);
        MichskyUIBridge.SetButtonInteractable(heatJoinCodeBackButton, interactable);
    }

    // =========================================================================
    //  Lobby Panel Actions
    // =========================================================================
    private void OnStartMatch()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        int currentCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (currentCount < minPlayers) return;

        HideLobbyUI();

        if (GirlRevealManager.Instance != null)
        {
            GirlRevealManager.Instance.BeginReveal();
        }
        else
        {
            Debug.LogWarning("[LobbyUI] GirlRevealManager not found. Loading game scene directly.");
            LoadGameSceneFallback();
        }
    }

    private void LoadGameSceneFallback()
    {
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.LoadScene(gameSceneName);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameSceneName);
        }
    }

    private void OnDisconnect()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.ConfigureLocalTransport(localIpAddress, localPort);
        }

        SetConnectionButtonsInteractable(true);
        SetJoinCodeButtonsInteractable(true);

        HideLoading();

        if (lobbyCanvasRoot != null) lobbyCanvasRoot.SetActive(true);
        else gameObject.SetActive(true);

        _pendingStartAction = PendingStartAction.None;
        UpdateProfileUI();
        ShowConnectionPanel();
    }

    // =========================================================================
    //  Lobby Refresh (called periodically)
    // =========================================================================
    private void RefreshLobbyPanels()
    {
        if (NetworkManager.Singleton == null) return;

        int  current  = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int  required = minPlayers;
        int  max      = Mathf.Max(maxPlayers, required);
        bool isServer = NetworkManager.Singleton.IsServer;
        bool canStart = isServer && current >= required;

        string countString = $"{current}  /  {max}  players";

        // Refresh Host Panel
        if (hostPlayerCountText != null) hostPlayerCountText.text = countString;
        if (hostStatusText != null)
        {
            hostStatusText.text = canStart
                ? "All players connected — ready to start!"
                : $"Waiting for {required - current} more player(s)...";
        }
        MichskyUIBridge.SetButtonInteractable(heatHostStartMatchButton, canStart);

        // Refresh Client Panel
        if (clientPlayerCountText != null) clientPlayerCountText.text = countString;
        if (clientStatusText != null)
        {
            clientStatusText.text = "Waiting for the host to start the match...";
        }
    }

    // =========================================================================
    //  Panel Visibility Helpers (Activates panels based on state & Relay mode)
    // =========================================================================
    public void ShowNameEntryPanel()
    {
        _activeView = ActiveView.NameEntry;
        if (nameEntryCancelButton != null)
        {
            nameEntryCancelButton.SetActive(true);
        }

        // Open modal popup smoothly without destroying/hiding the background Home Panel
        if (heatNameEntryModal != null)
        {
            heatNameEntryModal.OpenWindow();
        }
        else if (nameEntryPanel != null)
        {
            SetPanel(nameEntryPanel, true);
        }

        UnlockCursor();
    }

    public void ShowConnectionPanel()
    {
        _activeView = ActiveView.Home;
        _pendingStartAction = PendingStartAction.None;

        if (heatNameEntryModal != null && heatNameEntryModal.isOn)
        {
            heatNameEntryModal.CloseWindow();
        }
        if (nameEntryPanel != null && nameEntryPanel != (heatNameEntryModal != null ? heatNameEntryModal.gameObject : null))
        {
            nameEntryPanel.SetActive(false);
        }

        var joinCodeModal = joinCodePanel != null ? joinCodePanel.GetComponent<ModalWindowManager>() : null;
        if (joinCodeModal != null && joinCodeModal.isOn)
        {
            joinCodeModal.CloseWindow();
        }
        else if (joinCodePanel != null)
        {
            joinCodePanel.SetActive(false);
        }

        SetPanel(hostLobbyPanel,   false);
        SetPanel(clientLobbyPanel, false);

        // Ensure Home Panel is active and visible
        if (connectionPanel != null)
        {
            connectionPanel.SetActive(true);

            var cg = connectionPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            // Ensure Box Container and cards hierarchy are active
            var boxContainer = connectionPanel.transform.Find("Content/Panel Content/Box Container");
            if (boxContainer != null)
            {
                boxContainer.gameObject.SetActive(true);
                for (int i = 0; i < boxContainer.childCount; i++)
                {
                    boxContainer.GetChild(i).gameObject.SetActive(true);
                }
            }
        }

        // Always ensure Host and Join/Lobby cards are enabled and interactable
        if (heatBoxStartHostButton != null)
        {
            heatBoxStartHostButton.gameObject.SetActive(true);
            heatBoxStartHostButton.isInteractable = true;
            heatBoxStartHostButton.UpdateUI();
        }
        if (heatBoxStartClientButton != null)
        {
            heatBoxStartClientButton.gameObject.SetActive(true);
            heatBoxStartClientButton.isInteractable = true;
            heatBoxStartClientButton.UpdateUI();
        }

        // Force PanelManager to re-open Home Panel, trigger fade-in, and re-enable hotkeys/gamepad safely
        if (mainPanelManager != null && mainPanelManager.panels != null && mainPanelManager.panels.Count > 0)
        {
            if (mainPanelManager.currentPanelIndex == 0)
            {
                mainPanelManager.ShowCurrentPanel();
                if (mainPanelManager.panels[0].hotkeyParent != null && mainPanelManager.panels[0].hotkeys != null)
                {
                    foreach (var he in mainPanelManager.panels[0].hotkeys)
                    {
                        if (he != null) he.enabled = true;
                    }
                }
            }
            else
            {
                mainPanelManager.OpenPanelByIndex(0);
            }
        }

        SetConnectionButtonsInteractable(true);
        UnlockCursor();

        UpdateProfileUI();
        UpdateCreditsUI();
    }

    public void ShowJoinCodePanel()
    {
        _activeView = ActiveView.JoinCode;
        SetPanel(nameEntryPanel,   false);

        // If joinCodePanel has a ModalWindowManager, it overlays on top of Home without destroying it
        var mw = joinCodePanel != null ? joinCodePanel.GetComponent<ModalWindowManager>() : null;
        if (mw == null)
        {
            SetPanel(connectionPanel, false);
        }

        SetPanel(joinCodePanel,    true);
        SetPanel(hostLobbyPanel,   false);
        SetPanel(clientLobbyPanel, false);

        SetJoinCodeButtonsInteractable(true);
        MichskyUIBridge.SetInputText(null, heatJoinCodeInputField, string.Empty);
        HideJoinCodeError();

        UnlockCursor();
    }

    private void ShowHostLobby(string codeOrMode)
    {
        _activeView = ActiveView.HostLobby;
        SetPanel(nameEntryPanel,   false);
        SetPanel(connectionPanel,  false);
        SetPanel(joinCodePanel,    false);
        SetPanel(hostLobbyPanel,   true);
        SetPanel(clientLobbyPanel, false);

        if (hostLobbyPanel != null)
        {
            foreach (var he in hostLobbyPanel.GetComponentsInChildren<HotkeyEvent>(true))
            {
                he.enabled = false;
            }
        }

        if (hostJoinCodeText != null)
        {
            hostJoinCodeText.text = networkMode == NetworkMode.Relay
                ? $"JOIN CODE: {codeOrMode}"
                : $"MODE: {codeOrMode}";
        }

        if (heatHostCopyCodeButton != null)
        {
            heatHostCopyCodeButton.gameObject.SetActive(networkMode == NetworkMode.Relay);
        }

        UnlockCursor();
    }

    private void ShowClientLobby(string codeOrMode)
    {
        _activeView = ActiveView.ClientLobby;
        SetPanel(nameEntryPanel,   false);
        SetPanel(connectionPanel,  false);
        SetPanel(joinCodePanel,    false);
        SetPanel(hostLobbyPanel,   false);
        SetPanel(clientLobbyPanel, true);

        if (clientLobbyPanel != null)
        {
            foreach (var he in clientLobbyPanel.GetComponentsInChildren<HotkeyEvent>(true))
            {
                he.enabled = false;
            }
        }

        if (clientJoinCodeText != null)
        {
            clientJoinCodeText.text = networkMode == NetworkMode.Relay
                ? $"ROOM CODE: {codeOrMode}"
                : $"MODE: {codeOrMode}";
        }

        UnlockCursor();
    }

    public void ShowLoading(string reason)
    {
        SetPanel(loadingOverlayPanel, true);
        if (loadingSpinner != null)
        {
            if (spinSpinnerInCode) loadingSpinner.transform.localRotation = Quaternion.identity;
            loadingSpinner.SetActive(true);
        }
        if (loadingStatusText != null)
        {
            loadingStatusText.text = reason;
            loadingStatusText.gameObject.SetActive(true);
        }
    }

    public void HideLoading()
    {
        SetPanel(loadingOverlayPanel, false);
        if (loadingSpinner != null) loadingSpinner.SetActive(false);
        if (loadingStatusText != null) loadingStatusText.gameObject.SetActive(false);
    }

    public void HideLobbyUI()
    {
        _isHidden = true;
        HideLoading();
        SetPanel(nameEntryPanel,   false);
        SetPanel(connectionPanel,  false);
        SetPanel(joinCodePanel,    false);
        SetPanel(hostLobbyPanel,   false);
        SetPanel(clientLobbyPanel, false);

        if (lobbyCanvasRoot != null)
        {
            lobbyCanvasRoot.SetActive(false);
        }
        else
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null) gameObject.SetActive(false);
        }
    }

    private static void SetPanel(GameObject panel, bool visible)
    {
        if (panel == null) return;
        var mw = panel.GetComponent<ModalWindowManager>();
        if (mw != null)
        {
            if (visible) mw.OpenWindow();
            else mw.CloseWindow();
        }
        else
        {
            panel.SetActive(visible);
        }
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }
}
