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

    [Header("Success / Victory UI (Optional)")]
    [Tooltip("Optional dedicated Success panel. If unassigned, disconnectPanel or DeathUI will be used.")]
    public GameObject successPanel;

    [Tooltip("Optional title text for success panel.")]
    public TMP_Text successTitleText;

    [Tooltip("Optional subtitle text for success panel.")]
    public TMP_Text successSubtitleText;

    [Tooltip("Optional countdown text for success panel.")]
    public TMP_Text successCountdownText;

    [Header("Settings")]
    public float returnDelaySeconds = 3.5f;
    public string lobbySceneName = "LobbyScene";

    private bool _hasTriggered = false;
    private bool _wasConnected = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
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

    private void Update()
    {
        if (_hasTriggered) return;

        // Fail-safe connection watcher for remote clients
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                if (NetworkManager.Singleton.IsConnectedClient)
                {
                    _wasConnected = true;
                }
                else if (_wasConnected)
                {
                    // Server has disconnected or terminated!
                    Debug.Log("[HostDisconnectUI] Detected connection drop from Host server.");
                    TriggerHostDisconnect();
                }
            }
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
            Debug.Log($"[HostDisconnectUI] ClientDisconnectCallback fired for id={clientId}");
            TriggerHostDisconnect();
        }
    }

    public void TriggerSuccessOverlay(string title, string subtitle, string countdownFormat, float delay = 5f)
    {
        if (_hasTriggered) return;
        _hasTriggered = true;
        StartCoroutine(SuccessRoutine(title, subtitle, countdownFormat, delay));
    }

    private IEnumerator SuccessRoutine(string title, string subtitle, string countdownFormat, float delay)
    {
        // 1. Send alert into AllyBanner feed
        if (DeathUI.Instance != null)
        {
            DeathUI.Instance.AddAllyAlertEntry("<color=#00E676>|</color> [VICTORY] The Vengeful Spirit has fled. Investigators survive!", new Color(0f, 0.9f, 0.45f, 1f));
        }

        // 2. Unlock cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 3. Disable local player controls
        DisableLocalPlayerControls();

        // 4. Determine panel and text components
        GameObject activePanel = successPanel != null ? successPanel : disconnectPanel;
        TMP_Text titleComp = successTitleText != null ? successTitleText : titleText;
        TMP_Text subComp = successSubtitleText != null ? successSubtitleText : subtitleText;
        TMP_Text countComp = successCountdownText != null ? successCountdownText : countdownText;

        if (activePanel != null)
        {
            activePanel.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
            }
            if (titleComp != null)
            {
                titleComp.text = title;
                titleComp.color = new Color(0.2f, 1f, 0.4f); // Victory Green
            }
            if (subComp != null)
            {
                subComp.text = subtitle;
            }
        }
        else if (DeathUI.Instance != null)
        {
            DeathUI.Instance.ShowDeathScreen(title, subtitle);
            if (DeathUI.Instance.deathCanvasGroup != null)
            {
                DeathUI.Instance.deathCanvasGroup.alpha = 1f;
                DeathUI.Instance.deathCanvasGroup.blocksRaycasts = true;
                DeathUI.Instance.deathCanvasGroup.interactable = true;
            }
        }

        // 5. Live countdown loop
        float remaining = delay;
        while (remaining > 0f)
        {
            string countStr = string.Format(countdownFormat, Mathf.CeilToInt(remaining));
            if (countComp != null)
            {
                countComp.text = countStr;
            }
            else if (subComp != null)
            {
                subComp.text = $"{subtitle}\n{countStr}";
            }
            else if (DeathUI.Instance != null && DeathUI.Instance.subtitleText != null)
            {
                DeathUI.Instance.subtitleText.text = $"{subtitle}\n{countStr}";
            }

            yield return new WaitForSecondsRealtime(1f);
            remaining -= 1f;
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
        // 1. Send alert into AllyBanner feed
        if (DeathUI.Instance != null)
        {
            DeathUI.Instance.AddAllyAlertEntry("<color=#FFA000>|</color> [HOST DISCONNECTED] The Host has left the match.", new Color(0.95f, 0.6f, 0.15f, 1f));
        }

        // 2. Unlock cursor so user is not trapped in locked mouse mode
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 3. Disable local player controls so characters don't fall or drift
        DisableLocalPlayerControls();

        // 4. Display UI Overlay
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
            DeathUI.Instance.ShowDeathScreen("HOST DISCONNECTED", "The Host has disconnected. Returning to Lobby in 3s...");
            if (DeathUI.Instance.deathCanvasGroup != null)
            {
                DeathUI.Instance.deathCanvasGroup.alpha = 1f;
                DeathUI.Instance.deathCanvasGroup.blocksRaycasts = true;
                DeathUI.Instance.deathCanvasGroup.interactable = true;
            }
        }

        // 5. Live countdown display
        float remaining = returnDelaySeconds;
        while (remaining > 0f)
        {
            string countStr = $"The Host has disconnected. Returning to Lobby in {Mathf.CeilToInt(remaining)}s...";
            if (countdownText != null)
            {
                countdownText.text = countStr;
            }
            else if (subtitleText != null && disconnectPanel != null)
            {
                subtitleText.text = countStr;
            }
            else if (DeathUI.Instance != null && DeathUI.Instance.subtitleText != null)
            {
                DeathUI.Instance.subtitleText.text = countStr;
            }

            yield return new WaitForSecondsRealtime(1f);
            remaining -= 1f;
        }

        // 6. Clean network shutdown & transition to Lobby
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
