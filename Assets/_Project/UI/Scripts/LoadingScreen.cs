using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using TMPro;
using System.Collections;

/// <summary>
/// SOLID — SRP: Manages loading screen display, tips, progress bar, and scene transitions.
/// Intercepts both local scene loads and Netcode network scene transitions.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    // =========================================================================
    //  Singleton
    // =========================================================================
    public static LoadingScreen Instance { get; private set; }

    // =========================================================================
    //  Inspector — Panels & Visuals
    // =========================================================================
    [Header("Panels & Canvas")]
    [Tooltip("The root GameObject of the loading screen UI panel.")]
    public GameObject loadingRoot;

    [Tooltip("CanvasGroup on the loading overlay for smooth fade in/out transitions.")]
    public CanvasGroup fadeCanvasGroup;

    [Header("Visuals")]
    public Image logoImage;
    public Image progressFill;
    public TextMeshProUGUI progressText;
    public TextMeshProUGUI tipText;

    // =========================================================================
    //  Inspector — Timing Settings
    // =========================================================================
    [Header("Boot & Timing Settings")]
    public bool autoLoadOnStart = true;
    public string targetSceneName = "LobbyScene";
    public int targetSceneIndex = 1;

    [Range(1f, 10f)]
    public float initialBootMinDuration = 3.5f;

    [Range(0.2f, 5f)]
    public float sceneTransitionMinDuration = 1.2f;

    [Range(0.1f, 2f)]
    public float holdAfterComplete = 0.4f;

    [Range(0.1f, 1.5f)]
    public float fadeDuration = 0.35f;

    // =========================================================================
    //  Inspector — Loading Tips
    // =========================================================================
    [Header("Loading Tips")]
    [TextArea(2, 4)]
    public string[] tips = new string[]
    {
        "When footsteps sound upon the stone, beware the dark, you're not alone.",
        "Trust is fragile down below, who is friend and who is foe?",
        "Save your bullets, count your gear, the Vengeful Spirit draws so near.",
        "Shadows twist and paths turn round, no escape can here be found.",
        "Whispers echo in the cold, don't believe the lies you're told.",
        "When the lights begin to fade, someone has a bargain made."
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
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (IsBootScene())
        {
            if (loadingRoot != null) loadingRoot.SetActive(true);
            if (fadeCanvasGroup != null) fadeCanvasGroup.alpha = 1f;
        }
        else
        {
            if (loadingRoot != null) loadingRoot.SetActive(false);
            if (fadeCanvasGroup != null) fadeCanvasGroup.alpha = 0f;
        }
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SubscribeNetcodeSceneEvents();
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubscribeNetcodeSceneEvents();
    }

    void Start()
    {
        ShowInitialLoadScreen();
    }

    // =========================================================================
    //  Public API — Scene Loading
    // =========================================================================

    /// <summary>
    /// Async-loads a scene by name with a smooth loading screen & tips.
    /// Example: LoadingScreen.Instance.LoadScene("GameScene");
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;
        StartCoroutine(LoadSceneRoutineInternal(sceneName, -1, isInitialBoot: false));
    }

    /// <summary>
    /// Async-loads a scene by build index with a smooth loading screen & tips.
    /// Example: LoadingScreen.Instance.LoadScene(2);
    /// </summary>
    public void LoadScene(int sceneIndex)
    {
        if (_isLoading) return;
        StartCoroutine(LoadSceneRoutineInternal(null, sceneIndex, isInitialBoot: false));
    }

    /// <summary>
    /// Fades in the loading screen over the current view without immediately switching scene.
    /// </summary>
    public void ShowLoadingScreen()
    {
        if (_isLoading) return;
        _isLoading = true;
        ShowRandomTip();
        SetProgress(0.1f);
        StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Fades out the loading screen.
    /// </summary>
    public void HideLoadingScreen()
    {
        StartCoroutine(FadeOutAndComplete());
    }

    // =========================================================================
    //  Netcode & Scene Change Interception
    // =========================================================================

    private void SubscribeNetcodeSceneEvents()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetcodeSceneEvent;
        }
    }

    private void UnsubscribeNetcodeSceneEvents()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetcodeSceneEvent;
        }
    }

    private void OnNetcodeSceneEvent(SceneEvent sceneEvent)
    {
        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                // Triggered when Netcode starts loading a new scene on this client
                ShowLoadingScreen();
                break;

            case SceneEventType.LoadComplete:
                // Triggered when Netcode finishes scene load for this client
                HideLoadingScreen();
                break;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // When any new scene loads, auto-hide loading screen after a brief hold
        if (_isLoading)
        {
            StartCoroutine(FadeOutAndComplete());
        }
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

        if (op != null)
        {
            op.allowSceneActivation = false;

            while (!op.isDone)
            {
                float targetProgress = Mathf.Clamp01(op.progress / 0.9f);
                float elapsed = Time.realtimeSinceStartup - startTime;
                float timeProgress = Mathf.Clamp01(elapsed / minDuration);

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
        }
        else
        {
            // Simulated progress fallback if scene isn't in build settings during Editor testing
            while (currentDisplayedProgress < 1f)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                currentDisplayedProgress = Mathf.Clamp01(elapsed / minDuration);
                SetProgress(currentDisplayedProgress);
                yield return null;
            }
            yield return new WaitForSeconds(holdAfterComplete);
        }

        yield return StartCoroutine(FadeOutAndComplete());
    }

    // =========================================================================
    //  UI & Fade Helpers
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
            fadeCanvasGroup.blocksRaycasts = true;
            float t = 0f;
            float startAlpha = fadeCanvasGroup.alpha;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, t / fadeDuration);
                yield return null;
            }
            fadeCanvasGroup.alpha = 1f;
        }
    }

    private IEnumerator FadeOutAndComplete()
    {
        if (fadeCanvasGroup != null)
        {
            float t = 0f;
            float startAlpha = fadeCanvasGroup.alpha;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t / fadeDuration);
                yield return null;
            }
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false;
        }

        if (loadingRoot != null) loadingRoot.SetActive(false);
        _isLoading = false;
    }
}
