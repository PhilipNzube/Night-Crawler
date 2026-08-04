using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Manages only the pre-game lobby UI flow.
///
/// Replaces the legacy OnGUI buttons in NetworkUI with a proper full-screen
/// Canvas-based lobby that matches the game's dark aesthetic.
///
/// Panels:
///   • Connection Panel  — shown before host/client is chosen
///   • Lobby Panel       — shown after connecting, while waiting for players
///
/// The host sees a "START MATCH" button once enough players are ready.
/// Clients see a "Waiting for host..." indicator.
///
/// Setup:
///   1. Create a Canvas (Screen Space – Overlay, Sort Order 10).
///   2. Build two child panels: Connection Panel and Lobby Panel.
///   3. Drag this script onto the Canvas root and wire all fields below.
///   4. Disable NetworkUI.OnGUI (done — see NetworkUI.cs).
/// </summary>
public class LobbyUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — Connection Panel
    //  Shown before the player has connected as Host or Client
    // -------------------------------------------------------------------------
    [Header("Connection Panel")]
    [Tooltip("Root panel shown before a connection is made (Host / Client choice screen).")]
    public GameObject connectionPanel;

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
        ShowConnectionPanel();
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
        if (startHostButton   != null) startHostButton.onClick.AddListener(OnStartHost);
        if (startClientButton != null) startClientButton.onClick.AddListener(OnStartClient);
        if (startMatchButton  != null) startMatchButton.onClick.AddListener(OnStartMatch);
        if (disconnectButton  != null) disconnectButton.onClick.AddListener(OnDisconnect);
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

        // Hide lobby UI
        HideLobbyUI();

        // 1. If Netcode SceneManagement is enabled on NetworkManager:
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        // 2. Fallback to LoadingScreen if available:
        else if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.LoadScene(gameSceneName);
        }
        // 3. Fallback standard scene load:
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

        // Player count text
        if (playerCountText != null)
            playerCountText.text = $"{current}  /  {required}  players";

        // Status text
        if (statusText != null)
        {
            if (isServer)
                statusText.text = canStart ? "All players connected — ready to start!" : $"Waiting for {required - current} more player(s)...";
            else
                statusText.text = "Waiting for the host to start the match...";
        }

        // Host-only elements
        if (hostOnlyElements != null)
            hostOnlyElements.SetActive(isServer);

        // Start button interactability
        if (startMatchButton != null)
            startMatchButton.interactable = canStart;
    }

    // =========================================================================
    //  Panel Visibility Helpers
    // =========================================================================
    private void ShowConnectionPanel()
    {
        SetPanel(connectionPanel, true);
        SetPanel(lobbyPanel,      false);
        UnlockCursor();
    }

    private void ShowLobbyPanel()
    {
        SetPanel(connectionPanel, false);
        SetPanel(lobbyPanel,      true);
        UnlockCursor();
    }

    /// <summary>Called when the match begins — hides the entire lobby UI.</summary>
    public void HideLobbyUI()
    {
        SetPanel(connectionPanel, false);
        SetPanel(lobbyPanel,      false);
        // Cursor will be locked by NetworkPlayer when the player spawns
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
