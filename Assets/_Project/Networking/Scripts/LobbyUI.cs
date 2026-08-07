using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Manages only the pre-game lobby UI flow.
///
/// Flow:
///   1. Name Entry Panel — shown first if no player name is saved.
///      Player types a name and presses CONFIRM. Saved to PlayerPrefs.
///   2. Connection Panel — shown after name is confirmed. Host or Client choice.
///   3. Lobby Panel      — shown after connecting, while waiting for players.
///
/// Setup:
///   1. Create a Canvas (Screen Space – Overlay, Sort Order 10).
///   2. Build three child panels: Name Entry Panel, Connection Panel, Lobby Panel.
///   3. Drag this script onto the Canvas root and wire all fields below.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — Name Entry Panel
    //  Shown FIRST if no player name is saved yet
    // -------------------------------------------------------------------------
    [Header("Name Entry Panel  ← Shown first if no name saved")]
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
    //  Inspector — Connection Panel
    //  Shown after name is confirmed, before Host / Client choice
    // -------------------------------------------------------------------------
    [Header("Connection Panel")]
    [Tooltip("Root panel shown before a connection is made (Host / Client choice screen).")]
    public GameObject connectionPanel;

    [Tooltip("Displays the currently saved player name on the connection screen (optional).")]
    public TextMeshProUGUI connectionPlayerNameLabel;

    [Tooltip("Button that starts a Host session.")]
    public Button startHostButton;

    [Tooltip("Button that starts a Client session and joins the host.")]
    public Button startClientButton;

    [Tooltip("Decorative title text on the connection screen, e.g. 'NIGHT CRAWLER'.")]
    public TextMeshProUGUI gameTitleText;

    [Tooltip("Subtitle / tagline beneath the title, e.g. 'Survive the dark.'")]
    public TextMeshProUGUI taglineText;

    // -------------------------------------------------------------------------
    //  Inspector — Lobby Panel
    //  Shown after the player connects, while waiting for enough players
    // -------------------------------------------------------------------------
    [Header("Lobby Panel")]
    [Tooltip("Root panel shown while waiting in the lobby after connecting.")]
    public GameObject lobbyPanel;

    [Tooltip("Displays connected player count, e.g. '2 / 4 players'.")]
    public TextMeshProUGUI playerCountText;

    [Tooltip("Status message shown to all clients, e.g. 'Waiting for host...' or 'Ready!'")]
    public TextMeshProUGUI statusText;

    [Tooltip("Parent object that wraps everything only the host should see (Start button, etc.).")]
    public GameObject hostOnlyElements;

    [Tooltip("'START MATCH' button — visible only to the host when enough players are connected.")]
    public Button startMatchButton;

    [Tooltip("Button that lets any player disconnect and return to the connection screen.")]
    public Button disconnectButton;

    // -------------------------------------------------------------------------
    //  Inspector — Shared / Cosmetic / Match
    // -------------------------------------------------------------------------
    [Header("Match & Scene Settings")]
    [Tooltip("Minimum connected players required to enable 'START MATCH'. Set to 1 for solo testing, or 2+ for multiplayer builds.")]
    public int minPlayers = 1;

    [Tooltip("The name of the Game Scene containing GameManager and map spawn points.")]
    public string gameSceneName = "GameScene";

    [Header("Shared")]
    [Tooltip("Optional animated background element (e.g. a pulsing vignette image).")]
    public GameObject animatedBackground;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private float _refreshInterval = 0.5f;
    private float _refreshTimer;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Start()
    {
        WireButtonListeners();

        // Populate name field with saved name if one exists
        if (nameEntryInputField != null)
            nameEntryInputField.text = PlayerNameManager.GetPlayerName();

        if (nameEntryErrorText != null)
            nameEntryErrorText.gameObject.SetActive(false);

        // If player already has a saved name, skip straight to the connection screen
        if (PlayerNameManager.HasSavedName())
            ShowConnectionPanel();
        else
            ShowNameEntryPanel();
    }

    void Update()
    {
        // Only refresh lobby state periodically — not every frame
        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer > 0f) return;
        _refreshTimer = _refreshInterval;

        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            RefreshLobbyPanel();
    }

    // =========================================================================
    //  Button Wiring
    // =========================================================================
    private void WireButtonListeners()
    {
        if (nameConfirmButton  != null) nameConfirmButton.onClick.AddListener(OnConfirmName);
        if (startHostButton    != null) startHostButton.onClick.AddListener(OnStartHost);
        if (startClientButton  != null) startClientButton.onClick.AddListener(OnStartClient);
        if (startMatchButton   != null) startMatchButton.onClick.AddListener(OnStartMatch);
        if (disconnectButton   != null) disconnectButton.onClick.AddListener(OnDisconnect);
    }

    // =========================================================================
    //  Name Entry Actions
    // =========================================================================
    private void OnConfirmName()
    {
        string enteredName = nameEntryInputField != null ? nameEntryInputField.text.Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(enteredName))
        {
            // Show error — don't proceed
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
    private void OnStartHost()
    {
        NetworkManager.Singleton.StartHost();
        ShowLobbyPanel();
    }

    private void OnStartClient()
    {
        NetworkManager.Singleton.StartClient();
        ShowLobbyPanel();
    }

    // =========================================================================
    //  Lobby Panel Actions
    // =========================================================================
    private void OnStartMatch()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        int currentCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (currentCount < minPlayers) return;

        // Hide lobby UI — the reveal and character selection screens take over
        HideLobbyUI();

        // Route through the reveal flow so all players experience the
        // slot-machine spirit selection, character select, and squad lineup
        // before the game scene loads.
        if (GirlRevealManager.Instance != null)
        {
            GirlRevealManager.Instance.BeginReveal();
        }
        else
        {
            // Fallback: GirlRevealManager not present — load scene directly
            Debug.LogWarning("[LobbyUI] GirlRevealManager not found. Loading game scene directly.");
            LoadGameSceneFallback();
        }
    }

    /// <summary>
    /// Legacy direct scene load — used only when GirlRevealManager is absent.
    /// </summary>
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
        NetworkManager.Singleton.Shutdown();
        ShowConnectionPanel();
    }

    // =========================================================================
    //  Lobby Refresh (called every 0.5 s)
    // =========================================================================
    private void RefreshLobbyPanel()
    {
        if (NetworkManager.Singleton == null) return;

        int  current  = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int  required = minPlayers;
        bool isServer = NetworkManager.Singleton.IsServer;
        bool canStart = isServer && current >= required;

        if (playerCountText != null)
            playerCountText.text = $"{current}  /  {required}  players";

        if (statusText != null)
        {
            if (isServer)
                statusText.text = canStart ? "All players connected — ready to start!" : $"Waiting for {required - current} more player(s)...";
            else
                statusText.text = "Waiting for the host to start the match...";
        }

        if (hostOnlyElements != null)
            hostOnlyElements.SetActive(isServer);

        if (startMatchButton != null)
            startMatchButton.interactable = canStart;
    }

    // =========================================================================
    //  Panel Visibility Helpers
    // =========================================================================
    private void ShowNameEntryPanel()
    {
        SetPanel(nameEntryPanel,   true);
        SetPanel(connectionPanel,  false);
        SetPanel(lobbyPanel,       false);
        UnlockCursor();
    }

    private void ShowConnectionPanel()
    {
        SetPanel(nameEntryPanel,   false);
        SetPanel(connectionPanel,  true);
        SetPanel(lobbyPanel,       false);
        UnlockCursor();

        // Update the connection panel's name label with the confirmed name
        if (connectionPlayerNameLabel != null)
            connectionPlayerNameLabel.text = PlayerNameManager.GetPlayerName();
    }

    private void ShowLobbyPanel()
    {
        SetPanel(nameEntryPanel,   false);
        SetPanel(connectionPanel,  false);
        SetPanel(lobbyPanel,       true);
        UnlockCursor();
    }

    /// <summary>Called when the match begins — hides the entire lobby UI.</summary>
    public void HideLobbyUI()
    {
        SetPanel(nameEntryPanel,   false);
        SetPanel(connectionPanel,  false);
        SetPanel(lobbyPanel,       false);
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
