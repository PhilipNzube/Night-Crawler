using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// <summary>
/// SOLID — SRP: Manages the loading screen UI only.
///
/// Handles two cases:
///   1. Initial boot — shown automatically when the game first launches.
///   2. Scene transitions — call LoadingScreen.Instance.LoadScene("SceneName")
///      to fade in the loading screen and async-load the target scene.
///
/// Setup:
///   1. Create a Canvas in your first (boot/splash) scene.
///   2. Build the hierarchy below and drag fields into the Inspector.
///   3. Add this script to the Canvas root.
///   4. The Canvas will DontDestroyOnLoad so it persists across scenes.
///
/// Hierarchy:
///   LoadingCanvas
///   └── LoadingRoot  (this script lives here)
///       ├── Background       (full-screen dark Image)
///       ├── LogoImage        (optional game logo)
///       ├── ProgressBarBG    (Image — bar background)
///       │   └── ProgressFill (Image — fill, set to Filled / Horizontal)
///       ├── ProgressText     (TMP — "Loading... 72%")
///       └── TipText          (TMP — rotating gameplay tips)
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
    [Tooltip("The root GameObject of the loading screen. This is shown/hidden as needed.")]
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

    [Tooltip("Rotating tip text shown during loading (optional).")]
    public TextMeshProUGUI tipText;

    [Tooltip("Overlay CanvasGroup used for fade in/out transitions.")]
    public CanvasGroup fadeCanvasGroup;

    // -------------------------------------------------------------------------
    //  Inspector — Settings
    // -------------------------------------------------------------------------
    [Header("Settings")]
    [Tooltip("How long to hold the loading screen after reaching 100% (visual polish).")]
    [Range(0.2f, 2f)]
    public float holdAfterComplete = 0.5f;

    [Tooltip("Duration of the fade-in and fade-out animations.")]
    [Range(0.1f, 1.5f)]
    public float fadeDuration = 0.4f;

    [Tooltip("Minimum time the loading screen stays visible (prevents a flash for fast loads).")]
    [Range(0.5f, 5f)]
    public float minimumDisplayTime = 1.5f;

    // -------------------------------------------------------------------------
    //  Inspector — Tips
    // -------------------------------------------------------------------------
    [Header("Loading Tips (optional)")]
    [Tooltip("Gameplay tips displayed randomly during loading.")]
    [TextArea(2, 4)]
    public string[] tips = new string[]
    {
        "The demon can vanish from sight — listen for footsteps.",
        "Explorers: stick together. Alone, you are easy prey.",
        "Watch your ammo. Every shot counts.",
        "The demon grows stronger the longer the match goes on.",
        "Avoid dark corners — the demon thrives in the shadows."
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

        // Start hidden
        if (loadingRoot != null) loadingRoot.SetActive(false);
        if (fadeCanvasGroup != null) fadeCanvasGroup.alpha = 0f;
    }

    void Start()
    {
        // If this is the first scene, show the loading screen immediately
        // and load the actual game/lobby scene
        ShowInitialLoadScreen();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Async-loads a scene by name with a full loading screen transition.
    /// Safe to call from any script: LoadingScreen.Instance.LoadScene("GameScene");
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;
        StartCoroutine(LoadSceneRoutine(sceneName));
    }

    /// <summary>
    /// Shows the loading screen for an initial boot or manual use without a scene load.
    /// Call HideLoadingScreen() manually when ready.
    /// </summary>
    public void ShowLoadingScreen()
    {
        StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Fades out and hides the loading screen.
    /// </summary>
    public void HideLoadingScreen()
    {
        StartCoroutine(FadeOut());
    }

    // =========================================================================
    //  Initial Load
    // =========================================================================

    /// <summary>
    /// Called on Start in the boot scene. Shows the loading screen and waits
    /// for scene subscriptions (or a minimum time) before hiding.
    /// </summary>
    private void ShowInitialLoadScreen()
    {
        // Only trigger if we're in scene index 0 (the boot/loading scene)
        if (SceneManager.GetActiveScene().buildIndex != 0) return;

        // We assume scene index 1 is your Lobby / Main Menu scene
        StartCoroutine(LoadSceneRoutine(1));
    }

    // =========================================================================
    //  Scene Load Routine
    // =========================================================================
    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        _isLoading = true;

        yield return StartCoroutine(FadeIn());
        ShowRandomTip();
        SetProgress(0f);

        float startTime = Time.realtimeSinceStartup;

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            // Unity reports 0–0.9 during load, then jumps to 1.0 on activation
            float progress = Mathf.Clamp01(op.progress / 0.9f);
            SetProgress(progress);

            if (op.progress >= 0.9f)
            {
                // Enforce minimum display time
                float elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed < minimumDisplayTime)
                    yield return new WaitForSeconds(minimumDisplayTime - elapsed);

                SetProgress(1f);
                yield return new WaitForSeconds(holdAfterComplete);
                op.allowSceneActivation = true;
            }

            yield return null;
        }

        yield return StartCoroutine(FadeOut());
        _isLoading = false;
    }

    private IEnumerator LoadSceneRoutine(int sceneIndex)
    {
        _isLoading = true;

        yield return StartCoroutine(FadeIn());
        ShowRandomTip();
        SetProgress(0f);

        float startTime = Time.realtimeSinceStartup;

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneIndex);
        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            float progress = Mathf.Clamp01(op.progress / 0.9f);
            SetProgress(progress);

            if (op.progress >= 0.9f)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed < minimumDisplayTime)
                    yield return new WaitForSeconds(minimumDisplayTime - elapsed);

                SetProgress(1f);
                yield return new WaitForSeconds(holdAfterComplete);
                op.allowSceneActivation = true;
            }

            yield return null;
        }

        yield return StartCoroutine(FadeOut());
        _isLoading = false;
    }

    // =========================================================================
    //  Helpers
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
            float t = 0f;
            fadeCanvasGroup.alpha = 0f;
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
