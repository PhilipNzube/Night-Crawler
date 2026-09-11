using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Displays a clean UI overlay when the Host disconnects or terminates the session:
/// "The Host has disconnected. Returning to Lobby..."
/// Prevents clients from being stranded in an empty or broken scene.
/// </summary>
public class HostDisconnectUI : MonoBehaviour
{
    private static HostDisconnectUI _instance;
    public static HostDisconnectUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<HostDisconnectUI>(FindObjectsInactive.Include);
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("UI References (Assign in Editor)")]
    [Tooltip("Panel holding the disconnect overlay. If unassigned, will use DeathUI or fallback display.")]
    public GameObject disconnectPanel;

    [Tooltip("CanvasGroup for smooth fade-in.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Main title text (e.g., 'HOST DISCONNECTED').")]
    public TMP_Text titleText;

    [Tooltip("Subtitle / explanation text (e.g., 'The Host has disconnected. Returning to Lobby...').")]
    public TMP_Text subtitleText;

    [Tooltip("Countdown text (e.g., 'Returning in 3s...').")]
    public TMP_Text countdownText;

    [Header("Settings")]
    public float returnDelaySeconds = 3.5f;
    public string lobbySceneName = "LobbyScene";

    private bool _hasTriggered = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (disconnectPanel != null)
        {
            disconnectPanel.SetActive(false);
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
        if (Instance == this) Instance = null;
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        // Only trigger on remote clients (Host is server; when host quits, server process terminates)
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer) return;
        if (_hasTriggered) return;

        // If local client disconnected from server, or host disconnected
        if (clientId == NetworkManager.Singleton.LocalClientId || clientId == NetworkManager.ServerClientId)
        {
            _hasTriggered = true;
            StartCoroutine(HostDisconnectedRoutine());
        }
    }

    public void TriggerHostDisconnect()
    {
        if (_hasTriggered) return;
        _hasTriggered = true;
        StartCoroutine(HostDisconnectedRoutine());
    }

    private IEnumerator HostDisconnectedRoutine()
    {
        // 1. Unlock cursor so user is not trapped in locked mouse mode
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 2. Disable local player controls if present
        DisableLocalPlayerControls();

        // 3. Display UI Overlay
        if (disconnectPanel != null)
        {
            disconnectPanel.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
            }
            if (titleText != null) titleText.text = "HOST DISCONNECTED";
            if (subtitleText != null) subtitleText.text = "The Host has disconnected. Returning to Lobby...";
        }
        else if (DeathUI.Instance != null)
        {
            // Clean fallback using existing high-contrast overlay
            DeathUI.Instance.ShowDeathScreen("HOST DISCONNECTED", "The Host has disconnected. Returning to Lobby...");
        }

        // 4. Countdown display
        float remaining = returnDelaySeconds;
        while (remaining > 0f)
        {
            string countStr = $"Returning to Lobby in {Mathf.CeilToInt(remaining)}s...";
            if (countdownText != null)
            {
                countdownText.text = countStr;
            }
            else if (subtitleText != null && disconnectPanel != null)
            {
                subtitleText.text = $"The Host has disconnected. {countStr}";
            }

            yield return new WaitForSecondsRealtime(1f);
            remaining -= 1f;
        }

        // 5. Clean network shutdown & transition to Lobby
        if (NetworkManager.Singleton != null)
        {
            GameObject netObj = NetworkManager.Singleton.gameObject;
            NetworkManager.Singleton.Shutdown();
            Destroy(netObj);
        }

        if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.LoadScene(lobbySceneName);
        }
        else
        {
            SceneManager.LoadScene(lobbySceneName);
        }
    }

    private void DisableLocalPlayerControls()
    {
        var controllers = FindObjectsByType<StarterAssets.ThirdPersonController>(FindObjectsSortMode.None);
        foreach (var c in controllers)
        {
            c.enabled = false;
        }
        var inputs = FindObjectsByType<StarterAssets.StarterAssetsInputs>(FindObjectsSortMode.None);
        foreach (var i in inputs)
        {
            i.cursorLocked = false;
            i.cursorInputForLook = false;
            i.enabled = false;
        }
    }
}
