using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Thin bridge that routes all loading-screen calls through the Evo Loader system.
///
/// How it works:
///   - Every LoadScene / ShowImmediate / ShowLoadingScreen call forwards to Evo.Loader.LoadingScreen.
///   - Evo instantiates the prefab, runs its own fade + load coroutine, then destroys itself.
///   - Netcode SceneEvents are intercepted here so clients see a loading screen during any
///     network-driven scene switch (server calls Netcode.SceneManager, not Evo directly).
///
/// Setup (Inspector):
///   1. Assign your LoadingScreen prefab (with Evo.Loader.LoadingScreen on it) to [loaderPrefab].
///   2. Keep this component on a persistent GameObject in your first scene — that is all.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    // =========================================================================
    //  Singleton
    // =========================================================================
    public static LoadingScreen Instance { get; private set; }

    // =========================================================================
    //  Inspector
    // =========================================================================
    [Header("Evo Loader Prefab")]
    [Tooltip("Your LoadingScreen prefab (with Evo.Loader.LoadingScreen component). " +
             "Lives in Assets/_Project/Prefabs/LoadingScreen/.")]
    public Evo.Loader.LoadingScreen loaderPrefab;

    [Header("Boot / Initial Load")]
    [Tooltip("When true and this is the boot/loading scene (index 0), Evo automatically loads the target scene on Start.")]
    public bool autoLoadOnStart = true;

    [Tooltip("The scene to transition into on boot (e.g. LobbyScene). " +
             "Evo spawns the loading prefab here, covering the heavy scene load before the player sees anything.")]
    public string targetSceneName = "LobbyScene";

    // =========================================================================
    //  Private State
    // =========================================================================
    private bool _netcodeSubscribed = false;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Boot path: LoadingScene is scene index 0. Evo spawns the prefab immediately,
        // covering the entire LobbyScene load so the player never sees a black freeze.
        if (autoLoadOnStart && IsBootScene())
            LoadScene(targetSceneName);
    }

    void Update()
    {
        // Netcode's SceneManager may not exist yet at OnEnable — poll until it appears.
        if (!_netcodeSubscribed &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetcodeSceneEvent;
            _netcodeSubscribed = true;
        }
    }

    // =========================================================================
    //  Boot Detection
    // =========================================================================
    private bool IsBootScene()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        int    index     = SceneManager.GetActiveScene().buildIndex;
        return index == 0
            || sceneName.Equals("LoadingScene", System.StringComparison.OrdinalIgnoreCase)
            || sceneName.Equals("BootScene",    System.StringComparison.OrdinalIgnoreCase)
            || sceneName.Equals("Boot",         System.StringComparison.OrdinalIgnoreCase);
    }

    private const string HOLD_TASK_ID = "SceneTransitionHold";

    // =========================================================================
    //  Public API  (same surface as before — all callsites compile unchanged)
    // =========================================================================

    /// <summary>
    /// Loads a scene by name through Evo Loader (instantiates prefab, fades, loads, destroys).
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (loaderPrefab == null)
        {
            Debug.LogError("[LoadingScreen] loaderPrefab is not assigned! Drag your prefab into the Inspector.");
            return;
        }
        Evo.Loader.LoadingScreen.LoadScene(sceneName, loaderPrefab);
    }

    /// <summary>
    /// Loads a scene by build index through Evo Loader.
    /// </summary>
    public void LoadScene(int sceneIndex)
    {
        string path = SceneUtility.GetScenePathByBuildIndex(sceneIndex);
        if (!string.IsNullOrEmpty(path))
        {
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
            Evo.Loader.LoadingScreen.LoadScene(sceneName, loaderPrefab);
        }
        else
        {
            Debug.LogWarning($"[LoadingScreen] Could not resolve scene name for build index {sceneIndex}.");
        }
    }

    /// <summary>
    /// Shows the loading screen in Transition-Only mode (no scene load).
    /// Use before a Netcode-driven load so clients see the screen while the server drives the switch.
    /// Adds a hold task so Evo Loader does NOT self-dismiss until HideLoadingScreen() is called.
    /// </summary>
    public void ShowLoadingScreen()
    {
        if (loaderPrefab == null) return;
        var active = Evo.Loader.LoadingScreen.GetInstance();
        if (active != null)
        {
            EnsureHoldTask(active);
            return;
        }

        active = Evo.Loader.LoadingScreen.ShowTransition(loaderPrefab);
        if (active != null)
        {
            EnsureHoldTask(active);
        }
    }

    /// <summary>
    /// Shows the loading screen instantly in Transition-Only mode.
    /// Call right before a freeze-prone moment (e.g. player clicks Ready, Netcode handshake).
    /// </summary>
    public void ShowImmediate()
    {
        ShowLoadingScreen();
    }

    private void EnsureHoldTask(Evo.Loader.LoadingScreen active)
    {
        if (active == null) return;
        if (active.tasks != null && active.tasks.Exists(t => t.id == HOLD_TASK_ID && !t.isComplete))
            return;
        active.AddTask(HOLD_TASK_ID, "");
    }

    /// <summary>
    /// Signals the active Evo loading screen to proceed to teardown and hide.
    /// Only needed for ShowLoadingScreen / ShowImmediate calls — a real LoadScene hides itself.
    /// </summary>
    public void HideLoadingScreen()
    {
        Evo.Loader.LoadingScreen active = Evo.Loader.LoadingScreen.GetInstance();
        if (active != null)
        {
            if (active.tasks != null)
            {
                for (int i = 0; i < active.tasks.Count; i++)
                {
                    if (active.tasks[i].id == HOLD_TASK_ID)
                        active.tasks[i].isComplete = true;
                }
            }
            active.CompleteTask(HOLD_TASK_ID);
            active.NotifyInputReceived();
        }
    }

    // =========================================================================
    //  Scene Event Interception
    // =========================================================================
    private void SubscribeNetcodeSceneEvents()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetcodeSceneEvent;
            _netcodeSubscribed = true;
        }
    }

    private void UnsubscribeNetcodeSceneEvents()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetcodeSceneEvent;

        _netcodeSubscribed = false;
    }

    private void OnEnable()
    {
        SubscribeNetcodeSceneEvents();
        SceneManager.sceneLoaded += OnUnitySceneLoaded;
    }

    private void OnDisable()
    {
        UnsubscribeNetcodeSceneEvents();
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name.Equals("LoadingScene", System.StringComparison.OrdinalIgnoreCase) ||
            scene.name.Equals("BootScene",    System.StringComparison.OrdinalIgnoreCase) ||
            scene.name.Equals("Boot",         System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Destination scene (GameScene or LobbyScene) finished loading — hide after rendering
        StartCoroutine(HideAfterOneFrame());
    }

    private void OnNetcodeSceneEvent(SceneEvent sceneEvent)
    {
        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                // Netcode is loading a new scene for everyone — show and hold the loading screen on this client.
                ShowLoadingScreen();
                break;

            case SceneEventType.LoadComplete:
                // Scene is live on this client — wait until it renders then dismiss
                if (NetworkManager.Singleton != null && sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId)
                {
                    StartCoroutine(HideAfterOneFrame());
                }
                break;

            case SceneEventType.LoadEventCompleted:
                StartCoroutine(HideAfterOneFrame());
                break;
        }
    }

    private System.Collections.IEnumerator HideAfterOneFrame()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        HideLoadingScreen();
    }
}
