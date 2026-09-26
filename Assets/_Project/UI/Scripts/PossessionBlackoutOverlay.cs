using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Michsky.UI.Heat;
using NightCrawler.UI;

/// <summary>
/// SOLID — SRP: Renders full-screen blackout and the chilling message
/// "Let me take the wheel for a sec" when the player's character is possessed by the Girl.
/// Also hosts the rapid-mash Tug-of-War Struggle QTE for the Cursed Priest to break free.
/// </summary>
public class PossessionBlackoutOverlay : MonoBehaviour
{
    private static PossessionBlackoutOverlay _instance;
    public static PossessionBlackoutOverlay Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<PossessionBlackoutOverlay>(FindObjectsInactive.Include);
            }
            if (_instance != null && !_instance.gameObject.activeInHierarchy)
            {
                _instance.gameObject.SetActive(true);
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("UI References")]
    public CanvasGroup blackoutCanvasGroup;
    public TMP_Text possessMessageText;
    public TMP_Text subtitleText;
    public TMP_Text timerText;

    [Header("Message")]
    public string defaultMessage = "Let me take the wheel for a sec\u2620\uFE0F";
    public string defaultSubtitle = "The Vengeful Spirit has taken control of your body...";

    [Header("Priest Possession Rejection Prompt (Legacy Fallback)")]
    public TMP_Text rejectPromptText;

    // =========================================================================
    //  Priest Tug-of-War Struggle QTE (Heat UI / Standard)
    // =========================================================================

    [Header("Priest Struggle QTE (Tug-of-War Button Mash)")]
    [Tooltip("Root GameObject container for the struggle QTE bar and prompts.")]
    public GameObject struggleQTERoot;

    [Tooltip("Michsky Heat UI ProgressBar representing the tug-of-war struggle.")]
    public ProgressBar heatStruggleBar;

    [Tooltip("Standard Unity UI Slider fallback if Heat UI ProgressBar is not used.")]
    public Slider standardStruggleSlider;

    [Tooltip("Image fill for the struggle bar (for dynamic color shifting).")]
    public Image struggleBarFillImage;

    [Tooltip("Status label (e.g. 'BREAKING FREE!', 'LOSING CONTROL!').")]
    public TMP_Text struggleStatusText;

    [Tooltip("Instruction label (e.g. 'MASH [R] TO RESIST!').")]
    public TMP_Text struggleInstructionText;

    [Tooltip("Time remaining countdown text (e.g. '3.8s').")]
    public TMP_Text struggleCountdownText;

    [Tooltip("Key indicator transform that punches/scales up on each button press.")]
    public RectTransform keyIndicatorPunchTarget;

    [Header("Struggle Balance & Colors")]
    [Tooltip("Initial struggle percentage (0 to 100). Default is 40% (slight demonic advantage).")]
    [Range(0f, 100f)] public float initialStruggle = 40f;

    [Tooltip("Rate at which the demon pulls the bar down per second.")]
    [Range(5f, 60f)]  public float demonDecayPerSecond = 18f;

    [Tooltip("Amount of struggle gained per button mash.")]
    [Range(2f, 25f)]  public float playerBoostPerMash = 8.5f;

    [Tooltip("Color when Priest is winning (above 65%).")]
    public Color winningColor = new Color(0.18f, 0.80f, 0.44f); // Emerald Green

    [Tooltip("Color when in balanced struggle (35% to 65%).")]
    public Color strugglingColor = new Color(0.95f, 0.61f, 0.07f); // Amber / Gold

    [Tooltip("Color when Demonic possession is winning (below 35%).")]
    public Color losingColor = new Color(0.91f, 0.30f, 0.24f); // Demonic Crimson

    private float _possessionStartTime;
    private bool _isBlackoutActive;
    private float _rejectionWindowEndTime;
    private bool _isRejectionActive;
    private bool _isStruggleActive;
    private Coroutine _punchCoroutine;
    private Coroutine _struggleCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureQTEReferences();
        SetBlackout(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_isBlackoutActive)
        {
            float elapsed = Time.time - _possessionStartTime;
            if (timerText != null)
            {
                timerText.text = $"Possessed: {elapsed:F1}s";
            }

            // Legacy simple text prompt countdown (only when full struggle QTE root is inactive)
            if (_isRejectionActive && !_isStruggleActive)
            {
                float remaining = Mathf.Max(0f, _rejectionWindowEndTime - Time.time);
                if (remaining > 0f)
                {
                    string promptStr = $"Press R to purge spirit & reject ({remaining:F1}s)";
                    if (rejectPromptText != null)
                    {
                        rejectPromptText.text = promptStr;
                    }
                    else if (subtitleText != null)
                    {
                        subtitleText.text = promptStr;
                    }
                    else if (possessMessageText != null)
                    {
                        possessMessageText.text = defaultMessage + "\n\n" + promptStr;
                    }
                }
                else
                {
                    HideRejectionPrompt();
                }
            }
        }
    }

    // =========================================================================
    //  Auto-Discovery & Setup
    // =========================================================================

    public void EnsureQTEReferences()
    {
        if (struggleQTERoot == null)
        {
            var t = transform.Find("StruggleQTE") ?? transform.Find("ResistQTE") ?? transform.Find("QTE");
            if (t != null) struggleQTERoot = t.gameObject;
        }

        Transform searchRoot = struggleQTERoot != null ? struggleQTERoot.transform : transform;

        if (heatStruggleBar == null)
            heatStruggleBar = searchRoot.GetComponentInChildren<ProgressBar>(true);

        if (standardStruggleSlider == null)
            standardStruggleSlider = searchRoot.GetComponentInChildren<Slider>(true);

        if (struggleBarFillImage == null && heatStruggleBar != null)
            struggleBarFillImage = heatStruggleBar.barImage;

        if (struggleStatusText == null)
        {
            foreach (var txt in searchRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (txt.gameObject.name.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    struggleStatusText = txt;
                    break;
                }
            }
        }

        if (struggleInstructionText == null)
        {
            foreach (var txt in searchRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (txt.gameObject.name.IndexOf("Instruction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.gameObject.name.IndexOf("Prompt", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    struggleInstructionText = txt;
                    break;
                }
            }
        }

        if (struggleCountdownText == null)
        {
            foreach (var txt in searchRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (txt.gameObject.name.IndexOf("Countdown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.gameObject.name.IndexOf("Timer", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    struggleCountdownText = txt;
                    break;
                }
            }
        }

        if (keyIndicatorPunchTarget == null)
        {
            var keyObj = searchRoot.Find("Hotkey Indicator") ?? searchRoot.Find("Key") ?? searchRoot.Find("Hotkey");
            if (keyObj != null) keyIndicatorPunchTarget = keyObj.GetComponent<RectTransform>();
        }
    }

    // =========================================================================
    //  Priest Tug-of-War Struggle QTE Loop
    // =========================================================================

    /// <summary>
    /// Starts the rapid-mash struggle QTE. Decays down over time; each mash increments progress.
    /// Reaching 100% wins (rejects possession); hitting 0% or timeout loses (possession confirmed).
    /// </summary>
    public IEnumerator RunPriestStruggleRoutine(
        float maxDuration,
        Func<bool> isPossessedCheck,
        Action<bool> onComplete)
    {
        EnsureQTEReferences();

        _isStruggleActive = true;
        _isRejectionActive = true;

        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(true);
        }

        // Hide legacy plain text prompt while QTE is visible
        if (rejectPromptText != null) rejectPromptText.gameObject.SetActive(false);

        float currentProgress = Mathf.Clamp(initialStruggle, 10f, 90f);
        float elapsed = 0f;
        bool won = false;

        UpdateStruggleUI(currentProgress, maxDuration);

        while (elapsed < maxDuration)
        {
            // Abort if possession state was externally terminated
            if (isPossessedCheck != null && !isPossessedCheck())
            {
                break;
            }

            float dt = Time.deltaTime;
            elapsed += dt;

            // 1. Demonic decay naturally drags the struggle bar down
            currentProgress -= demonDecayPerSecond * dt;

            // 2. Rapid button mash input detection (Keyboard R or Space, Gamepad West / South)
            bool mashed = false;

            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    mashed = true;
                }
            }
            else if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space))
            {
                mashed = true;
            }

            if (UnityEngine.InputSystem.Gamepad.current != null)
            {
                if (UnityEngine.InputSystem.Gamepad.current.buttonWest.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Gamepad.current.buttonSouth.wasPressedThisFrame)
                {
                    mashed = true;
                }
            }

            if (mashed)
            {
                currentProgress += playerBoostPerMash;
                TriggerKeyPunch();
            }

            // Clamp progress between 0 and 100
            currentProgress = Mathf.Clamp(currentProgress, 0f, 100f);

            // 3. Update Visuals
            float timeLeft = Mathf.Max(0f, maxDuration - elapsed);
            UpdateStruggleUI(currentProgress, timeLeft);

            // Win condition: Player pushed bar to 100%
            if (currentProgress >= 100f)
            {
                won = true;
                break;
            }

            // Lose condition: Demon overpowered the bar to 0%
            if (currentProgress <= 0f)
            {
                won = false;
                break;
            }

            yield return null;
        }

        // Outcome feedback animation / text
        if (won)
        {
            if (struggleStatusText != null)
                struggleStatusText.text = "<color=#2ECC71><b>SPIRIT EXPELLED!</b></color>";
            yield return new WaitForSeconds(0.45f);
        }
        else
        {
            if (struggleStatusText != null)
                struggleStatusText.text = "<color=#E74C3C><b>OVERPOWERED...</b></color>";
            yield return new WaitForSeconds(0.45f);
        }

        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(false);
        }

        _isStruggleActive = false;
        _isRejectionActive = false;

        onComplete?.Invoke(won);
    }

    private void UpdateStruggleUI(float progressPercent, float timeLeft)
    {
        // 1. Update Heat UI Progress Bar and standard Slider
        MichskyUIBridge.SetProgress(standardStruggleSlider, heatStruggleBar, progressPercent, 100f);

        // 2. Dynamic Tension Colors & Status
        Color targetColor;
        string statusStr;

        if (progressPercent >= 65f)
        {
            targetColor = winningColor;
            statusStr = "<color=#2ECC71><b>WINNING — PURGE THE SPIRIT!</b></color>";
        }
        else if (progressPercent <= 35f)
        {
            targetColor = losingColor;
            statusStr = "<color=#E74C3C><b>LOSING CONTROL — MASH FASTER!</b></color>";
        }
        else
        {
            targetColor = strugglingColor;
            statusStr = "<color=#F39C12><b>STRUGGLING FOR CONTROL...</b></color>";
        }

        if (struggleStatusText != null)
            struggleStatusText.text = statusStr;

        if (struggleInstructionText != null)
            struggleInstructionText.text = "MASH <b>[R]</b> RAPIDLY TO BREAK FREE!";

        if (struggleCountdownText != null)
            struggleCountdownText.text = $"{timeLeft:F1}s";

        // Update fill bar color
        if (struggleBarFillImage != null)
        {
            struggleBarFillImage.color = targetColor;
        }
        else if (heatStruggleBar != null && heatStruggleBar.barImage != null)
        {
            heatStruggleBar.barImage.color = targetColor;
        }
    }

    private void TriggerKeyPunch()
    {
        if (keyIndicatorPunchTarget == null) return;
        if (_punchCoroutine != null) StopCoroutine(_punchCoroutine);
        _punchCoroutine = StartCoroutine(KeyPunchRoutine());
    }

    private IEnumerator KeyPunchRoutine()
    {
        Vector3 baseScale = Vector3.one;
        Vector3 punchedScale = Vector3.one * 1.30f;
        keyIndicatorPunchTarget.localScale = punchedScale;

        float t = 0f;
        float duration = 0.10f;
        while (t < duration)
        {
            t += Time.deltaTime;
            keyIndicatorPunchTarget.localScale = Vector3.Lerp(punchedScale, baseScale, t / duration);
            yield return null;
        }
        keyIndicatorPunchTarget.localScale = baseScale;
    }

    // =========================================================================
    //  Legacy API Support (Backwards Compatibility)
    // =========================================================================

    public void ShowRejectionPrompt(float durationSeconds)
    {
        EnsureQTEReferences();

        // If struggle QTE elements exist, show the QTE root directly
        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(true);
            UpdateStruggleUI(initialStruggle, durationSeconds);
            return;
        }

        _isRejectionActive = true;
        _rejectionWindowEndTime = Time.time + durationSeconds;
        string promptStr = $"Press R to purge spirit & reject ({durationSeconds:F1}s)";
        if (rejectPromptText != null)
        {
            rejectPromptText.gameObject.SetActive(true);
            rejectPromptText.text = promptStr;
        }
        else if (subtitleText != null)
        {
            subtitleText.text = promptStr;
        }
        else if (possessMessageText != null)
        {
            possessMessageText.text = defaultMessage + "\n\n" + promptStr;
        }
    }

    public void HideRejectionPrompt()
    {
        _isRejectionActive = false;
        _isStruggleActive = false;

        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(false);
        }

        if (rejectPromptText != null)
        {
            rejectPromptText.gameObject.SetActive(false);
        }
        else if (subtitleText != null)
        {
            subtitleText.text = defaultSubtitle;
        }
        else if (possessMessageText != null)
        {
            possessMessageText.text = defaultMessage;
        }
    }

    public void SetBlackout(bool active, string customMessage = null)
    {
        if (active)
        {
            // The Girl player must NEVER have a blackout screen!
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.LocalClient != null)
            {
                var localObj = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localObj != null && (localObj.GetComponent<GirlPossession>() != null || localObj.name.ToLower().Contains("girl")))
                {
                    return;
                }
            }
        }

        _isBlackoutActive = active;

        if (active)
        {
            _possessionStartTime = Time.time;
        }
        else
        {
            HideRejectionPrompt();
        }

        if (blackoutCanvasGroup == null)
            blackoutCanvasGroup = GetComponent<CanvasGroup>();

        if (blackoutCanvasGroup != null)
        {
            blackoutCanvasGroup.alpha = active ? 1f : 0f;
            blackoutCanvasGroup.blocksRaycasts = active;
            blackoutCanvasGroup.interactable = active;
        }

        if (possessMessageText != null)
        {
            possessMessageText.text = !string.IsNullOrEmpty(customMessage) ? customMessage : defaultMessage;
            possessMessageText.gameObject.SetActive(active);
        }

        if (subtitleText != null)
        {
            subtitleText.text = defaultSubtitle;
            subtitleText.gameObject.SetActive(active);
        }

        if (timerText != null)
        {
            timerText.gameObject.SetActive(active);
        }

        // If no CanvasGroup exists, fallback to GameObject active state
        if (blackoutCanvasGroup == null)
        {
            gameObject.SetActive(active);
        }
    }
}
