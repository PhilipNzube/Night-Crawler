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
    [Tooltip("Health fraction below which blood starts appearing (1.0 = 100% HP, 0.6 = 60% HP).")]
    [Range(0f, 1f)] public float bloodStartThreshold = 1.0f;

    [Tooltip("Maximum opacity/alpha of the blood screen when player is at 0 HP.")]
    [Range(0f, 1f)] public float maxBloodAlpha = 0.9f;

    [Tooltip("Speed at which the blood overlay fades in or out when taking damage or healing.")]
    public float fadeSpeed = 4f;

    [Header("Low Health Pulse")]
    [Tooltip("Enable a heartbeat pulsing effect when critically low on health.")]
    public bool enablePulse = true;

    [Tooltip("Health fraction below which the heartbeat pulse activates (e.g. 0.35 = 35% HP).")]
    [Range(0f, 1f)] public float pulseThreshold = 0.35f;

    [Tooltip("Frequency/speed of the heartbeat pulse.")]
    public float pulseSpeed = 4f;

    [Tooltip("Intensity fluctuation of the pulse.")]
    public float pulseIntensity = 0.15f;

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

        localPlayer.TryGetComponent<TargetHealth>(out _localTargetHealth);
        localPlayer.TryGetComponent<HealthSystem>(out _localHealthSystem);

        if (_localTargetHealth != null)
        {
            _maxHealth = _localTargetHealth.MaxHealth;
            _localTargetHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
            _localTargetHealth.maxHealth.OnValueChanged     += OnTargetHealthChanged;
            UpdateHealthFraction(_localTargetHealth.CurrentHealth, _maxHealth);
            _isBound = true;
        }
        else if (_localHealthSystem != null)
        {
            _maxHealth = _localHealthSystem.MaxHealth > 0 ? _localHealthSystem.MaxHealth : 100f;
            _localHealthSystem.OnHealthChanged += OnHealthSystemChanged;
            UpdateHealthFraction(_localHealthSystem.CurrentHealth, _maxHealth);
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

        if (_currentHealthFraction >= bloodStartThreshold)
        {
            _targetAlpha = 0f;
        }
        else
        {
            // Maps fraction (bloodStartThreshold -> 0) to alpha (0 -> maxBloodAlpha)
            float t = 1f - (_currentHealthFraction / Mathf.Max(0.001f, bloodStartThreshold));
            _targetAlpha = Mathf.Clamp01(t * maxBloodAlpha);
        }
    }

    // =========================================================================
    //  Rendering & Animation
    // =========================================================================
    private void UpdateOverlayAlpha()
    {
        if (bloodImage == null && canvasGroup == null) return;

        // Smooth interpolation
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, Time.deltaTime * fadeSpeed);

        float renderAlpha = _currentAlpha;

        // Pulse heartbeat effect when health is critical
        if (enablePulse && _currentHealthFraction <= pulseThreshold && _targetAlpha > 0.01f)
        {
            float pulse = (Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f) * pulseIntensity;
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
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
        }
    }
}
