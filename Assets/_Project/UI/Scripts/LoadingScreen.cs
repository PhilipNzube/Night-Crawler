using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// <summary>
/// SOLID — SRP: Manages loading screen display, tips, progress bar, and scene transitions.
///
/// Handles two cases:
///   1. Initial boot — automatically triggers in scene index 0 / BootScene, showing tips
///      for a set duration before transitioning to the Lobby scene.
///   2. Scene transitions — call LoadingScreen.Instance.LoadScene("GameScene")
///      to fade in the loading screen and load the target scene.
///
/// Setup:
///   1. Place LoadingCanvas in your first (Boot) scene.
///   2. Add scenes to File -> Build Settings (Index 0: Boot, Index 1: Lobby, Index 2: Game).
///   3. Wire up Inspector fields below.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Singleton
    // -------------------------------------------------------------------------
    public static LoadingScreen Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Inspector — Panels
    // -------------------------------------------------------------------------
    [Header("Panels")]
    [Tooltip("The root GameObject of the loading screen. Shown/hidden as needed.")]
    public GameObject loadingRoot;

    // -------------------------------------------------------------------------
    //  Inspector — Visuals
    // -------------------------------------------------------------------------
    [Header("Visuals")]
    [Tooltip("Optional game logo shown while loading.")]
    public Image logoImage;

    [Tooltip("The progress bar fill image. Set Image Type = Filled, Fill Method = Horizontal.")]
    public Image progressFill;

    [Tooltip("Displays loading progress, e.g. 'Loading...  72%'.")]
    public TextMeshProUGUI progressText;

    [Tooltip("Rotating tip text shown during loading.")]
    public TextMeshProUGUI tipText;

    [Tooltip("Overlay CanvasGroup used for fade in/out transitions.")]
    public CanvasGroup fadeCanvasGroup;

    // -------------------------------------------------------------------------
    //  Inspector — Boot & Timing Settings
    // -------------------------------------------------------------------------
    [Header("Boot & Timing Settings")]
    [Tooltip("If true, automatically loads the target scene on Start when in the Boot scene.")]
    public bool autoLoadOnStart = true;

    [Tooltip("The name of the scene to load on initial boot (e.g. 'LobbyScene'). If blank, uses targetSceneIndex.")]
    public string targetSceneName = "LobbyScene";

    [Tooltip("The build index of the scene to load if targetSceneName is blank or not found.")]
    public int targetSceneIndex = 1;

    [Tooltip("Minimum time (in seconds) the loading screen stays visible on initial boot so tips can be read.")]
    [Range(1f, 10f)]
    public float initialBootMinDuration = 3.5f;

    [Tooltip("Minimum time (in seconds) the loading screen stays visible during in-game scene transitions.")]
    [Range(0.2f, 5f)]
    public float sceneTransitionMinDuration = 1.0f;

    [Tooltip("How long to hold the loading screen after reaching 100% (visual polish).")]
    [Range(0.1f, 2f)]
    public float holdAfterComplete = 0.5f;

    [Tooltip("Duration of fade-in and fade-out animations.")]
    [Range(0.1f, 1.5f)]
    public float fadeDuration = 0.4f;

    // -------------------------------------------------------------------------
    //  Inspector — Loading Tips
    // -------------------------------------------------------------------------
    [Header("Loading Tips")]
    [Tooltip("Gameplay tips displayed randomly during loading.")]
    [TextArea(2, 4)]
    public string[] tips = new string[]
    {
        "The Vengeful Spirit can vanish from sight — listen for footsteps.",
        "Investigators: stick together. Alone, you are easy prey.",
        "Watch your ammo. Every shot counts.",
        "The Vengeful Spirit grows stronger the longer the match goes on.",
        "Avoid dark corners — the Vengeful Spirit thrives in the shadows."
    };

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private bool _isLoading = false;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        // Singleton — persist across scenes
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // If launching from the boot scene, keep loadingRoot ACTIVE so it shows immediately!
        if (IsBootScene())
        {
            if (loadingRoot != null) loadingRoot.SetActive(true);
            if (fadeCanvasGroup != null) fadeCanvasGroup.alpha = 1f;
        }
        else
        {
            // If launching directly into Lobby or Game scene (e.g. Editor testing), start hidden
            if (loadingRoot != null) loadingRoot.SetActive(false);
            if (fadeCanvasGroup != null) fadeCanvasGroup.alpha = 0f;
        }
    }

    void Start()
    {
        ShowInitialLoadScreen();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Async-loads a scene by name with a loading screen transition.
    /// Example: LoadingScreen.Instance.LoadScene("GameScene");
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;
        StartCoroutine(LoadSceneRoutineInternal(sceneName, -1, isInitialBoot: false));
    }

    /// <summary>
    /// Async-loads a scene by build index with a loading screen transition.
    /// Example: LoadingScreen.Instance.LoadScene(2);
    /// </summary>
    public void LoadScene(int sceneIndex)
    {
        if (_isLoading) return;
        StartCoroutine(LoadSceneRoutineInternal(null, sceneIndex, isInitialBoot: false));
    }

    /// <summary>
    /// Manual show function for custom loading states.
    /// </summary>
    public void ShowLoadingScreen()
    {
        StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Manual hide function for custom loading states.
    /// </summary>
    public void HideLoadingScreen()
    {
        StartCoroutine(FadeOut());
    }

    // =========================================================================
    //  Initial Boot Handling
    // =========================================================================
    private bool IsBootScene()
    {
        string activeName = SceneManager.GetActiveScene().name;
        return SceneManager.GetActiveScene().buildIndex == 0 ||
               activeName.Equals("BootScene", System.StringComparison.OrdinalIgnoreCase) ||
               activeName.Equals("Boot", System.StringComparison.OrdinalIgnoreCase);
    }

    private void ShowInitialLoadScreen()
    {
        if (!autoLoadOnStart || !IsBootScene()) return;

        StartCoroutine(LoadSceneRoutineInternal(targetSceneName, targetSceneIndex, isInitialBoot: true));
    }

    // =========================================================================
    //  Unified Scene Load Routine
    // =========================================================================
    private IEnumerator LoadSceneRoutineInternal(string sceneName, int sceneIndex, bool isInitialBoot)
    {
        _isLoading = true;

        yield return StartCoroutine(FadeIn());
        ShowRandomTip();
        SetProgress(0f);

        float minDuration = isInitialBoot ? initialBootMinDuration : sceneTransitionMinDuration;
        float startTime = Time.realtimeSinceStartup;
        float currentDisplayedProgress = 0f;

        AsyncOperation op = null;

        if (!string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
        {
            op = SceneManager.LoadSceneAsync(sceneName);
        }
        else if (sceneIndex >= 0 && sceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            op = SceneManager.LoadSceneAsync(sceneIndex);
        }
        else
        {
            Debug.LogWarning($"[LoadingScreen] Target scene '{sceneName}' (index {sceneIndex}) could not be found or loaded! " +
                             "Please check File -> Build Settings and make sure your scenes are added to the build list.");

            // Simulated progress bar fallback so UI doesn't freeze in Editor when scenes aren't added to Build Settings yet
            while (currentDisplayedProgress < 1f)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                currentDisplayedProgress = Mathf.Clamp01(elapsed / minDuration);
                SetProgress(currentDisplayedProgress);
                yield return null;
            }

            yield return new WaitForSeconds(holdAfterComplete);
            yield return StartCoroutine(FadeOut());
            _isLoading = false;
            yield break;
        }

        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            float targetProgress = Mathf.Clamp01(op.progress / 0.9f);
            float elapsed = Time.realtimeSinceStartup - startTime;
            float timeProgress = Mathf.Clamp01(elapsed / minDuration);

            // Smoothly progress according to both time and background loading
            currentDisplayedProgress = (op.progress >= 0.9f) ? timeProgress : Mathf.Min(targetProgress, timeProgress);

            if (op.progress >= 0.9f && elapsed >= minDuration)
            {
                SetProgress(1f);
                yield return new WaitForSeconds(holdAfterComplete);
                op.allowSceneActivation = true;
            }

            SetProgress(currentDisplayedProgress);
            yield return null;
        }

        yield return StartCoroutine(FadeOut());
        _isLoading = false;
    }

    // =========================================================================
    //  UI Helpers & Animations
    // =========================================================================
    private void SetProgress(float fraction)
    {
        if (progressFill != null)
            progressFill.fillAmount = fraction;

        if (progressText != null)
            progressText.text = $"Loading...  {Mathf.RoundToInt(fraction * 100f)}%";
    }

    private void ShowRandomTip()
    {
        if (tipText == null || tips == null || tips.Length == 0) return;
        tipText.text = tips[Random.Range(0, tips.Length)];
    }

    private IEnumerator FadeIn()
    {
        if (loadingRoot != null) loadingRoot.SetActive(true);

        if (fadeCanvasGroup != null)
        {
            float startAlpha = fadeCanvasGroup.alpha;
            float t = startAlpha * fadeDuration;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                fadeCanvasGroup.alpha = Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            fadeCanvasGroup.alpha = 1f;
        }
    }

    private IEnumerator FadeOut()
    {
        if (fadeCanvasGroup != null)
        {
            float t = fadeDuration;
            while (t > 0f)
            {
                t -= Time.deltaTime;
                fadeCanvasGroup.alpha = Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            fadeCanvasGroup.alpha = 0f;
        }

        if (loadingRoot != null) loadingRoot.SetActive(false);
    }
}
