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

    [Header("Health Thresholds")]
    [Tooltip("Health fraction below which blood starts appearing (default 0.85 = starts at 85% HP).")]
    [Range(0f, 1f)] public float bloodStartThreshold = 0.85f;

    [Tooltip("Maximum opacity/alpha of the blood screen when player is at 0 HP.")]
    [Range(0f, 1f)] public float maxBloodAlpha = 0.8f;

    [Tooltip("Exponent curve controlling how slowly blood builds up. Higher = slower initial build up.")]
    [Range(1f, 4f)] public float progressionCurvePower = 2.2f;

    [Tooltip("Speed at which the blood overlay fades in or out.")]
    public float fadeSpeed = 1.5f;

    [Header("Low Health Pulse")]
    [Tooltip("Enable a heartbeat pulsing effect when critically low on health.")]
    public bool enablePulse = true;

    [Tooltip("Health fraction below which the heartbeat pulse activates (e.g. 0.25 = 25% HP).")]
    [Range(0f, 1f)] public float pulseThreshold = 0.25f;

    [Tooltip("Frequency/speed of the heartbeat pulse.")]
    public float pulseSpeed = 3.5f;

    [Tooltip("Intensity fluctuation of the pulse.")]
    public float pulseIntensity = 0.12f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private TargetHealth _localTargetHealth;
    private HealthSystem _localHealthSystem;
    private bool         _isBound               = false;
    private float        _maxHealth             = 100f;
    private float        _currentHealthFraction = 1f;
    private float        _targetAlpha           = 0f;
    private float        _currentAlpha          = 0f;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    private void Awake()
    {
        // Auto-assign references if on the same GameObject
        if (bloodImage == null)  bloodImage  = GetComponent<Image>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        // Start completely invisible
        SetAlphaImmediate(0f);
    }

    private void OnEnable()
    {
        if (bloodImage == null)  bloodImage  = GetComponent<Image>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (!_isBound) TryBindToLocalPlayer();
    }

    private void Update()
    {
        // Smoothly update the visual alpha every frame
        UpdateOverlayAlpha();

        if (!_isBound)
        {
            TryBindToLocalPlayer();
        }
    }

    private void OnDestroy()
    {
        UnbindFromPlayer();
    }

    // =========================================================================
    //  Local Player Binding
    // =========================================================================
    private void TryBindToLocalPlayer()
    {
        if (NetworkManager.Singleton == null) return;

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        localPlayer.TryGetComponent<HealthSystem>(out _localHealthSystem);
        localPlayer.TryGetComponent<TargetHealth>(out _localTargetHealth);

        if (_localHealthSystem != null)
        {
            _maxHealth = _localHealthSystem.MaxHealth > 0 ? _localHealthSystem.MaxHealth : 100f;
            _localHealthSystem.OnHealthChanged += OnHealthSystemChanged;
            UpdateHealthFraction(_localHealthSystem.CurrentHealth, _maxHealth);
            _isBound = true;
        }
        else if (_localTargetHealth != null)
        {
            _maxHealth = _localTargetHealth.MaxHealth;
            _localTargetHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
            _localTargetHealth.maxHealth.OnValueChanged     += OnTargetHealthChanged;
            UpdateHealthFraction(_localTargetHealth.CurrentHealth, _maxHealth);
            _isBound = true;
        }
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

    // =========================================================================
    //  Health Change Handlers
    // =========================================================================
    private void OnTargetHealthChanged(float previous, float current)
    {
        if (_localTargetHealth != null)
        {
            _maxHealth = _localTargetHealth.MaxHealth;
            UpdateHealthFraction(_localTargetHealth.CurrentHealth, _maxHealth);
        }
    }

    private void OnHealthSystemChanged(float current, float max)
    {
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

        // Heartbeat pulsing activates only when critically low on health (< 25%)
        if (enablePulse && _currentHealthFraction <= pulseThreshold && _targetAlpha > 0.05f)
        {
            float pulseScale = Mathf.Clamp01((pulseThreshold - _currentHealthFraction) / pulseThreshold);
            float pulse = (Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f) * pulseIntensity * pulseScale;
            renderAlpha = Mathf.Clamp01(_currentAlpha + pulse);
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
    }
}
