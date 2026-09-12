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

    [Tooltip("Optional: Displays current healing vials count (e.g. 'VIALS  3').")]
    public TextMeshProUGUI vialText;

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
    private HealingVialInventoryNet _localVials;
    private GirlStealth           _localStealth;
    private bool                  _isBound    = false;
    private bool                  _isDemon    = false;
    private float                 _maxHealth  = 100f;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    public static PlayerHUD Instance { get; private set; }

    private CanvasGroup _hudCanvasGroup;
    private bool _isPossessingOverride = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

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

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PauseManager.OnPauseStateChanged -= SetHUDVisible;
        UnsubscribeHealthEvents();
    }

    /// <summary>
    /// Swaps Player HUD from the Girl's view to the possessed Investigator's view
    /// displaying their actual health bar, weapons, ammo, and role.
    /// </summary>
    public void BindToPossessedTarget(GameObject targetObj)
    {
        if (targetObj == null) return;
        _isPossessingOverride = true;

        UnsubscribeHealthEvents();

        targetObj.TryGetComponent<HealthSystem>(out _localHealthSys);
        targetObj.TryGetComponent<TargetHealth>(out _localHealth);
        targetObj.TryGetComponent<InvestigatorCombatNet>(out _localCombat);
        targetObj.TryGetComponent<HealingVialInventoryNet>(out _localVials);

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

        // Show investigator HUD, hide demon HUD
        _isDemon = false;
        if (explorerPanel != null) explorerPanel.SetActive(true);
        if (demonPanel != null) demonPanel.SetActive(false);

        // Turn on minimap if target is an Adventurer/Explorer
        AdventurerMinimapSetup.OnPossessionChanged(targetObj, true);

        // Bind blood screen overlay to the possessed target
        if (bloodScreenOverlay != null)
        {
            bloodScreenOverlay.BindToTarget(targetObj);
        }

        string targetName = targetObj.name.Replace("(Clone)", "").Trim();
        if (roleLabel != null)
        {
            roleLabel.text = $"<color=#B388FF>|</color> POSSESSING: {targetName.ToUpper()}";
            roleLabel.color = new Color(0.7f, 0.4f, 1f);
        }

        _isBound = true;
        RefreshHealth();
    }

    /// <summary>
    /// Restores the Girl's native HUD when possession ends.
    /// </summary>
    public void RestoreGirlHUD()
    {
        if (!_isPossessingOverride) return;
        _isPossessingOverride = false;

        UnsubscribeHealthEvents();
        AdventurerMinimapSetup.OnPossessionChanged(null, false);
        if (bloodScreenOverlay != null)
        {
            bloodScreenOverlay.BindToTarget(null);
        }
        _isBound = false;
        TryBindToLocalPlayer();
    }

    /// <summary>
    /// Hides all active HUD elements (explorer panel, weapons, ammo, vials, minimap)
    /// when the local character dies, leaving the screen clean for DeathUI.
    /// </summary>
    public void HandleLocalPlayerDied()
    {
        if (explorerPanel != null) explorerPanel.SetActive(false);
        if (demonPanel != null) demonPanel.SetActive(false);
        if (hudRoot != null && hudRoot != gameObject) hudRoot.SetActive(false);

        // Hide minimap for dead explorer
        AdventurerMinimapSetup.OnPossessionChanged(null, false);

        if (bloodScreenOverlay != null)
        {
            bloodScreenOverlay.gameObject.SetActive(false);
        }

        var corpseHUD = GetComponentInChildren<CorpseInteractionHUD>(true);
        if (corpseHUD != null)
        {
            corpseHUD.gameObject.SetActive(false);
        }

        if (roleLabel != null)
        {
            roleLabel.text = "<color=#E53935>|</color> DECEASED";
            roleLabel.color = Color.red;
        }
    }

    private void UnsubscribeHealthEvents()
    {
        if (_localHealthSys != null)
        {
            _localHealthSys.OnHealthChanged -= OnHealthSysChanged;
        }
        if (_localHealth != null)
        {
            _localHealth.currentHealth.OnValueChanged -= OnTargetHealthChanged;
            _localHealth.maxHealth.OnValueChanged     -= OnTargetHealthChanged;
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
        if (_isPossessingOverride) return;
        if (NetworkManager.Singleton == null) return;

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        // Determine role
        _isDemon = localPlayer.TryGetComponent<GirlStealth>(out _localStealth);

        localPlayer.TryGetComponent<HealthSystem>(out _localHealthSys);
        localPlayer.TryGetComponent<TargetHealth>(out _localHealth);
        localPlayer.TryGetComponent<InvestigatorCombatNet>(out _localCombat);
        localPlayer.TryGetComponent<HealingVialInventoryNet>(out _localVials);

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
        // Ensure auxiliary HUD overlays (Corpse looting prompt, Blood damage vignette) are active
        var corpseHUD = GetComponentInChildren<CorpseInteractionHUD>(true);
        if (corpseHUD != null && !corpseHUD.gameObject.activeSelf)
        {
            corpseHUD.gameObject.SetActive(true);
        }

        var bloodOverlay = FindFirstObjectByType<BloodScreenOverlay>(FindObjectsInactive.Include);
        if (bloodOverlay != null && !bloodOverlay.gameObject.activeSelf)
        {
            bloodOverlay.gameObject.SetActive(true);
        }

        // Immediately initialize health bar UI
        RefreshHealth();
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

            if (healthFill == null && healthSlider.fillRect != null)
            {
                healthFill = healthSlider.fillRect.GetComponent<Image>();
            }
        }

        if (healthFill != null)
        {
            // If the image uses Unity's Filled type (e.g. Bloodlines UI Horizontal Fill),
            // ensure the fill container is full-span and drive fillAmount linearly!
            if (healthFill.type == Image.Type.Filled)
            {
                healthFill.rectTransform.anchorMin = new Vector2(0f, 0f);
                healthFill.rectTransform.anchorMax = new Vector2(1f, 1f);
                healthFill.fillAmount = displayFraction;
            }

            // Tint health bar: Bloodlines UI textures are already blood-red.
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
        int vialCount = _localVials != null ? _localVials.VialCount : 0;
        if (vialText != null)
        {
            vialText.text = $"VIALS  {vialCount}";
        }

        if (_localCombat == null || ammoText == null) return;

        bool isGun = _localCombat.currentWeaponIndex.Value == 1;
        if (weaponText != null) weaponText.text = isGun ? "GUN" : "AXE";

        if (isGun)
        {
            int ammo = _localCombat.currentAmmo.Value;
            ammoText.text  = (vialText != null) ? $"AMMO  {ammo}" : $"AMMO  {ammo}  |  VIALS  {vialCount}";
            ammoText.color = ammo <= 3 ? new Color(1f, 0.3f, 0.3f) : Color.white;
        }
        else
        {
            ammoText.text  = (vialText != null) ? "──" : $"VIALS  {vialCount}";
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
