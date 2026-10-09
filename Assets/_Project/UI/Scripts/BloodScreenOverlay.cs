using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Dedicated visual overlay component for the Blood Screen / Damage Vignette effect.
///
/// Responsibilities:
/// - Controls the opacity and animation of the blood screen image.
/// - Decoupled from PlayerHUD (can be placed on the blood Image GameObject directly or anywhere on Canvas).
/// - Reactively listens to the local player's HealthSystem / TargetHealth.
/// - Features customizable health thresholds, smooth alpha interpolation, and low-health heartbeat pulsing.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BloodScreenOverlay : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector Configuration
    // -------------------------------------------------------------------------
    [Header("Visual References")]
    [Tooltip("The Image component displaying the blood texture. If left empty, will look on this GameObject.")]
    public Image bloodImage;

    [Tooltip("Optional CanvasGroup if alpha is controlled via CanvasGroup instead of Image color.")]
    public CanvasGroup canvasGroup;

    [Header("Blur / Distortion")]
    [Tooltip("Optional Image overlay displaying blur texture (e.g. Blood_Blur_1 / Blur Camera Output / UI blur).")]
    public Image blurOverlayImage;

    [Tooltip("Optional CanvasGroup specifically controlling the blur layer opacity.")]
    public CanvasGroup blurCanvasGroup;

    [Tooltip("Maximum opacity of the blur effect when health is near zero.")]
    [Range(0f, 1f)] public float maxBlurAlpha = 0.85f;

    [Header("Health Thresholds")]
    [Tooltip("Health fraction below which blood starts appearing (default 0.85 = starts at 85% HP).")]
    [Range(0f, 1f)] public float bloodStartThreshold = 0.85f;

    [Tooltip("Maximum opacity/alpha of the blood screen when player is at 0 HP.")]
    [Range(0f, 1f)] public float maxBloodAlpha = 0.85f;

    [Tooltip("Exponent curve controlling how slowly blood builds up. Higher = slower initial build up.")]
    [Range(1f, 4f)] public float progressionCurvePower = 2.2f;

    [Tooltip("Speed at which the blood overlay fades in or out.")]
    public float fadeSpeed = 1.5f;

    [Header("Intense Low Health Pulse")]
    [Tooltip("Enable a heartbeat pulsing effect when critically low on health.")]
    public bool enablePulse = true;

    [Tooltip("Health fraction below which the heartbeat pulse activates (e.g. 0.30 = 30% HP).")]
    [Range(0f, 1f)] public float pulseThreshold = 0.30f;

    [Tooltip("Base frequency/speed of the heartbeat pulse.")]
    public float pulseSpeed = 4.2f;

    [Tooltip("Intensity fluctuation of the pulse alpha.")]
    [Range(0.05f, 0.6f)]
    public float pulseIntensity = 0.35f;

    [Tooltip("Double-thump physiological heartbeat simulation (lub-dub bi-phasic pulse).")]
    public bool useDoubleBeatPulse = true;

    [Tooltip("Dynamic breathing/zoom scale bounce on the RectTransform during heartbeat.")]
    public bool enableScalePulse = true;

    [Tooltip("Maximum scale bounce during pulse.")]
    public float maxPulseScale = 1.05f;

    [Header("Audio Effects (Heartbeat & Muffled Audio)")]
    [Tooltip("Heartbeat audio clip (e.g. Heartbeat.mp3 / Heartbeat.wav in Audio folder).")]
    public AudioClip heartbeatClip;

    [Tooltip("Dedicated AudioSource for the looping heartbeat. If left null, one is automatically created.")]
    public AudioSource heartbeatAudioSource;

    [Tooltip("Max volume of the heartbeat at critical low health.")]
    [Range(0f, 1f)] public float maxHeartbeatVolume = 0.95f;

    [Tooltip("Pitch scaling range as health drops towards zero (faster heart rate as player approaches death). Up to 2.05x for racing tachycardia!")]
    public Vector2 heartbeatPitchRange = new Vector2(0.95f, 2.05f);

    [Tooltip("Enable realistic muffled audio (low pass filter) when low on health.")]
    public bool enableMuffledAudio = true;

    [Tooltip("AudioLowPassFilter component. Wire this to the AudioListener on your Main/Player Camera.")]
    public AudioLowPassFilter lowPassFilter;

    [Tooltip("Cutoff frequency when healthy (22000Hz = full audio clarity).")]
    public float normalCutoffFrequency = 22000f;

    [Tooltip("Muffled cutoff frequency at near 0 HP (realistic underwater / shell-shock rumble, e.g. 550-700Hz).")]
    public float criticalCutoffFrequency = 650f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private TargetHealth  _localTargetHealth;
    private HealthSystem  _localHealthSystem;
    private RectTransform _rectTransform;
    private Vector3       _initialScale          = Vector3.one;
    private bool          _isBound               = false;
    private float         _maxHealth             = 100f;
    private float         _currentHealthFraction = 1f;
    private float         _targetAlpha           = 0f;
    private float         _currentAlpha          = 0f;
    private bool          _createdAudioSource    = false;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        if (_rectTransform != null) _initialScale = _rectTransform.localScale;

        // Auto-assign references if on the same GameObject
        if (bloodImage == null)  bloodImage  = GetComponent<Image>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        SetupHeartbeatAudioSource();

        // Start completely invisible
        SetAlphaImmediate(0f);
    }

    private void OnEnable()
    {
        if (bloodImage == null)  bloodImage  = GetComponent<Image>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
        if (_rectTransform != null) _initialScale = _rectTransform.localScale;

        SetupHeartbeatAudioSource();
        if (!_isBound) TryBindToLocalPlayer();
    }

    private void Start()
    {
        if (enableMuffledAudio && lowPassFilter == null)
        {
            Debug.LogWarning("[BloodScreenOverlay] Low Pass Filter is not assigned. To enable muffled audio when health is low: Add an AudioLowPassFilter component to your Camera with AudioListener and drag it into BloodScreenOverlay's 'Low Pass Filter' field in the Inspector.");
        }
    }

    private void Update()
    {
        // Smoothly update the visual alpha every frame
        UpdateOverlayAlpha();

        // Update audio (heartbeat & low-pass muffled filter)
        UpdateLowHealthAudio();

        if (!_isBound)
        {
            TryBindToLocalPlayer();
        }
    }

    private void OnDisable()
    {
        ResetAudioAndEffects();
    }

    private void OnDestroy()
    {
        UnbindFromPlayer();
        ResetAudioAndEffects();
    }

    private void SetupHeartbeatAudioSource()
    {
        if (heartbeatAudioSource == null)
        {
            heartbeatAudioSource = GetComponent<AudioSource>();
            if (heartbeatAudioSource == null)
            {
                heartbeatAudioSource = gameObject.AddComponent<AudioSource>();
                _createdAudioSource = true;
            }
        }

        if (heartbeatAudioSource != null)
        {
            heartbeatAudioSource.playOnAwake = false;
            heartbeatAudioSource.loop = true;
            heartbeatAudioSource.spatialBlend = 0f; // 2D Stereo sound straight in player's ears
            if (heartbeatClip != null && heartbeatAudioSource.clip == null)
            {
                heartbeatAudioSource.clip = heartbeatClip;
            }
        }
    }

    private void ResetAudioAndEffects()
    {
        if (heartbeatAudioSource != null && heartbeatAudioSource.isPlaying)
        {
            heartbeatAudioSource.Stop();
        }

        if (lowPassFilter != null)
        {
            lowPassFilter.cutoffFrequency = normalCutoffFrequency;
            lowPassFilter.enabled = false;
        }

        if (_rectTransform != null)
        {
            _rectTransform.localScale = _initialScale;
        }
    }

    // =========================================================================
    //  Local Player Binding
    // =========================================================================
    private void TryBindToLocalPlayer()
    {
        if (NetworkManager.Singleton == null) return;

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        UnbindFromPlayer();

        localPlayer.TryGetComponent<HealthSystem>(out _localHealthSystem);
        localPlayer.TryGetComponent<TargetHealth>(out _localTargetHealth);

        if (_localHealthSystem != null)
        {
            _localHealthSystem.OnHealthChanged += OnHealthSystemChanged;
        }
        if (_localTargetHealth != null)
        {
            _localTargetHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
            _localTargetHealth.maxHealth.OnValueChanged     += OnTargetHealthChanged;
        }

        RefreshCurrentHealth();
        _isBound = true;
    }

    private void UnbindFromPlayer()
    {
        if (_localTargetHealth != null)
        {
            _localTargetHealth.currentHealth.OnValueChanged -= OnTargetHealthChanged;
            _localTargetHealth.maxHealth.OnValueChanged     -= OnTargetHealthChanged;
        }

        if (_localHealthSystem != null)
            _localHealthSystem.OnHealthChanged -= OnHealthSystemChanged;
    }

    /// <summary>
    /// Dynamically binds the blood overlay to a target (or re-binds to local player if target is null).
    /// </summary>
    public void BindToTarget(GameObject target)
    {
        UnbindFromPlayer();

        if (target == null)
        {
            _isBound = false;
            TryBindToLocalPlayer();
            return;
        }

        target.TryGetComponent<HealthSystem>(out _localHealthSystem);
        target.TryGetComponent<TargetHealth>(out _localTargetHealth);

        if (_localHealthSystem != null)
        {
            _localHealthSystem.OnHealthChanged += OnHealthSystemChanged;
        }
        if (_localTargetHealth != null)
        {
            _localTargetHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
            _localTargetHealth.maxHealth.OnValueChanged     += OnTargetHealthChanged;
        }

        RefreshCurrentHealth();
        _isBound = true;
    }

    // =========================================================================
    //  Health Change Handlers
    // =========================================================================
    private void OnTargetHealthChanged(float previous, float current)
    {
        RefreshCurrentHealth();
    }

    private void OnHealthSystemChanged(float current, float max)
    {
        RefreshCurrentHealth();
    }

    private void RefreshCurrentHealth()
    {
        float current = 100f;
        float max = 100f;

        bool isCorpse = (_localTargetHealth != null && _localTargetHealth.isCorpse.Value) ||
                        (_localHealthSystem != null && _localHealthSystem.IsDead);

        if (isCorpse)
        {
            current = 0f;
            max = _localTargetHealth != null ? _localTargetHealth.MaxHealth : (_localHealthSystem != null ? _localHealthSystem.MaxHealth : 100f);
        }
        else if (_localTargetHealth != null && _localHealthSystem != null)
        {
            max = Mathf.Max(_localTargetHealth.MaxHealth, _localHealthSystem.MaxHealth);
            // If the character is alive, use the active higher health value so UI never desyncs to 0
            current = Mathf.Max(_localTargetHealth.CurrentHealth, _localHealthSystem.CurrentHealth);
            if (current <= 0f) current = 1f;
        }
        else if (_localHealthSystem != null)
        {
            current = _localHealthSystem.CurrentHealth;
            max = _localHealthSystem.MaxHealth;
        }
        else if (_localTargetHealth != null)
        {
            current = _localTargetHealth.CurrentHealth;
            max = _localTargetHealth.MaxHealth;
        }

        _maxHealth = max > 0 ? max : 100f;
        UpdateHealthFraction(current, _maxHealth);
    }

    /// <summary>
    /// Updates the target alpha based on current health and threshold.
    /// Can also be called externally to drive the overlay directly.
    /// </summary>
    public void UpdateHealthFraction(float currentHealth, float maxHealth)
    {
        _currentHealthFraction = Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));

        // Use effective threshold (clamps so serialized 1.0f in scene behaves as 0.85f)
        float threshold = Mathf.Clamp(bloodStartThreshold, 0.4f, 0.85f);

        if (_currentHealthFraction >= threshold)
        {
            _targetAlpha = 0f;
        }
        else
        {
            // Maps fraction (threshold -> 0) to normalized progression (0.0 -> 1.0)
            float t = 1f - (_currentHealthFraction / threshold);
            t = Mathf.Clamp01(t);

            // Progressive power curve so blood rises slowly in proportion to health lost
            float curved = Mathf.Pow(t, progressionCurvePower);
            _targetAlpha = Mathf.Clamp01(curved * maxBloodAlpha);
        }
    }

    // =========================================================================
    //  Rendering & Animation
    // =========================================================================
    private void UpdateOverlayAlpha()
    {
        if (bloodImage == null && canvasGroup == null) return;

        // Smoothly follow target alpha at a natural pace matching health loss
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, Time.deltaTime * fadeSpeed);

        float renderAlpha = _currentAlpha;
        float pulseWave = 0f;
        float pulseScale = 0f;

        // Heartbeat pulsing activates only when critically low on health (< pulseThreshold)
        if (enablePulse && _currentHealthFraction <= pulseThreshold && _targetAlpha > 0.05f)
        {
            pulseScale = Mathf.Clamp01((pulseThreshold - _currentHealthFraction) / pulseThreshold);

            // Heart rate accelerates aggressively as health drops closer to death (up to 2.2x speed)
            float currentRate = pulseSpeed * (1f + Mathf.Pow(pulseScale, 0.7f) * 1.2f);

            if (useDoubleBeatPulse)
            {
                // Physiological bi-phasic lub-dub waveform
                float phase = (Time.time * currentRate) % (Mathf.PI * 2f);
                float lub = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(phase)), 4f);
                float dub = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(phase * 1.8f - 0.4f)), 6f) * 0.65f;
                pulseWave = Mathf.Clamp01(lub + dub);
            }
            else
            {
                pulseWave = Mathf.Sin(Time.time * currentRate) * 0.5f + 0.5f;
            }

            float pulse = pulseWave * pulseIntensity * pulseScale;
            renderAlpha = Mathf.Clamp01(_currentAlpha + pulse);

            // Scale zoom pulsation
            if (enableScalePulse && _rectTransform != null)
            {
                float targetScaleMult = 1f + (pulseWave * (maxPulseScale - 1f) * pulseScale);
                _rectTransform.localScale = Vector3.Lerp(_rectTransform.localScale, _initialScale * targetScaleMult, Time.deltaTime * 12f);
            }
        }
        else if (_rectTransform != null)
        {
            _rectTransform.localScale = Vector3.MoveTowards(_rectTransform.localScale, _initialScale, Time.deltaTime * 2f);
        }

        ApplyAlpha(renderAlpha);
    }

    public void SetAlphaImmediate(float alpha)
    {
        _currentAlpha = alpha;
        _targetAlpha  = alpha;
        ApplyAlpha(alpha);
    }

    private void ApplyAlpha(float alpha)
    {
        if (bloodImage != null)
        {
            Color c = bloodImage.color;
            c.a = alpha;
            bloodImage.color = c;
            bloodImage.enabled = (alpha > 0.001f);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
        }

        // Synchronize blur overlay with blood alpha
        if (blurOverlayImage != null)
        {
            Color bc = blurOverlayImage.color;
            bc.a = alpha * maxBlurAlpha;
            blurOverlayImage.color = bc;
            blurOverlayImage.enabled = (bc.a > 0.001f);
        }

        if (blurCanvasGroup != null)
        {
            blurCanvasGroup.alpha = alpha * maxBlurAlpha;
        }
    }

    // =========================================================================
    //  Low-Health Audio Feedback (Heartbeat Loop & Low-Pass Muffled Filter)
    // =========================================================================
    private void UpdateLowHealthAudio()
    {
        bool isCritical = _currentHealthFraction <= pulseThreshold && _targetAlpha > 0.05f;
        float criticalFraction = isCritical ? Mathf.Clamp01((pulseThreshold - _currentHealthFraction) / pulseThreshold) : 0f;

        // 1. Looping Heartbeat Sound — accelerates drastically as health nears death
        if (heartbeatAudioSource != null && (heartbeatClip != null || heartbeatAudioSource.clip != null))
        {
            if (heartbeatAudioSource.clip == null && heartbeatClip != null)
            {
                heartbeatAudioSource.clip = heartbeatClip;
            }

            if (isCritical)
            {
                if (!heartbeatAudioSource.isPlaying && heartbeatAudioSource.clip != null)
                {
                    heartbeatAudioSource.Play();
                }

                float targetVol = Mathf.Lerp(0.15f, maxHeartbeatVolume, criticalFraction);
                heartbeatAudioSource.volume = Mathf.MoveTowards(heartbeatAudioSource.volume, targetVol, Time.deltaTime * 2.5f);

                // Accelerate pitch non-linearly so heart races frantic and loud in single-digit HP
                float pitchCurve = Mathf.Pow(criticalFraction, 0.75f);
                float targetPitch = Mathf.Lerp(heartbeatPitchRange.x, heartbeatPitchRange.y, pitchCurve);
                heartbeatAudioSource.pitch = Mathf.MoveTowards(heartbeatAudioSource.pitch, targetPitch, Time.deltaTime * 3.5f);
            }
            else
            {
                if (heartbeatAudioSource.isPlaying)
                {
                    heartbeatAudioSource.volume = Mathf.MoveTowards(heartbeatAudioSource.volume, 0f, Time.deltaTime * 3.5f);
                    if (heartbeatAudioSource.volume <= 0.01f)
                    {
                        heartbeatAudioSource.Stop();
                    }
                }
            }
        }

        // 2. Realistic Low-Pass Muffled Audio Filter (Shell-Shock / Underwater Feeling)
        if (enableMuffledAudio && lowPassFilter != null)
        {
            if (isCritical)
            {
                if (!lowPassFilter.enabled)
                {
                    lowPassFilter.enabled = true;
                    lowPassFilter.lowpassResonanceQ = 1.35f; // Gives that muffled ear pressure resonance
                }

                float targetCutoff = Mathf.Lerp(normalCutoffFrequency, criticalCutoffFrequency, criticalFraction);
                lowPassFilter.cutoffFrequency = Mathf.MoveTowards(lowPassFilter.cutoffFrequency, targetCutoff, Time.deltaTime * 14000f);
            }
            else
            {
                if (lowPassFilter.enabled)
                {
                    lowPassFilter.cutoffFrequency = Mathf.MoveTowards(lowPassFilter.cutoffFrequency, normalCutoffFrequency, Time.deltaTime * 16000f);
                    if (lowPassFilter.cutoffFrequency >= normalCutoffFrequency - 50f)
                    {
                        lowPassFilter.cutoffFrequency = normalCutoffFrequency;
                        lowPassFilter.enabled = false;
                    }
                }
            }
        }
    }
}
