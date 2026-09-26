using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Michsky.UI.Heat;
using UnityEngine.InputSystem;

/// <summary>
/// Full-screen blackout overlay with priest struggle QTE tug-of-war bar and dynamic key prompt.
/// Cleaned up: All useless/legacy inspector fields and dead code branches removed.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PossessionBlackoutOverlay : MonoBehaviour
{
    public static PossessionBlackoutOverlay Instance { get; private set; }

    [Header("Blackout Canvas and Message")]
    [Tooltip("CanvasGroup controlling the blackout overlay visibility and input blocking.")]
    public CanvasGroup blackoutCanvasGroup;

    [Tooltip("Main large text displaying the possession state message.")]
    public TMP_Text possessMessageText;

    [Header("Priest Struggle QTE Elements")]
    [Tooltip("Root GameObject container for the struggle QTE bar and prompts (ResistPossessionGO).")]
    public GameObject struggleQTERoot;

    [Tooltip("Michsky Heat UI ProgressBar representing the tug-of-war struggle.")]
    public ProgressBar heatStruggleBar;

    [Tooltip("The glow halo image under Highlighted.")]
    public Image glowImage;

    [Header("Key Badge Indicator")]
    [Tooltip("Key indicator transform that punches/scales up on each button press (Border or Text Parent).")]
    public RectTransform keyIndicatorPunchTarget;

    [Tooltip("Key indicator text inside the prompt badge (e.g. 'F').")]
    public TMP_Text keyIndicatorText;

    [Tooltip("The keyboard key assigned for resisting possession. The key indicator badge text (e.g. 'F') dynamically updates to reflect this key.")]
    public Key assignedResistKey = Key.F;

    [Tooltip("Fallback KeyCode for legacy Input system if New Input System is not present.")]
    public KeyCode fallbackKeyCode = KeyCode.F;

    [Header("Pulsing Text Components")]
    [Tooltip("Heat UI TextPulse component attached to 'Mash'.")]
    public TextPulse mashTextPulse;

    [Tooltip("Heat UI TextPulse component attached to 'To Resist Possession'.")]
    public TextPulse actionTextPulse;

    [Tooltip("Default message displayed when possessed.")]
    public string defaultMessage = "YOU HAVE BEEN POSSESSED";

    [Tooltip("Default subtitle displayed under the possession message.")]
    public string defaultSubtitle = "Another entity has taken control of your body.";

    [Tooltip("Image component for the struggle bar fill whose color shifts dynamically.")]
    public Image struggleBarFillImage;

    [Header("Pro Game Feel Settings")]
    [Tooltip("Speed at which the visual fill catches up to the logical progress value.")]
    public float fillCatchupSpeed = 16f;

    [Tooltip("Intensity of the tug-of-war struggle tension jitter.")]
    public float tensionJitterIntensity = 1.25f;

    [Tooltip("Enable subtle horizontal shake on the progress bar when under heavy demonic pressure.")]
    public bool enableBarStrainShake = true;

    [Header("Struggle Balance and Colors")]
    [Tooltip("Initial struggle percentage (0 to 100). Default is 40% (slight demonic advantage).")]
    [Range(0f, 100f)] public float initialStruggle = 40f;

    [Tooltip("Rate at which the demon pulls the bar down per second.")]
    [Range(5f, 60f)]  public float demonDecayPerSecond = 18f;

    [Tooltip("Amount of struggle gained per button mash.")]
    [Range(2f, 25f)]  public float playerBoostPerMash = 8.5f;

    [Tooltip("Color when Priest is winning (above 65%).")]
    public Color winningColor = new Color(0.18f, 0.85f, 0.45f); // Emerald Radiant Green

    [Tooltip("Color when in balanced struggle (35% to 65%).")]
    public Color strugglingColor = new Color(0.96f, 0.65f, 0.12f); // Amber / Holy Gold

    [Tooltip("Color when Demonic possession is winning (below 35%).")]
    public Color losingColor = new Color(0.92f, 0.22f, 0.22f); // Demonic Blood Crimson

    private float _possessionStartTime;
    private bool _isBlackoutActive;
    private Coroutine _punchCoroutine;

    public void SetAssignedResistKey(Key newKey)
    {
        assignedResistKey = newKey;
        UpdateKeyIndicatorDisplay();
    }

    public void UpdateKeyIndicatorDisplay()
    {
        string displayStr = FormatKeyDisplayName(assignedResistKey);
        if (keyIndicatorText != null)
        {
            keyIndicatorText.text = displayStr;
        }
    }

    public static string FormatKeyDisplayName(Key key)
    {
        switch (key)
        {
            case Key.Space: return "SPACE";
            case Key.LeftShift: return "L-SHIFT";
            case Key.RightShift: return "R-SHIFT";
            case Key.LeftCtrl: return "L-CTRL";
            case Key.RightCtrl: return "R-CTRL";
            case Key.LeftAlt: return "L-ALT";
            case Key.RightAlt: return "R-ALT";
            case Key.Tab: return "TAB";
            case Key.Enter: return "ENTER";
            case Key.Escape: return "ESC";
            case Key.Backspace: return "BKSP";
            default:
                string s = key.ToString();
                if (s.StartsWith("Digit")) return s.Substring(5);
                if (s.StartsWith("Numpad")) return "NUM" + s.Substring(6);
                return s.ToUpperInvariant();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UpdateKeyIndicatorDisplay();
    }
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureQTEReferences();
        UpdateKeyIndicatorDisplay();
        SetBlackout(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void EnsureQTEReferences()
    {
        if (struggleBarFillImage == null && heatStruggleBar != null)
        {
            struggleBarFillImage = heatStruggleBar.barImage;
        }
    }

    /// <summary>
    /// Starts the rapid-mash struggle QTE with pro-game text pulsing and dynamic struggle filling.
    /// </summary>
    public IEnumerator RunPriestStruggleRoutine(
        float maxDuration,
        Func<bool> isPossessedCheck,
        Action<bool> onComplete,
        Key overrideKey = Key.None)
    {
        if (overrideKey != Key.None)
        {
            assignedResistKey = overrideKey;
        }

        EnsureQTEReferences();
        UpdateKeyIndicatorDisplay();

        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(true);
        }

        // Nudge blackout possess message upward so it doesn't overlap the center struggle QTE
        Vector2 origPossessMsgPos = Vector2.zero;
        bool didNudgeMessage = false;
        if (possessMessageText != null)
        {
            origPossessMsgPos = possessMessageText.rectTransform.anchoredPosition;
            if (Mathf.Abs(origPossessMsgPos.y) < 50f)
            {
                possessMessageText.rectTransform.anchoredPosition = new Vector2(origPossessMsgPos.x, 90f);
                didNudgeMessage = true;
            }
        }

        Vector3 baseKeyScale = keyIndicatorPunchTarget != null ? keyIndicatorPunchTarget.localScale : Vector3.one;
        RectTransform barRect = heatStruggleBar != null ? heatStruggleBar.GetComponent<RectTransform>() : null;
        Vector2 origBarPos = barRect != null ? barRect.anchoredPosition : Vector2.zero;

        float currentProgress = Mathf.Clamp(initialStruggle, 10f, 90f);
        float displayedProgress = currentProgress;
        float elapsed = 0f;
        bool won = false;

        while (elapsed < maxDuration)
        {
            if (isPossessedCheck != null && !isPossessedCheck())
            {
                break;
            }

            float dt = Time.deltaTime;
            elapsed += dt;

            // 1. Natural demonic decay pulling the bar down
            currentProgress -= demonDecayPerSecond * dt;

            // 2. Rapid button mash detection dynamically checking assigned key
            bool mashed = false;

            if (Keyboard.current != null)
            {
                var keyControl = Keyboard.current[assignedResistKey];
                if (keyControl != null && keyControl.wasPressedThisFrame)
                {
                    mashed = true;
                }
                else if (Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    mashed = true;
                }
            }
            else
            {
                if (Input.GetKeyDown(fallbackKeyCode) || Input.GetKeyDown(KeyCode.Space))
                {
                    mashed = true;
                }
            }

            if (Gamepad.current != null)
            {
                if (Gamepad.current.buttonWest.wasPressedThisFrame ||
                    Gamepad.current.buttonSouth.wasPressedThisFrame)
                {
                    mashed = true;
                }
            }

            if (mashed)
            {
                currentProgress += playerBoostPerMash;
                TriggerKeyPunch();
            }

            currentProgress = Mathf.Clamp(currentProgress, 0f, 100f);

            // 3. Visual Smoothing and Struggle Jitter
            displayedProgress = Mathf.Lerp(displayedProgress, currentProgress, dt * fillCatchupSpeed);

            float dangerFactor = 1f - (displayedProgress / 100f);
            float strainJitter = (Mathf.PerlinNoise(Time.time * 26f, 0.2f) - 0.5f) * 2f * tensionJitterIntensity * Mathf.Lerp(0.4f, 1.8f, dangerFactor);
            float visualFill = Mathf.Clamp(displayedProgress + strainJitter, 0f, 100f);

            Color currentColor;
            if (displayedProgress < 50f)
            {
                float t = displayedProgress / 50f;
                currentColor = Color.Lerp(losingColor, strugglingColor, t);
            }
            else
            {
                float t = (displayedProgress - 50f) / 50f;
                currentColor = Color.Lerp(strugglingColor, winningColor, t);
            }

            if (enableBarStrainShake && barRect != null)
            {
                float barShake = (displayedProgress < 35f) ? (Mathf.Sin(Time.time * 42f) * Mathf.Lerp(0f, 2.8f, (35f - displayedProgress) / 35f)) : 0f;
                barRect.anchoredPosition = new Vector2(origBarPos.x + barShake, origBarPos.y);
            }

            // 4. Text Pulsing and Breathing Animation
            float pulseFreq = (displayedProgress < 35f) ? 11.5f : (displayedProgress < 65f ? 6.5f : 3.5f);
            float pulseWave = Mathf.Sin(Time.time * pulseFreq);

            if (mashTextPulse != null)
            {
                mashTextPulse.SetSpeed(pulseFreq);
                if (displayedProgress < 35f)
                    mashTextPulse.SetColor(Color.Lerp(Color.white, losingColor, (pulseWave + 1f) * 0.45f));
                else
                    mashTextPulse.SetColor(Color.white);
            }

            if (actionTextPulse != null)
            {
                actionTextPulse.SetSpeed(pulseFreq);
                if (displayedProgress < 35f)
                    actionTextPulse.SetColor(Color.Lerp(Color.white, losingColor, (pulseWave + 1f) * 0.45f));
                else
                    actionTextPulse.SetColor(Color.white);
            }

            if (glowImage != null)
            {
                float glowAlpha = Mathf.Lerp(0.15f, 0.45f, (pulseWave + 1f) * 0.5f);
                glowImage.color = new Color(currentColor.r, currentColor.g, currentColor.b, glowAlpha);
            }

            // 5. Update Progress Bar and Fill Elements
            if (heatStruggleBar != null)
            {
                heatStruggleBar.currentValue = visualFill;
                heatStruggleBar.UpdateUI();
            }

            if (struggleBarFillImage != null)
            {
                struggleBarFillImage.color = currentColor;
            }
            else if (heatStruggleBar != null && heatStruggleBar.barImage != null)
            {
                heatStruggleBar.barImage.color = currentColor;
            }

            if (currentProgress >= 100f)
            {
                won = true;
                break;
            }

            if (currentProgress <= 0f)
            {
                won = false;
                break;
            }

            yield return null;
        }

        // Outcome flourish
        if (won)
        {
            if (heatStruggleBar != null && heatStruggleBar.barImage != null)
                heatStruggleBar.barImage.color = winningColor;
            yield return new WaitForSeconds(0.40f);
        }
        else
        {
            if (heatStruggleBar != null && heatStruggleBar.barImage != null)
                heatStruggleBar.barImage.color = losingColor;
            yield return new WaitForSeconds(0.40f);
        }

        if (keyIndicatorPunchTarget != null)
        {
            keyIndicatorPunchTarget.localScale = baseKeyScale;
        }

        if (barRect != null)
        {
            barRect.anchoredPosition = origBarPos;
        }

        if (didNudgeMessage && possessMessageText != null)
        {
            possessMessageText.rectTransform.anchoredPosition = origPossessMsgPos;
        }

        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(false);
        }

        onComplete?.Invoke(won);
    }

    private void TriggerKeyPunch()
    {
        if (_punchCoroutine != null) StopCoroutine(_punchCoroutine);
        _punchCoroutine = StartCoroutine(KeyPunchRoutine());
    }

    private IEnumerator KeyPunchRoutine()
    {
        Vector3 baseKeyScale = Vector3.one;
        Vector3 punchScale = Vector3.one * 1.32f;

        if (keyIndicatorPunchTarget != null)
        {
            keyIndicatorPunchTarget.localScale = punchScale;
        }

        if (keyIndicatorText != null)
        {
            keyIndicatorText.transform.localScale = punchScale;
        }

        if (glowImage != null)
        {
            Color c = glowImage.color;
            glowImage.color = new Color(c.r, c.g, c.b, 0.85f);
        }

        if (mashTextPulse != null)
        {
            mashTextPulse.TriggerPunch(1.20f);
        }

        float t = 0f;
        float duration = 0.12f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;

            if (keyIndicatorPunchTarget != null)
            {
                keyIndicatorPunchTarget.localScale = Vector3.Lerp(punchScale, baseKeyScale, ratio);
            }

            if (keyIndicatorText != null)
            {
                keyIndicatorText.transform.localScale = Vector3.Lerp(punchScale, baseKeyScale, ratio);
            }

            yield return null;
        }

        if (keyIndicatorPunchTarget != null)
        {
            keyIndicatorPunchTarget.localScale = baseKeyScale;
        }

        if (keyIndicatorText != null)
        {
            keyIndicatorText.transform.localScale = baseKeyScale;
        }
    }

    public void UpdateStruggleUI(float progressPercent, float timeLeft)
    {
        if (heatStruggleBar != null)
        {
            heatStruggleBar.currentValue = progressPercent;
            heatStruggleBar.UpdateUI();
        }

        Color targetColor = (progressPercent >= 65f) ? winningColor : (progressPercent <= 35f ? losingColor : strugglingColor);

        if (struggleBarFillImage != null)
        {
            struggleBarFillImage.color = targetColor;
        }
        else if (heatStruggleBar != null && heatStruggleBar.barImage != null)
        {
            heatStruggleBar.barImage.color = targetColor;
        }
    }

    public void ShowRejectionPrompt(float durationSeconds)
    {
        EnsureQTEReferences();
        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(true);
            UpdateStruggleUI(initialStruggle, durationSeconds);
        }
    }

    public void HideRejectionPrompt()
    {
        if (struggleQTERoot != null)
        {
            struggleQTERoot.SetActive(false);
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
    }
}
