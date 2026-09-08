using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Manages the pre-game lobby UI flow.
///
/// Supports dedicated screens:
///   1. Name Entry Panel     — Name prompt shown first.
///   2. Connection Panel     — Main menu to choose Host or Join.
///   3. Join Code Panel      — Dedicated client screen to type/paste a Join Code.
///   4. Host Lobby Panel     — Dedicated host screen displaying the generated Join Code, copy button, and match controls.
///   5. Client Lobby Panel   — Dedicated client waiting room showing the room code, player count, and host status.
///   6. Fallback Lobby Panel — Classic unified lobby panel (used in LocalLAN mode or if dedicated panels are unassigned).
///
/// Also supports instant Inspector toggle between Relay and Local LAN for dev testing.
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

    [Tooltip("Input field where the player types their name.")]
    public TMP_InputField nameEntryInputField;

    [Tooltip("Placeholder text inside the input field (e.g. 'Enter your name...')")]
    public TextMeshProUGUI nameEntryPlaceholder;

    [Tooltip("Error/hint label shown when the player tries to confirm with an empty name.")]
    public TextMeshProUGUI nameEntryErrorText;

    [Tooltip("Button that confirms the entered name and advances to the Connection Panel.")]
    public Button nameConfirmButton;

    // -------------------------------------------------------------------------
    //  Inspector — 2. Main Connection Panel (Host / Join Choice)
    // -------------------------------------------------------------------------
    [Header("2. Connection Panel  ← Main menu to choose Host or Join")]
    [Tooltip("Root panel shown after name confirmation (Host / Join choice screen).")]
    public GameObject connectionPanel;

    [Tooltip("Displays the currently saved player name on the connection screen (optional).")]
    public TextMeshProUGUI connectionPlayerNameLabel;

    [Tooltip("Button that starts a Host session.")]
    public Button startHostButton;

    [Tooltip("Button that navigates to the Join Code panel (or connects directly if LocalLAN).")]
    public Button startClientButton;

    [Tooltip("Status or error message on connection screen.")]
    public TextMeshProUGUI connectionStatusText;

    [Tooltip("Decorative title text on the connection screen, e.g. 'NIGHT CRAWLER'.")]
    public TextMeshProUGUI gameTitleText;

    [Tooltip("Subtitle / tagline beneath the title, e.g. 'Survive the dark.'")]
    public TextMeshProUGUI taglineText;

    // -------------------------------------------------------------------------
    //  Inspector — 3. Dedicated Client Join Code Panel
    // -------------------------------------------------------------------------
    [Header("3. Join Code Panel (Client)  ← Dedicated screen to enter code")]
    [Tooltip("Dedicated panel where client enters the Join Code. Opened when clicking 'Join Game' in Relay mode.")]
    public GameObject joinCodePanel;

    [Tooltip("Input field where client enters / pastes the 6-character Join Code.")]
    public TMP_InputField joinCodeInputField;

    [Tooltip("Button inside the Join Code panel that connects to the Relay room.")]
    public Button joinCodeSubmitButton;

    [Tooltip("Button inside the Join Code panel that goes back to the main Connection screen.")]
    public Button joinCodeBackButton;

    [Tooltip("Status or error message inside the Join Code panel (e.g. 'Connecting...', 'Invalid Code').")]
    public TextMeshProUGUI joinCodeStatusText;

    // -------------------------------------------------------------------------
    //  Inspector — 4. Dedicated Host Lobby Panel
    // -------------------------------------------------------------------------
    [Header("4. Host Lobby Panel  ← Dedicated screen for Host with code & start")]
    [Tooltip("Dedicated panel shown to the host after creating a room. (Falls back to unified Lobby Panel if unassigned).")]
    public GameObject hostLobbyPanel;

    [Tooltip("Displays the generated Join Code on the host panel (e.g. 'LOBBY CODE: A9F3X2').")]
    public TextMeshProUGUI hostJoinCodeText;

    [Tooltip("Button that copies the session Join Code to the host's clipboard.")]
    public Button hostCopyCodeButton;

    [Tooltip("Feedback label showing 'Copied to clipboard!'.")]
    public TextMeshProUGUI hostCopyFeedbackText;

    [Tooltip("Displays connected player count on the host panel, e.g. '2 / 6 players'.")]
    public TextMeshProUGUI hostPlayerCountText;

    [Tooltip("Status message on host panel, e.g. 'Waiting for players...' or 'Ready to start!'.")]
    public TextMeshProUGUI hostStatusText;

    [Tooltip("'START MATCH' button — active when enough players are connected.")]
    public Button hostStartMatchButton;

    [Tooltip("Button that lets the host cancel and return to the connection screen.")]
    public Button hostDisconnectButton;

    // -------------------------------------------------------------------------
    //  Inspector — 5. Dedicated Client Lobby Panel (Waiting Room)
    // -------------------------------------------------------------------------
    [Header("5. Client Lobby Panel  ← Dedicated waiting room for Clients")]
    [Tooltip("Dedicated panel shown to clients while waiting for the host. (Falls back to unified Lobby Panel if unassigned).")]
    public GameObject clientLobbyPanel;

    [Tooltip("Displays connected room code to client (e.g. 'ROOM: A9F3X2').")]
    public TextMeshProUGUI clientJoinCodeText;

    [Tooltip("Displays connected player count on client panel.")]
    public TextMeshProUGUI clientPlayerCountText;

    [Tooltip("Status message shown to client, e.g. 'Waiting for host to start...'")]
    public TextMeshProUGUI clientStatusText;

    [Tooltip("Button that lets client leave and return to the connection screen.")]
    public Button clientDisconnectButton;

    // -------------------------------------------------------------------------
    //  Inspector — Fallback Unified Lobby Panel (for backward compatibility)
    // -------------------------------------------------------------------------
    [Header("Fallback Unified Lobby Panel (Optional)")]
    [Tooltip("Single unified panel used if Host/Client dedicated panels are not assigned.")]
    public GameObject lobbyPanel;

    [Tooltip("Displays connected player count on the fallback lobby panel.")]
    public TextMeshProUGUI playerCountText;

    [Tooltip("Status message on the fallback lobby panel.")]
    public TextMeshProUGUI statusText;

    [Tooltip("Join code text on the fallback lobby panel.")]
    public TextMeshProUGUI lobbyJoinCodeText;

    [Tooltip("Copy code button on the fallback lobby panel.")]
    public Button copyJoinCodeButton;

    [Tooltip("Copy feedback text on the fallback lobby panel.")]
    public TextMeshProUGUI copyFeedbackText;

    [Tooltip("Host only elements wrapper on fallback lobby panel.")]
    public GameObject hostOnlyElements;

    [Tooltip("Start match button on fallback lobby panel.")]
    public Button startMatchButton;

    [Tooltip("Disconnect button on fallback lobby panel.")]
    public Button disconnectButton;

    // -------------------------------------------------------------------------
    //  Inspector — Match & Scene Settings
    // -------------------------------------------------------------------------
    [Header("Match & Scene Settings")]
    [Tooltip("Minimum connected players required to enable 'START MATCH'. Set to 1 for solo testing, or 2+ for multiplayer builds.")]
    public int minPlayers = 1;

    [Tooltip("Maximum allowed players in the lobby (e.g. 6: 1 Vengeful Spirit + 5 Investigators).")]
    public int maxPlayers = 6;

    [Tooltip("The name of the Game Scene containing GameManager and map spawn points.")]
    public string gameSceneName = "GameScene";

    [Header("Shared")]
    [Tooltip("Optional animated background element (e.g. a pulsing vignette image).")]
    public GameObject animatedBackground;

    public static LobbyUI Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private float _refreshInterval = 0.5f;
    private float _refreshTimer;
    private bool  _isHidden = false;
    private Coroutine _copyFeedbackCoroutine;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        Instance = this;
        EnsureRelayManager();
    }

    void Start()
    {
        WireButtonListeners();

        if (nameEntryInputField != null)
            nameEntryInputField.text = PlayerNameManager.GetPlayerName();

        if (nameEntryErrorText != null)
            nameEntryErrorText.gameObject.SetActive(false);

        if (connectionStatusText != null)
            connectionStatusText.gameObject.SetActive(false);

        if (joinCodeStatusText != null)
            joinCodeStatusText.gameObject.SetActive(false);

        if (hostCopyFeedbackText != null)
            hostCopyFeedbackText.gameObject.SetActive(false);

        if (copyFeedbackText != null)
            copyFeedbackText.gameObject.SetActive(false);

        // If player already has a saved name, skip straight to connection screen
        if (PlayerNameManager.HasSavedName())
            ShowConnectionPanel();
        else
            ShowNameEntryPanel();
    }

    void Update()
    {
        if (_isHidden) return;

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer > 0f) return;
        _refreshTimer = _refreshInterval;

        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            RefreshLobbyPanels();
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
        if (nameConfirmButton      != null) nameConfirmButton.onClick.AddListener(OnConfirmName);

        // 2. Connection Panel
        if (startHostButton        != null) startHostButton.onClick.AddListener(OnStartHost);
        if (startClientButton      != null) startClientButton.onClick.AddListener(OnStartClientChoice);

        // 3. Join Code Panel (Client)
        if (joinCodeSubmitButton   != null) joinCodeSubmitButton.onClick.AddListener(OnSubmitJoinCode);
        if (joinCodeBackButton     != null) joinCodeBackButton.onClick.AddListener(ShowConnectionPanel);

        // 4. Host Lobby Panel
        if (hostStartMatchButton   != null) hostStartMatchButton.onClick.AddListener(OnStartMatch);
        if (hostDisconnectButton   != null) hostDisconnectButton.onClick.AddListener(OnDisconnect);
        if (hostCopyCodeButton     != null) hostCopyCodeButton.onClick.AddListener(OnCopyJoinCode);

        // 5. Client Lobby Panel
        if (clientDisconnectButton != null) clientDisconnectButton.onClick.AddListener(OnDisconnect);

        // Fallback Lobby Panel
        if (startMatchButton       != null) startMatchButton.onClick.AddListener(OnStartMatch);
        if (disconnectButton       != null) disconnectButton.onClick.AddListener(OnDisconnect);
        if (copyJoinCodeButton     != null) copyJoinCodeButton.onClick.AddListener(OnCopyJoinCode);
    }

    // =========================================================================
    //  Name Entry Actions
    // =========================================================================
    private void OnConfirmName()
    {
        string enteredName = nameEntryInputField != null ? nameEntryInputField.text.Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(enteredName))
        {
            if (nameEntryErrorText != null)
            {
                nameEntryErrorText.text = "Please enter a name before continuing.";
                nameEntryErrorText.gameObject.SetActive(true);
            }
            return;
        }

        PlayerNameManager.SetPlayerName(enteredName);

        if (nameEntryErrorText != null)
            nameEntryErrorText.gameObject.SetActive(false);

        ShowConnectionPanel();
    }

    // =========================================================================
    //  Connection Panel Actions
    // =========================================================================
    private async void OnStartHost()
    {
        SetConnectionButtonsInteractable(false);
        ShowConnectionStatus("Preparing host session...", false);

        if (networkMode == NetworkMode.LocalLAN)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
                transport.SetConnectionData(localIpAddress, localPort);

            bool success = NetworkManager.Singleton.StartHost();
            if (success)
            {
                ShowHostLobby($"LOCAL LAN ({localIpAddress}:{localPort})");
            }
            else
            {
                ShowConnectionStatus("Failed to start local host.", true);
                SetConnectionButtonsInteractable(true);
            }
        }
        else // Relay mode
        {
            EnsureRelayManager();
            ShowConnectionStatus("Connecting to Relay & allocating session...", false);

            string joinCode = await RelayManager.Instance.StartRelayHostAsync(maxPlayers);
            if (!string.IsNullOrEmpty(joinCode))
            {
                ShowHostLobby(joinCode);
            }
            else
            {
                ShowConnectionStatus("Failed to create Relay session. Check internet & dashboard.", true);
                SetConnectionButtonsInteractable(true);
            }
        }
    }

    private void OnStartClientChoice()
    {
        if (networkMode == NetworkMode.LocalLAN)
        {
            // In LocalLAN, join local host directly without needing a join code
            ConnectLocalClient();
        }
        else
        {
            // In Relay mode:
            // If the user has assigned a dedicated Join Code panel, open it!
            if (joinCodePanel != null)
            {
                ShowJoinCodePanel();
            }
            else if (joinCodeInputField != null)
            {
                // If the input field is directly on the connection panel, connect with it
                OnSubmitJoinCode();
            }
            else
            {
                // Fallback: prompt for Join Code panel
                ShowJoinCodePanel();
            }
        }
    }

    private void ConnectLocalClient()
    {
        SetConnectionButtonsInteractable(false);
        ShowConnectionStatus($"Connecting to local host at {localIpAddress}...", false);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData(localIpAddress, localPort);

        bool success = NetworkManager.Singleton.StartClient();
        if (success)
        {
            ShowClientLobby($"LOCAL LAN ({localIpAddress})");
        }
        else
        {
            ShowConnectionStatus("Failed to connect to local host.", true);
            SetConnectionButtonsInteractable(true);
        }
    }

    // =========================================================================
    //  Join Code Panel Actions (Client)
    // =========================================================================
    private async void OnSubmitJoinCode()
    {
        string code = joinCodeInputField != null ? joinCodeInputField.text.Trim().ToUpper() : string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            ShowJoinCodeStatus("Please enter a 6-character Join Code.", true);
            return;
        }

        SetJoinCodeButtonsInteractable(false);
        ShowJoinCodeStatus($"Connecting to Relay room '{code}'...", false);

        EnsureRelayManager();
        bool success = await RelayManager.Instance.StartRelayClientAsync(code);

        if (success)
        {
            ShowClientLobby(code);
        }
        else
        {
            ShowJoinCodeStatus("Failed to join. Check the code and try again.", true);
            SetJoinCodeButtonsInteractable(true);
        }
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
        else if (joinCodeInputField != null && !string.IsNullOrEmpty(joinCodeInputField.text))
        {
            codeToCopy = joinCodeInputField.text.Trim().ToUpper();
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
        if (hostCopyFeedbackText != null)
        {
            hostCopyFeedbackText.text = "Copied to clipboard!";
            hostCopyFeedbackText.gameObject.SetActive(true);
        }
        if (copyFeedbackText != null)
        {
            copyFeedbackText.text = "Copied to clipboard!";
            copyFeedbackText.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(2.0f);

        if (hostCopyFeedbackText != null) hostCopyFeedbackText.gameObject.SetActive(false);
        if (copyFeedbackText     != null) copyFeedbackText.gameObject.SetActive(false);
    }

    private void ShowConnectionStatus(string message, bool isError)
    {
        if (connectionStatusText != null)
        {
            connectionStatusText.text = message;
            connectionStatusText.color = isError ? new Color(1f, 0.35f, 0.35f) : Color.white;
            connectionStatusText.gameObject.SetActive(true);
        }
    }

    private void ShowJoinCodeStatus(string message, bool isError)
    {
        if (joinCodeStatusText != null)
        {
            joinCodeStatusText.text = message;
            joinCodeStatusText.color = isError ? new Color(1f, 0.35f, 0.35f) : Color.white;
            joinCodeStatusText.gameObject.SetActive(true);
        }
        else
        {
            ShowConnectionStatus(message, isError);
        }
    }

    private void SetConnectionButtonsInteractable(bool interactable)
    {
        if (startHostButton   != null) startHostButton.interactable = interactable;
        if (startClientButton != null) startClientButton.interactable = interactable;
    }

    private void SetJoinCodeButtonsInteractable(bool interactable)
    {
        if (joinCodeSubmitButton != null) joinCodeSubmitButton.interactable = interactable;
        if (joinCodeBackButton   != null) joinCodeBackButton.interactable = interactable;
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

        if (connectionStatusText != null) connectionStatusText.gameObject.SetActive(false);
        if (joinCodeStatusText   != null) joinCodeStatusText.gameObject.SetActive(false);

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
        if (hostStartMatchButton != null) hostStartMatchButton.interactable = canStart;

        // Refresh Client Panel
        if (clientPlayerCountText != null) clientPlayerCountText.text = countString;
        if (clientStatusText != null)
        {
            clientStatusText.text = "Waiting for the host to start the match...";
        }

        // Refresh Fallback Unified Panel
        if (playerCountText != null) playerCountText.text = countString;
        if (statusText != null)
        {
            if (isServer)
                statusText.text = canStart ? "All players connected — ready to start!" : $"Waiting for {required - current} more player(s)...";
            else
                statusText.text = "Waiting for the host to start the match...";
        }
        if (hostOnlyElements != null) hostOnlyElements.SetActive(isServer);
        if (startMatchButton != null) startMatchButton.interactable = canStart;
    }

    // =========================================================================
    //  Panel Visibility Helpers (Activates panels based on state & Relay mode)
    // =========================================================================
    public void ShowNameEntryPanel()
    {
        SetPanel(nameEntryPanel,      true);
        SetPanel(connectionPanel,     false);
        SetPanel(joinCodePanel,       false);
        SetPanel(hostLobbyPanel,      false);
        SetPanel(clientLobbyPanel,    false);
        SetPanel(lobbyPanel,          false);
        UnlockCursor();
    }

    public void ShowConnectionPanel()
    {
        SetPanel(nameEntryPanel,      false);
        SetPanel(connectionPanel,     true);
        SetPanel(joinCodePanel,       false);
        SetPanel(hostLobbyPanel,      false);
        SetPanel(clientLobbyPanel,    false);
        SetPanel(lobbyPanel,          false);

        SetConnectionButtonsInteractable(true);
        UnlockCursor();

        if (connectionPlayerNameLabel != null)
            connectionPlayerNameLabel.text = PlayerNameManager.GetPlayerName();
    }

    public void ShowJoinCodePanel()
    {
        SetPanel(nameEntryPanel,      false);
        SetPanel(connectionPanel,     false);
        SetPanel(joinCodePanel,       true);
        SetPanel(hostLobbyPanel,      false);
        SetPanel(clientLobbyPanel,    false);
        SetPanel(lobbyPanel,          false);

        SetJoinCodeButtonsInteractable(true);
        if (joinCodeStatusText != null)
            joinCodeStatusText.gameObject.SetActive(false);

        if (joinCodeInputField != null)
        {
            joinCodeInputField.text = string.Empty;
            joinCodeInputField.Select();
            joinCodeInputField.ActivateInputField();
        }

        UnlockCursor();
    }

    private void ShowHostLobby(string codeOrMode)
    {
        SetPanel(nameEntryPanel,      false);
        SetPanel(connectionPanel,     false);
        SetPanel(joinCodePanel,       false);

        bool hasDedicatedHost = hostLobbyPanel != null;
        SetPanel(hostLobbyPanel,      hasDedicatedHost);
        SetPanel(clientLobbyPanel,    false);
        SetPanel(lobbyPanel,          !hasDedicatedHost);

        if (hostJoinCodeText != null)
        {
            hostJoinCodeText.text = networkMode == NetworkMode.Relay
                ? $"JOIN CODE: {codeOrMode}"
                : $"MODE: {codeOrMode}";
        }

        if (hostCopyCodeButton != null)
        {
            hostCopyCodeButton.gameObject.SetActive(networkMode == NetworkMode.Relay);
        }

        // Fallback panel support
        if (lobbyJoinCodeText != null)
        {
            lobbyJoinCodeText.text = networkMode == NetworkMode.Relay
                ? $"JOIN CODE: {codeOrMode}"
                : $"MODE: {codeOrMode}";
        }
        if (copyJoinCodeButton != null)
        {
            copyJoinCodeButton.gameObject.SetActive(networkMode == NetworkMode.Relay);
        }

        UnlockCursor();
    }

    private void ShowClientLobby(string codeOrMode)
    {
        SetPanel(nameEntryPanel,      false);
        SetPanel(connectionPanel,     false);
        SetPanel(joinCodePanel,       false);

        bool hasDedicatedClient = clientLobbyPanel != null;
        SetPanel(clientLobbyPanel,    hasDedicatedClient);
        SetPanel(hostLobbyPanel,      false);
        SetPanel(lobbyPanel,          !hasDedicatedClient);

        if (clientJoinCodeText != null)
        {
            clientJoinCodeText.text = networkMode == NetworkMode.Relay
                ? $"ROOM CODE: {codeOrMode}"
                : $"MODE: {codeOrMode}";
        }

        if (lobbyJoinCodeText != null)
        {
            lobbyJoinCodeText.text = networkMode == NetworkMode.Relay
                ? $"ROOM CODE: {codeOrMode}"
                : $"MODE: {codeOrMode}";
        }

        UnlockCursor();
    }

    public void HideLobbyUI()
    {
        _isHidden = true;
        SetPanel(nameEntryPanel,      false);
        SetPanel(connectionPanel,     false);
        SetPanel(joinCodePanel,       false);
        SetPanel(hostLobbyPanel,      false);
        SetPanel(clientLobbyPanel,    false);
        SetPanel(lobbyPanel,          false);
        SetPanel(animatedBackground,  false);
    }

    private static void SetPanel(GameObject panel, bool visible)
    {
        if (panel != null) panel.SetActive(visible);
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }
}
