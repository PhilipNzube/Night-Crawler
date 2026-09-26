using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Michsky.UI.Heat;

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

    [Header("Damage / Blood Overlay")]
    [Tooltip("Vignette blood screen overlay. If null, will auto-locate on HUDCanvas even if inactive.")]
    public BloodScreenOverlay bloodScreenOverlay;

    [Header("Vial Count UI Element")]
    [Tooltip("The 'VialCount' GameObject from HUDCanvas. Displayed for Medic or when other characters carry >= 1 vial.")]
    public GameObject vialCountGO;

    [Tooltip("The text component inside VialCount (e.g. VialText) showing the number.")]
    public TextMeshProUGUI vialCountNumberText;

    [Header("Vengeful Spirit Panel (hidden for Investigator)")]
    [Tooltip("Root GameObject for Vengeful Spirit-specific UI (stealth prompt, taunt prompt, etc). Hidden for Investigators.")]
    public GameObject demonPanel;

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

        // Auto-ensure ContextInteractionHUD is present on the HUD Canvas
        var interactionHUD = GetComponentInChildren<ContextInteractionHUD>(true);
        if (interactionHUD == null)
        {
            interactionHUD = FindFirstObjectByType<ContextInteractionHUD>(FindObjectsInactive.Include);
        }
        if (interactionHUD == null)
        {
            interactionHUD = gameObject.AddComponent<ContextInteractionHUD>();
            Debug.Log("[PlayerHUD] Auto-created ContextInteractionHUD component on PlayerHUD Canvas.");
        }
        if (interactionHUD != null && !interactionHUD.gameObject.activeSelf)
        {
            interactionHUD.gameObject.SetActive(true);
        }

        // Auto-locate VialCount GameObject on HUDCanvas if not manually wired
        if (vialCountGO == null)
        {
            var canvas = transform.root;
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "VialCount")
                {
                    vialCountGO = t.gameObject;
                    break;
                }
            }
        }
        if (vialCountGO != null && vialCountNumberText == null)
        {
            var vt = vialCountGO.transform.Find("VialText");
            if (vt != null) vialCountNumberText = vt.GetComponent<TextMeshProUGUI>();
            if (vialCountNumberText == null) vialCountNumberText = vialCountGO.GetComponentInChildren<TextMeshProUGUI>(true);
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
            _localHealthSys.OnHealthChanged += OnHealthSysChanged;
        }
        if (_localHealth != null)
        {
            _localHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
            _localHealth.maxHealth.OnValueChanged     += OnTargetHealthChanged;
        }

        // Bind to target
        _isDemon = false;
        if (demonPanel != null) demonPanel.SetActive(false);

        // Turn on minimap if target is an Adventurer/Explorer
        AdventurerMinimapSetup.OnPossessionChanged(targetObj, true);

        // Bind blood screen overlay to the possessed target
        if (bloodScreenOverlay != null)
        {
            bloodScreenOverlay.BindToTarget(targetObj);
        }

        _isBound = true;
        RefreshHealth();
    }

    private bool _isDead = false;

    /// <summary>
    /// Restores the Girl's native HUD when possession ends.
    /// </summary>
    public void RestoreGirlHUD()
    {
        if (!_isPossessingOverride) return;
        _isPossessingOverride = false;
        _isDead = false;

        if (healthSlider != null) healthSlider.gameObject.SetActive(true);
        if (vialCountGO != null) vialCountGO.SetActive(false);

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
    /// Hides all active HUD elements (health bar, explorer panel, weapons, ammo, vials, minimap)
    /// when the local character dies, leaving the screen clean for DeathUI.
    /// </summary>
    public void HandleLocalPlayerDied()
    {
        _isDead = true;
        UnsubscribeHealthEvents();

        if (healthSlider != null) healthSlider.gameObject.SetActive(false);
        if (demonPanel != null) demonPanel.SetActive(false);
        if (vialCountGO != null) vialCountGO.SetActive(false);
        if (hudRoot != null && hudRoot != gameObject) hudRoot.SetActive(false);

        // Hide minimap for dead explorer
        AdventurerMinimapSetup.OnPossessionChanged(null, false);

        if (bloodScreenOverlay != null)
        {
            bloodScreenOverlay.gameObject.SetActive(false);
        }

        var interactionHUD = GetComponentInChildren<ContextInteractionHUD>(true);
        if (interactionHUD != null)
        {
            interactionHUD.gameObject.SetActive(false);
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
        if (_isDead) return;

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

        // Check if player is dead
        if (localPlayer.TryGetComponent<TargetHealth>(out var checkHealth) && checkHealth.isCorpse.Value)
        {
            HandleLocalPlayerDied();
            return;
        }

        _isDead = false;
        if (healthSlider != null) healthSlider.gameObject.SetActive(true);

        // Determine role
        _isDemon = localPlayer.TryGetComponent<GirlStealth>(out _localStealth);

        localPlayer.TryGetComponent<HealthSystem>(out _localHealthSys);
        localPlayer.TryGetComponent<TargetHealth>(out _localHealth);
        localPlayer.TryGetComponent<InvestigatorCombatNet>(out _localCombat);
        localPlayer.TryGetComponent<HealingVialInventoryNet>(out _localVials);

        if (_localHealthSys != null)
        {
            _localHealthSys.OnHealthChanged += OnHealthSysChanged;
        }
        if (_localHealth != null)
        {
            _localHealth.currentHealth.OnValueChanged += OnTargetHealthChanged;
            _localHealth.maxHealth.OnValueChanged     += OnTargetHealthChanged;
        }

        // Configure role-specific panels
        if (demonPanel != null) demonPanel.SetActive(_isDemon);
        _isBound = true;
        // Ensure auxiliary HUD overlays (Corpse looting prompt, Blood damage vignette) are active
        var interactionHUD = GetComponentInChildren<ContextInteractionHUD>(true);
        if (interactionHUD != null && !interactionHUD.gameObject.activeSelf)
        {
            interactionHUD.gameObject.SetActive(true);
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

        bool isCorpse = (_localHealth != null && _localHealth.isCorpse.Value) ||
                        (_localHealthSys != null && _localHealthSys.IsDead);

        if (isCorpse)
        {
            current = 0f;
            max = _localHealth != null ? _localHealth.MaxHealth : (_localHealthSys != null ? _localHealthSys.MaxHealth : 100f);
        }
        else if (_localHealth != null && _localHealthSys != null)
        {
            max = Mathf.Max(_localHealth.MaxHealth, _localHealthSys.MaxHealth);
            // If the character is alive and not a corpse, take the highest synchronized health value
            current = Mathf.Max(_localHealth.CurrentHealth, _localHealthSys.CurrentHealth);
            // Safeguard: alive investigator must never be displayed with 0 HP and max blood while running around
            if (current <= 0f && !_isDead)
            {
                current = 1f;
            }
        }
        else if (_localHealthSys != null)
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

        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = 1f;
            healthSlider.value    = fraction;

            if (healthFill == null && healthSlider.fillRect != null)
            {
                healthFill = healthSlider.fillRect.GetComponent<Image>();
            }
        }

        if (healthFill != null)
        {
            if (healthFill.type == Image.Type.Filled)
            {
                healthFill.rectTransform.anchorMin = new Vector2(0f, 0f);
                healthFill.rectTransform.anchorMax = new Vector2(1f, 1f);
                healthFill.fillAmount = fraction;
            }
            healthFill.color = Color.white;
        }

        // Push health updates to Blood Screen Overlay immediately
        if (bloodScreenOverlay != null)
        {
            bloodScreenOverlay.UpdateHealthFraction(current, _maxHealth);
        }
    }

    private void RefreshExplorerPanel()
    {
        int vialCount = _localVials != null ? _localVials.VialCount : 0;
        bool isMedic = _localVials != null && _localVials.IsMedicCharacter();

        // VialCount GO visibility:
        // Visible for Medic or appears when another character carries >= 1 vial
        if (vialCountGO != null)
        {
            bool showVials = !_isDemon && !_isDead && (isMedic || vialCount > 0);
            vialCountGO.SetActive(showVials);
        }

        if (vialCountNumberText != null)
        {
            vialCountNumberText.text = vialCount.ToString();
        }
    }

    private void RefreshDemonPanel()
    {
    }

    private void SetWaitingState(bool waiting)
    {
    }
}
