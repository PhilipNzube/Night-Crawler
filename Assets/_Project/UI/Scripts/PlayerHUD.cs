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

    [Tooltip("Displays current / max health as text, e.g. '75 / 100'.")]
    public TextMeshProUGUI healthText;

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
    void Start()
    {
        // Keep waiting overlay hidden when match starts
        SetWaitingState(false);

        // If hudRoot is not assigned, fall back to this GameObject
        if (hudRoot == null) hudRoot = gameObject;
    }

    void OnEnable()
    {
        PauseManager.OnPauseStateChanged += SetHUDVisible;
    }

    void OnDisable()
    {
        PauseManager.OnPauseStateChanged -= SetHUDVisible;
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

        localPlayer.TryGetComponent<TargetHealth>(out _localHealth);
        localPlayer.TryGetComponent<HealthSystem>(out _localHealthSys);
        localPlayer.TryGetComponent<InvestigatorCombatNet>(out _localCombat);

        if (_localHealth != null)
        {
            _maxHealth = (_localHealth.stats != null && _localHealth.stats.maxHealth > 0)
                ? _localHealth.stats.maxHealth
                : 100f;
            _localHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
        }
        else if (_localHealthSys != null)
        {
            _maxHealth = _localHealthSys.MaxHealth > 0 ? _localHealthSys.MaxHealth : 100f;
            _localHealthSys.OnHealthChanged += OnHealthSysChanged;
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
        if (_localHealth != null)
            _localHealth.currentHealth.OnValueChanged -= OnTargetHealthChanged;

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
        float current = _maxHealth;

        if (_localHealth != null)
        {
            current = _localHealth.currentHealth.Value;
        }
        else if (_localHealthSys != null)
        {
            current = _localHealthSys.CurrentHealth;
        }

        float fraction = Mathf.Clamp01(current / _maxHealth);

        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = 1f;
            healthSlider.value    = fraction;
        }

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(_maxHealth)}";

        // Tint health bar: green → yellow → red
        if (healthFill != null)
            healthFill.color = Color.Lerp(Color.red, Color.green, fraction);
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
            stealthPromptText.text  = stealthOn ? "VANISHED" : "[Q] Vanish";
            stealthPromptText.color = stealthOn
                ? new Color(0.5f, 0f, 1f)  // Purple when active
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

    /// <summary>
    /// Shows or hides the entire HUD. Called automatically when PauseManager
    /// raises the OnPauseStateChanged event.
    /// • paused = true  → HUD hides (health bar, ammo, etc.)
    /// • paused = false → HUD shows again
    /// </summary>
    public void SetHUDVisible(bool isPaused)
    {
        if (hudRoot != null)
            hudRoot.SetActive(!isPaused);
    }
}
