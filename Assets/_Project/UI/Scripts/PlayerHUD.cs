using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Manages the local player's in-game HUD display only.
///
/// One PlayerHUD instance lives on the HUD Canvas. It listens to the local
/// player's NetworkVariables and updates all UI elements reactively —
/// no per-frame polling of scene objects.
///
/// Setup:
///   1. Add this script to your HUD Canvas root (or a child).
///   2. Wire all [Header] fields in the Inspector.
///   3. This script will automatically bind to the local player when the match starts.
///
/// OCP: Adding a new stat (e.g. sanity) only requires adding a field and one
/// binding line in BindToPlayer() — no existing logic changes.
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — HUD Root
    // -------------------------------------------------------------------------
    [Header("HUD Root")]
    [Tooltip("The root GameObject of the entire HUD canvas. " +
             "Assign the Canvas or the root panel here — it will be hidden when paused.")]
    public GameObject hudRoot;

    [Header("Health")]
    [Tooltip("Slider that displays the player's current health.")]
    public Slider healthSlider;

    [Tooltip("Fills the health bar with color (optional gradient tinting done via script).")]
    public Image healthFill;

    [Tooltip("Invert the fill fraction if using a slider that fills in reverse (e.g. 1 - fraction).")]
    public bool invertHealthBar = false;

    [Tooltip("Tint the health bar from red to green. Set to false for Bloodlines UI since its sprites are already textured blood-red (tinting green blacks them out).")]
    public bool tintHealthBarWithColor = false;

    [Tooltip("Displays current / max health as text, e.g. '75 / 100'.")]
    public TextMeshProUGUI healthText;

    [Header("Damage / Blood Overlay")]
    [Tooltip("Vignette blood screen overlay. If null, will auto-locate on HUDCanvas even if inactive.")]
    public BloodScreenOverlay bloodScreenOverlay;

    // -------------------------------------------------------------------------
    //  Inspector — Role
    // -------------------------------------------------------------------------
    [Header("Role")]
    [Tooltip("Displays the player's role: VENGEFUL SPIRIT or INVESTIGATOR.")]
    public TextMeshProUGUI roleLabel;

    // -------------------------------------------------------------------------
    //  Inspector — Investigator-Only Panel
    // -------------------------------------------------------------------------
    [Header("Investigator Panel (hidden for Vengeful Spirit)")]
    [Tooltip("Root GameObject for the Investigator weapon/ammo UI. Hidden for the Vengeful Spirit.")]
    public GameObject explorerPanel;

    [Tooltip("Displays current ammo count.")]
    public TextMeshProUGUI ammoText;

    [Tooltip("Displays current weapon name.")]
    public TextMeshProUGUI weaponText;

    // -------------------------------------------------------------------------
    //  Inspector — Vengeful Spirit-Only Panel
    // -------------------------------------------------------------------------
    [Header("Vengeful Spirit Panel (hidden for Investigator)")]
    [Tooltip("Root GameObject for Vengeful Spirit-specific UI (stealth prompt, taunt prompt, etc). Hidden for Investigators.")]
    public GameObject demonPanel;

    [Tooltip("Radial fill image that shows the stealth ability cooldown (0 = ready, 1 = on cooldown).")]
    public Image stealthCooldownFill;

    [Tooltip("Text hint shown when stealth is available (e.g. '[Q] Vanish').")]
    public TextMeshProUGUI stealthPromptText;

    // -------------------------------------------------------------------------
    //  Inspector — Match State
    // -------------------------------------------------------------------------
    [Header("Match State")]
    [Tooltip("Shown when waiting for the match to start.")]
    public GameObject waitingOverlay;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private TargetHealth          _localHealth;
    private HealthSystem          _localHealthSys;
    private InvestigatorCombatNet _localCombat;
    private GirlStealth           _localStealth;
    private bool                  _isBound    = false;
    private bool                  _isDemon    = false;
    private float                 _maxHealth  = 100f;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    private CanvasGroup           _hudCanvasGroup;

    void Awake()
    {
        // Subscribe to pause events across the entire lifecycle so unpausing always works
        PauseManager.OnPauseStateChanged += SetHUDVisible;

        // Auto-resolve missing health references if not wired in Inspector
        if (healthSlider == null)
            healthSlider = GetComponentInChildren<Slider>(true);

        if (healthSlider != null && healthFill == null && healthSlider.fillRect != null)
            healthFill = healthSlider.fillRect.GetComponent<Image>();

        // Auto-find BloodScreenOverlay (even if inactive in hierarchy) and activate it
        if (bloodScreenOverlay == null)
        {
            bloodScreenOverlay = FindFirstObjectByType<BloodScreenOverlay>(FindObjectsInactive.Include);
            if (bloodScreenOverlay == null)
            {
                bloodScreenOverlay = transform.root.GetComponentInChildren<BloodScreenOverlay>(true);
            }
        }
        if (bloodScreenOverlay != null && !bloodScreenOverlay.gameObject.activeSelf)
        {
            bloodScreenOverlay.gameObject.SetActive(true);
            Debug.Log("[PlayerHUD] Activated inactive BloodScreenOverlay on HUD Canvas.");
        }
    }

    void Start()
    {
        // Keep waiting overlay hidden when match starts
        SetWaitingState(false);

        if (hudRoot == null) hudRoot = gameObject;
        _hudCanvasGroup = hudRoot.GetComponent<CanvasGroup>();
    }

    private void SetHUDVisible(bool isPaused)
    {
        if (hudRoot != null)
        {
            // If hudRoot is this GameObject, use a CanvasGroup so PlayerHUD is NOT deactivated!
            // Deactivating the GameObject stops Update() and coroutines from running.
            if (hudRoot == gameObject)
            {
                if (_hudCanvasGroup == null)
                    _hudCanvasGroup = gameObject.AddComponent<CanvasGroup>();

                _hudCanvasGroup.alpha = isPaused ? 0f : 1f;
                _hudCanvasGroup.interactable = !isPaused;
                _hudCanvasGroup.blocksRaycasts = !isPaused;
            }
            else
            {
                hudRoot.SetActive(!isPaused);
            }
        }
    }

    void Update()
    {
        if (!_isBound)
        {
            TryBindToLocalPlayer();
            return;
        }

        RefreshHUD();
    }

    // =========================================================================
    //  Binding
    // =========================================================================

    /// <summary>
    /// Tries once per frame to find and bind to the local player object.
    /// Stops trying once successfully bound.
    /// </summary>
    private void TryBindToLocalPlayer()
    {
        if (NetworkManager.Singleton == null) return;

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        // Determine role
        _isDemon = localPlayer.TryGetComponent<GirlStealth>(out _localStealth);

        localPlayer.TryGetComponent<HealthSystem>(out _localHealthSys);
        localPlayer.TryGetComponent<TargetHealth>(out _localHealth);
        localPlayer.TryGetComponent<InvestigatorCombatNet>(out _localCombat);

        if (_localHealthSys != null)
        {
            _maxHealth = _localHealthSys.MaxHealth > 0 ? _localHealthSys.MaxHealth : 100f;
            _localHealthSys.OnHealthChanged += OnHealthSysChanged;
        }
        else if (_localHealth != null)
        {
            _maxHealth = _localHealth.MaxHealth;
            _localHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
            _localHealth.maxHealth.OnValueChanged     += OnTargetHealthChanged;
        }

        // Configure role-specific panels
        if (explorerPanel != null) explorerPanel.SetActive(!_isDemon);
        if (demonPanel     != null) demonPanel.SetActive(_isDemon);

        // Set role label
        if (roleLabel != null)
        {
            roleLabel.text  = _isDemon ? "VENGEFUL SPIRIT" : "INVESTIGATOR";
            roleLabel.color = _isDemon
                ? new Color(0.7f, 0.1f, 1f)   // Vengeful Spirit purple
                : new Color(0.2f, 0.8f, 1f);   // Investigator cyan
        }

        _isBound = true;
        SetWaitingState(false);

        // Immediately initialize health bar UI
        RefreshHealth();
    }

    private void OnDestroy()
    {
        PauseManager.OnPauseStateChanged -= SetHUDVisible;

        if (_localHealth != null)
        {
            _localHealth.currentHealth.OnValueChanged -= OnTargetHealthChanged;
            _localHealth.maxHealth.OnValueChanged     -= OnTargetHealthChanged;
        }

        if (_localHealthSys != null)
            _localHealthSys.OnHealthChanged -= OnHealthSysChanged;
    }

    private void OnTargetHealthChanged(float previous, float current)
    {
        RefreshHealth();
    }

    private void OnHealthSysChanged(float current, float max)
    {
        _maxHealth = max > 0 ? max : 100f;
        RefreshHealth();
    }

    // =========================================================================
    //  HUD Refresh
    // =========================================================================

    /// <summary>Polls synced NetworkVariables and pushes values to UI elements.</summary>
    private void RefreshHUD()
    {
        RefreshHealth();

        if (_isDemon)
            RefreshDemonPanel();
        else
            RefreshExplorerPanel();
    }

    private void RefreshHealth()
    {
        float current = 100f;
        float max = 100f;

        if (_localHealthSys != null)
        {
            current = _localHealthSys.CurrentHealth;
            max = _localHealthSys.MaxHealth;
        }
        else if (_localHealth != null)
        {
            current = _localHealth.CurrentHealth;
            max = _localHealth.MaxHealth;
        }

        _maxHealth = max > 0 ? max : 100f;
        float fraction = Mathf.Clamp01(current / _maxHealth);
        float displayFraction = invertHealthBar ? (1f - fraction) : fraction;

        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = 1f;
            healthSlider.value    = displayFraction;

            // Ensure the fill rect has vertical height so it doesn't collapse to 0 height
            if (healthSlider.fillRect != null)
            {
                Vector2 aMin = healthSlider.fillRect.anchorMin;
                Vector2 aMax = healthSlider.fillRect.anchorMax;
                if (aMax.y < 0.5f)
                {
                    healthSlider.fillRect.anchorMin = new Vector2(aMin.x, 0f);
                    healthSlider.fillRect.anchorMax = new Vector2(aMax.x, 1f);
                }

                if (healthFill == null)
                {
                    healthFill = healthSlider.fillRect.GetComponent<Image>();
                }
            }
        }

        if (healthFill != null)
        {
            // Bloodlines UI Slider 5 uses a Filled Image (Horizontal) to represent fill amount
            if (healthFill.type == Image.Type.Filled)
            {
                healthFill.fillAmount = displayFraction;
            }

            // Tint health bar: Bloodlines UI textures are already blood-red.
            // Tinting with Color.green zeroes out red pixels, making the bar black (invisible/empty) at 100% health!
            if (tintHealthBarWithColor)
            {
                healthFill.color = Color.Lerp(Color.red, Color.green, fraction);
            }
            else
            {
                healthFill.color = Color.white;
            }
        }

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(_maxHealth)}";

        // Push health updates to Blood Screen Overlay immediately
        if (bloodScreenOverlay != null)
        {
            bloodScreenOverlay.UpdateHealthFraction(current, _maxHealth);
        }
    }

    private void RefreshExplorerPanel()
    {
        if (_localCombat == null || ammoText == null) return;

        bool isGun = _localCombat.currentWeaponIndex.Value == 1;
        if (weaponText != null) weaponText.text = isGun ? "GUN" : "AXE";

        if (isGun)
        {
            int ammo = _localCombat.currentAmmo.Value;
            ammoText.text  = $"AMMO  {ammo}";
            ammoText.color = ammo <= 3 ? new Color(1f, 0.3f, 0.3f) : Color.white;
        }
        else
        {
            ammoText.text  = "──";
            ammoText.color = Color.gray;
        }
    }

    private void RefreshDemonPanel()
    {
        if (_localStealth == null) return;

        // Stealth cooldown fill (requires internal access — GirlStealth exposes CanTaunt/CanStealth publicly)
        // We read the public NetworkVariable to show active state
        bool stealthOn = _localStealth.IsStealthActive.Value;

        if (stealthPromptText != null)
        {
            stealthPromptText.text  = stealthOn 
                ? "VANISHED [Q] | [T] Manifest | [E] Possess | [B] Deals" 
                : "[Q] Vanish | [T] Manifest | [E] Possess | [B] Deals";
            stealthPromptText.color = stealthOn
                ? new Color(0.7f, 0.2f, 1f)  // Purple when active
                : Color.white;
        }

        // stealthCooldownFill driven by CanTaunt (re-use the same boolean gate)
        if (stealthCooldownFill != null)
            stealthCooldownFill.fillAmount = _localStealth.CanTaunt() ? 1f : 0f;
    }

    // =========================================================================
    //  Helpers
    // =========================================================================
    private void SetWaitingState(bool waiting)
    {
        if (waitingOverlay != null) waitingOverlay.SetActive(waiting);
    }
}
