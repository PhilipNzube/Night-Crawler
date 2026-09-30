using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: High-performance, cinematic spectator camera and HUD controller.
/// Engages seamlessly when the local investigator dies, allowing them to cycle
/// through surviving teammates with third-person tracking, free-orbit mouse look,
/// dynamic health monitoring, and modern esports-grade UI presentation.
/// 
/// OCP: Self-contained procedural UI ensures 100% plug-and-play capability out-of-the-box
/// while supporting Inspector-assigned custom UI prefabs/elements.
/// </summary>
public class SpectatorController : MonoBehaviour
{
    private static SpectatorController _instance;
    public static SpectatorController SurvivorInstance { get; private set; }
    public static SpectatorController MonsterInstance { get; private set; }

    public static SpectatorController Instance
    {
        get
        {
            if (SurvivorInstance != null && SurvivorInstance.IsSpectating) return SurvivorInstance;
            if (MonsterInstance != null && MonsterInstance.IsSpectating) return MonsterInstance;
            if (SurvivorInstance != null) return SurvivorInstance;
            if (MonsterInstance != null) return MonsterInstance;
            if (_instance != null) return _instance;
            return null;
        }
        private set => _instance = value;
    }

    [Header("Spectator Settings")]
    [Tooltip("Mouse sensitivity when orbiting the spectated teammate.")]
    public float mouseSensitivity = 0.15f;
    [Tooltip("Smoothing factor for camera position tracking.")]
    public float followSmoothness = 12f;
    [Tooltip("Default camera distance behind the spectated player.")]
    public float defaultCameraDistance = 3.8f;
    [Tooltip("Minimum and maximum camera zoom limits with mouse scroll wheel.")]
    public Vector2 zoomRange = new Vector2(1.8f, 6.5f);

    public enum SpectatorModeType
    {
        Survivors,
        Monsters
    }

    [Header("Spectator Mode Settings")]
    [Tooltip("Target type for this spectator controller: Survivors (Dead Player) or Monsters (Girl).")]
    public SpectatorModeType modeType = SpectatorModeType.Survivors;

    [Header("Heat UI Target & Count Elements (Clean Presentation)")]
    [Tooltip("Text displaying strictly the name of the spectated entity without any prefix.")]
    public TMP_Text cleanTargetNameText;

    [Tooltip("Text displaying strictly the living count number (e.g. '3').")]
    public TMP_Text cleanCountText;

    [Tooltip("Text displaying total connected players in the game (e.g. '5').")]
    public TMP_Text totalConnectedPlayersText;

    [Header("Exit Hotkey")]
    [Tooltip("Keyboard key to exit spectator mode (default: C).")]
    public Key exitHotkey = Key.C;

    [Tooltip("Optional Michsky Heat HotkeyEvent for exiting spectator mode.")]
    public Michsky.UI.Heat.HotkeyEvent exitHotkeyEvent;

    [Header("Optional Custom UI References (Procedural if Null)")]
    public GameObject customCanvasRoot;
    public TMP_Text customRoleText;

    [Header("Michsky Heat / Hotkey References")]
    [Tooltip("Optional Michsky Heat HotkeyEvent for cycling to Previous Survivor.")]
    public Michsky.UI.Heat.HotkeyEvent prevHotkey;

    [Tooltip("Optional Michsky Heat HotkeyEvent for cycling to Next Survivor.")]
    public Michsky.UI.Heat.HotkeyEvent nextHotkey;

    [Tooltip("Optional Michsky Heat HotkeyEvent for View Mode Toggle.")]
    public Michsky.UI.Heat.HotkeyEvent viewModeHotkey;

    [Tooltip("Optional Michsky Heat HotkeyEvent for Cursor Lock Toggle.")]
    public Michsky.UI.Heat.HotkeyEvent cursorHotkey;

    [Header("Camera Return Reference")]
    [Tooltip("Dynamic runtime reference to PlayerFollowCamera (auto-resolved when local player spawns).")]
    public CinemachineVirtualCameraBase playerFollowCamera;

    private static CinemachineVirtualCameraBase s_RegisteredPlayerFollowCam;

    /// <summary>
    /// Called by NetworkPlayer when the local player's PlayerFollowCamera is spawned.
    /// Eliminates the need to manually drag anything in the Editor Inspector.
    /// </summary>
    public static void RegisterPlayerFollowCamera(CinemachineVirtualCameraBase cam)
    {
        if (cam != null)
        {
            s_RegisteredPlayerFollowCam = cam;
            if (SurvivorInstance != null) SurvivorInstance.playerFollowCamera = cam;
            if (MonsterInstance != null) MonsterInstance.playerFollowCamera = cam;
            if (_instance != null) _instance.playerFollowCamera = cam;
        }
    }

    /// <summary>
    /// Robustly resolves the local player's gameplay follow camera at runtime.
    /// </summary>
    public CinemachineVirtualCameraBase GetPlayerFollowCamera()
    {
        if (playerFollowCamera != null) return playerFollowCamera;
        if (s_RegisteredPlayerFollowCam != null)
        {
            playerFollowCamera = s_RegisteredPlayerFollowCam;
            return playerFollowCamera;
        }

        // 1. Check local NetworkPlayer
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<NetworkPlayer>(out var np) && np.virtualCamera != null)
            {
                playerFollowCamera = np.virtualCamera;
                s_RegisteredPlayerFollowCam = playerFollowCamera;
                return playerFollowCamera;
            }
        }

        // 2. Search by runtime name format: "PlayerFollowCamera_Local_{id}"
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        var camObj = GameObject.Find($"PlayerFollowCamera_Local_{localId}") ?? GameObject.Find("PlayerFollowCamera_Local_0");
        if (camObj != null && camObj.TryGetComponent<CinemachineVirtualCameraBase>(out var vcam))
        {
            playerFollowCamera = vcam;
            s_RegisteredPlayerFollowCam = playerFollowCamera;
            return playerFollowCamera;
        }

        // 3. Fallback search all virtual cameras
        var allCams = FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in allCams)
        {
            if (c == null || c == _cinemachineCam) continue;
            if (c.gameObject.name.Contains("Spectator") || c.gameObject.name.Contains("MonsterSpawn")) continue;
            if (c.gameObject.name.Contains("PlayerFollowCamera"))
            {
                playerFollowCamera = c;
                s_RegisteredPlayerFollowCam = playerFollowCamera;
                return playerFollowCamera;
            }
        }

        if (_cachedGameplayCam != null && _cachedGameplayCam != _cinemachineCam)
        {
            playerFollowCamera = _cachedGameplayCam;
            return playerFollowCamera;
        }

        return null;
    }

    [Header("Michsky Heat / Dark UI")]
    [Tooltip("Optional: Michsky ProgressBar to display spectated player's health.")]
    public Michsky.UI.Heat.ProgressBar heatHealthProgressBar;

    // Runtime state
    private bool _isSpectating = false;
    private bool _isStopping = false; // True during StopSpectating to block StartSpectating's StopAllCoroutines
    public bool IsSpectating => _isSpectating;

    public static bool IsAnySpectating =>
        (_instance != null && _instance._isSpectating) ||
        (SurvivorInstance != null && SurvivorInstance._isSpectating) ||
        (MonsterInstance != null && MonsterInstance._isSpectating);

    private readonly List<TargetHealth> _aliveTargets = new List<TargetHealth>();
    private int _currentTargetIndex = 0;
    private TargetHealth _currentTarget;

    // Camera targets & Cinemachine binding
    private Transform _spectatorAnchor;
    private Transform _spectatorCamTransform;
    private CinemachineVirtualCameraBase _cinemachineCam;
    private CinemachineVirtualCameraBase _cachedGameplayCam;
    private Transform _cachedGameplayCamTarget;
    private float _yaw = 0f;
    private float _pitch = 18f;
    private float _currentDistance = 3.8f;
    private float _collisionDistance = 3.8f;
    private bool _freeOrbitMode = true; // true = mouse orbits, false = over-shoulder follows player facing

    // Procedural UI references
    private Canvas _spectatorCanvas;
    private CanvasGroup _canvasGroup;
    private TMP_Text _targetNameText;
    private TMP_Text _roleBadgeText;
    private TMP_Text _healthReadoutText;
    private Image _healthFillImage;
    private TMP_Text _survivorsCountText;
    private TMP_Text _modePromptText;
    private GameObject _toastBanner;
    private TMP_Text _toastText;
    private CanvasGroup _toastCanvasGroup;
    private Coroutine _toastCoroutine;
    private int _maxMonstersSummoned = 0;

    private void Awake()
    {
        _maxMonstersSummoned = 0;
        if (modeType == SpectatorModeType.Monsters)
        {
            MonsterInstance = this;
            defaultCameraDistance = 2.1f;
            zoomRange = new Vector2(1.2f, 3.2f);
        }
        else
        {
            SurvivorInstance = this;
            _instance = this;
        }

        _currentDistance = defaultCameraDistance;
        _collisionDistance = defaultCameraDistance;
        CreateSpectatorAnchor();
        BuildProceduralHUD();
    }

    private void OnEnable()
    {
        PauseManager.OnPauseStateChanged += HandlePauseStateChanged;
        KeybindingManager.OnBindingsChanged += UpdateSpectatorHotkeyLabels;
    }

    private void OnDisable()
    {
        PauseManager.OnPauseStateChanged -= HandlePauseStateChanged;
        KeybindingManager.OnBindingsChanged -= UpdateSpectatorHotkeyLabels;
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        // Only react to pause state while actively spectating.
        // If we're stopped or in the process of stopping, don't re-show the HUD.
        if (_canvasGroup != null && _isSpectating && !_isStopping)
        {
            _canvasGroup.alpha = isPaused ? 0f : 1f;
            // Keep blocksRaycasts FALSE at all times — the pause menu must always
            // be interactable, even while spectating.
            _canvasGroup.blocksRaycasts = false;
        }

        SetHotkeysActive(!isPaused);
    }

    private void SetHotkeysActive(bool active)
    {
        // Spectator hotkeys are ONLY active while genuinely spectating.
        // Gate on _isSpectating so unpausing never re-enables them when idle.
        bool effectiveActive = active && _isSpectating;
        if (prevHotkey != null)      prevHotkey.enabled      = effectiveActive;
        if (nextHotkey != null)      nextHotkey.enabled      = effectiveActive;
        if (viewModeHotkey != null)  viewModeHotkey.enabled  = effectiveActive;
        if (cursorHotkey != null)    cursorHotkey.enabled    = effectiveActive;
        // Also gate exitHotkeyEvent — it is mapped to Escape (key 60), so it MUST
        // be disabled outside spectator mode or it will fire ExitSpectating() on
        // every Escape press and fight with PauseManager.
        if (exitHotkeyEvent != null) exitHotkeyEvent.enabled = effectiveActive;
    }

    private void OnDestroy()
    {
        if (SurvivorInstance == this) SurvivorInstance = null;
        if (MonsterInstance == this) MonsterInstance = null;
        if (_instance == this) _instance = null;
        if (_spectatorAnchor != null)
        {
            Destroy(_spectatorAnchor.gameObject);
        }
        if (_spectatorCamTransform != null)
        {
            Destroy(_spectatorCamTransform.gameObject);
        }
    }

    private void CreateSpectatorAnchor()
    {
        if (_spectatorAnchor == null)
        {
            var anchorObj = new GameObject("SpectatorCameraAnchor");
            _spectatorAnchor = anchorObj.transform;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(anchorObj);
            }
        }

        if (_spectatorCamTransform == null)
        {
            var camObj = new GameObject("SpectatorVirtualCameraRig");
            _spectatorCamTransform = camObj.transform;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(camObj);
            }

            _cinemachineCam = camObj.AddComponent<CinemachineCamera>();
            _cinemachineCam.Priority = -100;
            _cinemachineCam.enabled = false;
            camObj.SetActive(false);
        }
    }

    /// <summary>
    /// Checks if the local player is truly dead (TargetHealth or HealthSystem).
    /// Living players must NEVER enter or remain in Spectator Mode!
    /// </summary>
    public bool IsLocalPlayerDead()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return false;
        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null) return false;

        // If possessed, check the active possessed target
        if (playerObj.TryGetComponent<GirlPossession>(out var gp) && gp.isPossessing.Value)
        {
            return false;
        }

        bool hasTh = playerObj.TryGetComponent<TargetHealth>(out var th);
        bool hasHs = playerObj.TryGetComponent<HealthSystem>(out var hs);

        // If either component confirms player is still alive, they are NOT dead!
        bool isThAlive = hasTh && !th.isCorpse.Value && th.CurrentHealth > 0f;
        bool isHsAlive = hasHs && !hs.IsDead && hs.CurrentHealth > 0f;

        if (isThAlive || isHsAlive)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Activates spectator mode for the local player. Called when death completes.
    /// </summary>
    public void StartSpectating()
    {
        if (_isSpectating) return;

        // Survivor spectating is strictly for dead investigators
        if (modeType == SpectatorModeType.Survivors)
        {
            if (!IsLocalPlayerDead())
            {
                Debug.LogWarning("[SpectatorController] Suppressed StartSpectating — Local player is still ALIVE!");
                return;
            }

            // Verify local player is not the Girl (anti-ghosting)
            if (IsLocalPlayerGirl())
            {
                Debug.Log("[SpectatorController] Suppressed — Local player is the Vengeful Spirit.");
                return;
            }
        }
        else if (modeType == SpectatorModeType.Monsters)
        {
            // Monster spectating is ONLY allowed when active monsters exist in scene
            if (!HasActiveMonstersInScene())
            {
                Debug.LogWarning("[SpectatorController] Suppressed monster spectating — No active monsters exist in the mine!");
                return;
            }
        }

        Debug.Log($"[SpectatorController] Initiating Spectator Mode ({modeType})...");
        _isSpectating = true;

        // Resolve camera
        ResolveCamera();

        // Cache local player's gameplay camera & target before entering spectator mode
        CacheLocalGameplayCamera();

        // Lock cursor for smooth mouse look
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Suspend local character movement and mouse look while spectating so body doesn't spin
        SuspendLocalPlayerMovement(true);

        // Refresh targets and snap to the first alive teammate
        RefreshAliveTargets();
        _currentTargetIndex = 0;
        SelectTargetByIndex(_currentTargetIndex, true);

        // Initialize and sync Hotkey labels, then enable them
        InitHotkeys();
        SetHotkeysActive(true); // _isSpectating is true here, so effectiveActive = true
        if (viewModeHotkey != null) viewModeHotkey.SetLabel(_freeOrbitMode ? "FREE ORBIT" : "SHOULDER CAM");
        if (cursorHotkey != null) cursorHotkey.SetLabel(Cursor.lockState == CursorLockMode.Locked ? "UNLOCK CURSOR" : "LOCK CURSOR");

        // Fade in HUD — only stop coroutines if we are NOT in the middle of stopping
        // (StopAllCoroutines would kill the fade-out and ReassertPlayerCamPriority coroutines)
        if (_canvasGroup != null && !_isStopping)
        {
            StopAllCoroutines();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false; // Never block raycasts — pause must always work
            _canvasGroup.gameObject.SetActive(true);
            StartCoroutine(FadeCanvasGroup(_canvasGroup, 0f, 1f, 0.5f));
        }

        ShowToast("SPECTATOR MODE ENGAGED", new Color(0.2f, 0.9f, 1f, 1f));
    }

    /// <summary>
    /// Binds Michsky Heat HotkeyEvent listeners to spectator controls.
    /// </summary>
    public void InitHotkeys()
    {
        if (prevHotkey != null)
        {
            prevHotkey.onHotkeyPress.RemoveListener(CyclePreviousSurvivor);
            prevHotkey.onHotkeyPress.AddListener(CyclePreviousSurvivor);
        }
        if (nextHotkey != null)
        {
            nextHotkey.onHotkeyPress.RemoveListener(CycleNextSurvivor);
            nextHotkey.onHotkeyPress.AddListener(CycleNextSurvivor);
        }
        if (viewModeHotkey != null)
        {
            viewModeHotkey.onHotkeyPress.RemoveListener(ToggleOrbitMode);
            viewModeHotkey.onHotkeyPress.AddListener(ToggleOrbitMode);
        }
        if (cursorHotkey != null)
        {
            cursorHotkey.onHotkeyPress.RemoveListener(ToggleCursorLock);
            cursorHotkey.onHotkeyPress.AddListener(ToggleCursorLock);
        }
        if (exitHotkeyEvent != null)
        {
            exitHotkeyEvent.onHotkeyPress.RemoveListener(ExitSpectating);
            exitHotkeyEvent.onHotkeyPress.AddListener(ExitSpectating);

            // Also wire mouse clicks if there is a Button / ButtonManager on this hotkey element
            var uiBtn = exitHotkeyEvent.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (uiBtn != null)
            {
                uiBtn.onClick.RemoveListener(ExitSpectating);
                uiBtn.onClick.AddListener(ExitSpectating);
            }
            var heatBtn = exitHotkeyEvent.GetComponentInChildren<Michsky.UI.Heat.ButtonManager>(true);
            if (heatBtn != null)
            {
                heatBtn.onClick.RemoveListener(ExitSpectating);
                heatBtn.onClick.AddListener(ExitSpectating);
            }
        }

        UpdateSpectatorHotkeyLabels();
    }

    /// <summary>
    /// Updates all spectator HotkeyEvent button text and prompt displays to reflect current keybindings.
    /// </summary>
    public void UpdateSpectatorHotkeyLabels()
    {
        string prevKeyStr = KeybindingManager.GetBoundKeyString("SpectatePrev", "A");
        string nextKeyStr = KeybindingManager.GetBoundKeyString("SpectateNext", "D");
        string viewModeKeyStr = KeybindingManager.GetBoundKeyString("SpectateViewMode", "SPACE");
        string cursorKeyStr = KeybindingManager.GetBoundKeyString("SpectateCursor", "L-ALT");
        string catKeyStr = KeybindingManager.GetBoundKeyString("SpectateCategory", "TAB");
        string exitKeyStr = KeybindingManager.GetBoundKeyString("SpectateExit", "C");

        SetHotkeyVisual(prevHotkey, prevKeyStr, modeType == SpectatorModeType.Monsters ? "PREV MONSTER" : "PREV SURVIVOR");
        SetHotkeyVisual(nextHotkey, nextKeyStr, modeType == SpectatorModeType.Monsters ? "NEXT MONSTER" : "NEXT SURVIVOR");
        // Show the mode you'd switch TO, not the current mode
        SetHotkeyVisual(viewModeHotkey, viewModeKeyStr, _freeOrbitMode ? "SHOULDER CAM" : "FREE ORBIT");
        SetHotkeyVisual(cursorHotkey, cursorKeyStr, Cursor.lockState == CursorLockMode.Locked ? "UNLOCK CURSOR" : "LOCK CURSOR");
        SetHotkeyVisual(exitHotkeyEvent, exitKeyStr, modeType == SpectatorModeType.Monsters ? "EXIT SPECTATE" : "EXIT TO DEATH");

        if (_modePromptText != null)
        {
            string modeName = _freeOrbitMode ? "FREE ORBIT" : "SHOULDER CAM";
            _modePromptText.text = $"[{viewModeKeyStr}] View Mode: {modeName}   •   [{prevKeyStr}/{nextKeyStr}] Switch   •   [{catKeyStr}] Switch Category   •   [{cursorKeyStr}] Cursor   •   [{exitKeyStr}] Exit";
        }
    }

    private void SetHotkeyVisual(Michsky.UI.Heat.HotkeyEvent hotkey, string keyStr, string labelStr)
    {
        if (hotkey == null) return;
        hotkey.keyID = keyStr;
        hotkey.hotkeyLabel = labelStr;
        hotkey.SetLabel(labelStr);
        hotkey.UpdateUI();

        var tmps = hotkey.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        foreach (var t in tmps)
        {
            if (t == null) continue;
            string n = t.gameObject.name.ToLower();
            if (n.Contains("label"))
            {
                t.text = labelStr;
            }
            else
            {
                t.text = keyStr;
            }
        }
    }

    /// <summary>
    /// Seamlessly toggles spectator observation category between living human survivors and summoned monsters.
    /// </summary>
    public void ToggleSpectatorCategory()
    {
        if (modeType == SpectatorModeType.Survivors)
        {
            if (HasActiveMonstersInScene())
            {
                modeType = SpectatorModeType.Monsters;
                ShowToast("SPECTATING: MONSTERS", new Color(1f, 0.4f, 0.4f, 1f));
            }
            else
            {
                ShowToast("NO ACTIVE MONSTERS IN THE MINE", new Color(1f, 0.6f, 0.2f, 1f));
                return;
            }
        }
        else
        {
            modeType = SpectatorModeType.Survivors;
            ShowToast("SPECTATING: SURVIVORS", new Color(0.2f, 0.9f, 1f, 1f));
        }

        RefreshAliveTargets();
        _currentTargetIndex = 0;
        SelectTargetByIndex(_currentTargetIndex, true);
        UpdateSpectatorHotkeyLabels();
    }

    /// <summary>
    /// Exits spectator mode via hotkey (e.g. Esc) and returns to death screen or normal view.
    /// </summary>
    public void ExitSpectating()
    {
        if (!_isSpectating) return;

        StopSpectating();

        if (modeType == SpectatorModeType.Survivors)
        {
            if (DeathUI.Instance != null)
            {
                DeathUI.Instance.ReturnFromSpectatorToDeath();
            }
        }
        else
        {
            // CRITICAL: Do NOT call customCanvasRoot.SetActive(false) here!
            // For the Monster spectator, customCanvasRoot IS this GameObject.
            // Calling SetActive(false) on ourselves mid-frame instantly kills all
            // running coroutines (including the camera-restore and HUD-fade ones
            // started by StopSpectating) and can leave the camera frozen at the
            // monster's position forever.
            //
            // StopSpectating() already started a FadeCanvasGroup coroutine that
            // will call SetActive(false) after 0.4 s. Just make it invisible NOW
            // via alpha so the user never sees the half-faded HUD.
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }
            if (_spectatorCanvas != null) _spectatorCanvas.gameObject.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>
    /// Checks if any active, living monsters exist in the mine.
    /// </summary>
    public static bool HasActiveMonstersInScene()
    {
        var controllers = FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
        foreach (var m in controllers)
        {
            if (m == null || m.gameObject == null) continue;
            if (m.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) continue;
            if (m.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead) continue;
            return true;
        }

        var ais = FindObjectsByType<MonsterAI>(FindObjectsSortMode.None);
        foreach (var ai in ais)
        {
            if (ai == null || ai.gameObject == null) continue;
            if (ai.currentState == MonsterAI.AIState.Dead) continue;
            if (ai.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Dedicated entry point for the Monster Spectator modal.
    /// If no active monsters exist, notifies the user and closes modal.
    /// </summary>
    public void TryOpenMonsterSpectator(Michsky.UI.Heat.ModalWindowManager modal = null, TargetHealth initialTarget = null)
    {
        if (!HasActiveMonstersInScene() && initialTarget == null)
        {
            if (modal != null) modal.CloseWindow();
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("There are no active monsters left in the mine!", 3.5f);
            }
            return;
        }

        if (modal != null) modal.CloseWindow();
        modeType = SpectatorModeType.Monsters;
        StartSpectating();

        if (initialTarget != null)
        {
            SelectTarget(initialTarget, true);
        }
    }

    /// <summary>
    /// Focuses the camera directly onto a specific TargetHealth instance.
    /// </summary>
    public void SelectTarget(TargetHealth target, bool snapImmediate = true)
    {
        if (target == null) return;
        RefreshAliveTargets();
        int idx = _aliveTargets.IndexOf(target);
        if (idx >= 0)
        {
            SelectTargetByIndex(idx, snapImmediate);
        }
        else
        {
            _aliveTargets.Insert(0, target);
            SelectTargetByIndex(0, snapImmediate);
        }
    }

    /// <summary>
    /// Toggles between Free Orbit and Follow Over-Shoulder camera modes.
    /// </summary>
    public void ToggleOrbitMode()
    {
        _freeOrbitMode = !_freeOrbitMode;
        // currentModeName = what we just switched TO (for the toast)
        string currentModeName = _freeOrbitMode ? "FREE ORBIT" : "SHOULDER CAM";
        // nextModeName = what pressing Space NEXT TIME would switch to (for the hotkey label)
        string nextModeName = _freeOrbitMode ? "SHOULDER CAM" : "FREE ORBIT";
        ShowToast($"CAMERA MODE: {currentModeName}", new Color(1f, 0.85f, 0.2f, 1f));
        if (viewModeHotkey != null)
        {
            viewModeHotkey.SetLabel(nextModeName);
        }
        if (_modePromptText != null)
        {
            _modePromptText.text = $"[SPACE] Switch to {nextModeName}   •   [MOUSE] Orbit   •   [SCROLL] Zoom   •   [ALT] Cursor";
        }
    }

    /// <summary>
    /// Toggles mouse cursor visibility and lock state.
    /// </summary>
    public void ToggleCursorLock()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (cursorHotkey != null) cursorHotkey.SetLabel("LOCK CURSOR");
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (cursorHotkey != null) cursorHotkey.SetLabel("UNLOCK CURSOR");
        }
    }

    private void CacheLocalGameplayCamera()
    {
        GameObject localObj = ResolveLocalPlayerObject();
        if (localObj != null)
        {
            if (localObj.TryGetComponent<StarterAssets.ThirdPersonController>(out var tpc) && tpc.CinemachineCameraTarget != null)
            {
                _cachedGameplayCamTarget = tpc.CinemachineCameraTarget.transform;
            }
            if (_cachedGameplayCamTarget == null)
            {
                _cachedGameplayCamTarget = localObj.transform.Find("PlayerCameraRoot") ?? localObj.transform;
            }

            if (localObj.TryGetComponent<NetworkPlayer>(out var np) && np.virtualCamera != null)
            {
                _cachedGameplayCam = np.virtualCamera;
            }
            if (_cachedGameplayCam == null && localObj.TryGetComponent<GirlPossession>(out var gp) && gp.vcam != null)
            {
                _cachedGameplayCam = gp.vcam;
            }
            if (_cachedGameplayCam == null)
            {
                _cachedGameplayCam = localObj.GetComponentInChildren<CinemachineVirtualCameraBase>(true);
            }
        }

        if (_cachedGameplayCam == null)
        {
            var allCams = FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in allCams)
            {
                if (c == null) continue;
                if (c == _cinemachineCam) continue;
                if (c.gameObject.name.Contains("Spectator")) continue;
                if (c.gameObject.name.Contains("MonsterSpawn")) continue;
                _cachedGameplayCam = c;
                break;
            }
        }
    }

    private GameObject ResolveLocalPlayerObject()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            return NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
        }
        if (GameManager.Instance != null && GameManager.Instance.GirlTransform != null)
        {
            return GameManager.Instance.GirlTransform.gameObject;
        }
        var gmComp = FindFirstObjectByType<GirlMovement>();
        if (gmComp != null) return gmComp.gameObject;
        var gpComp = FindFirstObjectByType<GirlPossession>();
        if (gpComp != null) return gpComp.gameObject;
        var tpcComp = FindFirstObjectByType<StarterAssets.ThirdPersonController>();
        if (tpcComp != null) return tpcComp.gameObject;
        return null;
    }

    private void SuspendLocalPlayerMovement(bool suspend)
    {
        GameObject localObj = ResolveLocalPlayerObject();
        if (localObj == null) return;

        if (localObj.TryGetComponent<StarterAssets.StarterAssetsInputs>(out var sai))
        {
            sai.move = Vector2.zero;
            sai.look = Vector2.zero;
            sai.cursorInputForLook = !suspend;
            sai.enabled = !suspend;
        }
        if (localObj.TryGetComponent<StarterAssets.ThirdPersonController>(out var tpc))
        {
            tpc.enabled = !suspend;
        }
        if (localObj.TryGetComponent<GirlMovement>(out var gm))
        {
            gm.enabled = !suspend;
        }
    }

    /// <summary>
    /// Gracefully exits spectator mode (e.g. if revived or match ends).
    /// </summary>
    public void StopSpectating()
    {
        if (!_isSpectating) return;

        _isSpectating = false;
        _isStopping = false;
        _currentTarget = null;

        // Cancel any pending monster summon spectate routines
        if (NightCrawler.Monsters.GirlMonsterSummonHUD.Instance != null)
        {
            NightCrawler.Monsters.GirlMonsterSummonHUD.Instance.CancelPendingSpectateRoutine();
        }

        // Immediately disable all spectator hotkeys so they can no longer
        // intercept Escape or other keys now that we are no longer spectating.
        SetHotkeysActive(false);

        // 1. Immediately drop and disable dedicated spectator camera
        if (_cinemachineCam != null)
        {
            _cinemachineCam.Priority = -999999;
            _cinemachineCam.enabled = false;
            _cinemachineCam.gameObject.SetActive(false);
        }
        if (_spectatorCamTransform != null)
        {
            _spectatorCamTransform.gameObject.SetActive(false);
        }

        // 2. Disable monster spawn virtual camera if active
        if (NightCrawler.Monsters.GirlMonsterSummonHUD.Instance != null && NightCrawler.Monsters.GirlMonsterSummonHUD.Instance.monsterSpawnVirtualCamera != null)
        {
            NightCrawler.Monsters.GirlMonsterSummonHUD.Instance.monsterSpawnVirtualCamera.Priority = -999999;
            NightCrawler.Monsters.GirlMonsterSummonHUD.Instance.monsterSpawnVirtualCamera.enabled = false;
            NightCrawler.Monsters.GirlMonsterSummonHUD.Instance.monsterSpawnVirtualCamera.gameObject.SetActive(false);
        }

        // 3. Find and restore PlayerFollowCamera
        CinemachineVirtualCameraBase playerCam = GetPlayerFollowCamera();

        if (playerCam != null)
        {
            playerCam.gameObject.SetActive(true);
            playerCam.enabled = true;
            playerCam.Priority = 99999;
            playerCam.PreviousStateIsValid = false; // tells Brain to snap, not blend
        }

        // 4. Force CinemachineBrain to instantly Cut to the player camera
        CinemachineBrain brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : FindFirstObjectByType<CinemachineBrain>();
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            brain.ActiveBlend = null;
        }

        // 5. Restore local player controls and inputs
        GameObject localObj = ResolveLocalPlayerObject();
        if (localObj == null && playerCam != null)
        {
            if (playerCam.Follow != null) localObj = playerCam.Follow.root.gameObject;
            else if (playerCam.transform.parent != null) localObj = playerCam.transform.parent.root.gameObject;
        }

        SuspendLocalPlayerMovement(false);

        if (localObj != null)
        {
            if (localObj.TryGetComponent<StarterAssets.StarterAssetsInputs>(out var inputs))
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
                inputs.move = Vector2.zero;
                inputs.look = Vector2.zero;
            }
            if (localObj.TryGetComponent<StarterAssets.ThirdPersonController>(out var tpcComp))
            {
                tpcComp.enabled = true;
                if (tpcComp.CinemachineCameraTarget != null)
                {
                    tpcComp.CinemachineCameraTarget.transform.rotation = localObj.transform.rotation;
                }
                tpcComp.ResetTargetRotation(localObj.transform.eulerAngles.y, 0f);
            }
            if (localObj.TryGetComponent<GirlMovement>(out var gm))
            {
                gm.enabled = true;
            }
            if (localObj.TryGetComponent<GirlPossession>(out var gpComp))
            {
                gpComp.enabled = true;
                if (playerCam != null) gpComp.vcam = playerCam as CinemachineCamera;
            }
            if (localObj.TryGetComponent<UnityEngine.InputSystem.PlayerInput>(out var pi))
            {
                pi.enabled = true;
            }
        }

        _cachedGameplayCam = null;
        _cachedGameplayCamTarget = null;

        if (_canvasGroup != null)
        {
            // IMMEDIATELY hide the canvas so it can never show behind the pause menu
            // during the brief window between StopSpectating and the fade completing.
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            if (_canvasGroup.gameObject != gameObject)
                _canvasGroup.gameObject.SetActive(false);
            if (customCanvasRoot != null && customCanvasRoot != gameObject)
                customCanvasRoot.SetActive(false);
        }

        // NOTE: We do NOT run ReassertPlayerCamPriority here.
        // Running it across multiple exits caused coroutines to fight each other
        // (one exit's coroutine reasserting playerCam while the next exit's
        //  spectator cam had already taken control, causing the glitch loop).
        // Setting Priority = 99999 + PreviousStateIsValid = false above is sufficient
        // for the Brain to cut to playerCam on its own next LateUpdate.

        // Clear the stopping flag after a short delay
        StartCoroutine(ClearStoppingFlag());

        if (modeType == SpectatorModeType.Monsters)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        if (!_isSpectating) return;

        // Living players MUST NEVER stay in survivor spectator mode!
        if (modeType == SpectatorModeType.Survivors && !IsLocalPlayerDead())
        {
            Debug.Log("[SpectatorController] Local player is alive — Exiting Spectator Mode immediately.");
            StopSpectating();
            return;
        }

        // If game is paused, freeze spectator controls and HUD updates
        if (PauseManager.IsGamePaused) return;

        // Keep local player movement, look, and ability inputs suspended while spectating
        SuspendLocalPlayerMovement(true);

        // 1. Handle Navigation & Controls
        HandleInput();

        // 2. Validate current target (auto-switch if target just died or DC'd)
        ValidateCurrentTarget();

        // 3. Update HUD data (health bar, survivor counts)
        UpdateHUD();
    }

    private void LateUpdate()
    {
        if (!_isSpectating) return;
        if (PauseManager.IsGamePaused) return;

        // Smooth camera anchor positioning and rotation
        UpdateAnchorTransform();
    }

    private void HandleInput()
    {
        if (PauseManager.IsGamePaused) return;
        if (Keyboard.current == null && Mouse.current == null && Gamepad.current == null) return;

        // Exit Spectating: bound key or the configured exitHotkey (default C).
        // NOTE: Do NOT include escapeKey here. PauseManager owns Escape globally and
        // already routes it to ExitSpectating() when IsSpectating is true. If we also
        // handle Escape here, the two handlers fire in an indeterminate script-order:
        // whichever runs first sets _isSpectating=false, then the other sees
        // IsSpectating=false and opens the pause menu on the very same frame.
        if (KeybindingManager.IsActionTriggered("SpectateExit") 
            || (Keyboard.current != null && (Keyboard.current[exitHotkey].wasPressedThisFrame || Keyboard.current.cKey.wasPressedThisFrame)))
        {
            ExitSpectating();
            return;
        }

        // Switch Category (Survivors <-> Monsters): [Tab] or Gamepad Y / Triangle
        if (KeybindingManager.IsActionTriggered("SpectateCategory") 
            || (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame))
        {
            ToggleSpectatorCategory();
            return;
        }

        // Cycle Previous: [A], [Left Arrow], or Gamepad Left Shoulder
        bool prevPressed = KeybindingManager.IsActionTriggered("SpectatePrev")
            || (Keyboard.current != null && (Keyboard.current.aKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame))
            || KeybindingManager.IsGamepadButtonPressed("leftShoulder");

        // Cycle Next: [D], [Right Arrow], or Gamepad Right Shoulder
        bool nextPressed = KeybindingManager.IsActionTriggered("SpectateNext")
            || KeybindingManager.IsActionTriggered("SpectateCycle")
            || (Keyboard.current != null && (Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame))
            || KeybindingManager.IsGamepadButtonPressed("rightShoulder");

        // Toggle Free Orbit vs Follow Facing: [Space]
        if (KeybindingManager.IsActionTriggered("SpectateViewMode") || (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame))
        {
            ToggleOrbitMode();
        }

        // Toggle Cursor Lock: [Left Alt]
        if (KeybindingManager.IsActionTriggered("SpectateCursor") || (Keyboard.current != null && Keyboard.current.leftAltKey.wasPressedThisFrame))
        {
            ToggleCursorLock();
        }

        if (prevPressed) CyclePreviousSurvivor();
        else if (nextPressed) CycleNextSurvivor();

        // Mouse Orbit Input
        if (Mouse.current != null)
        {
            // Auto re-lock cursor on left click if it became unlocked
            if (Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            Vector2 delta = Mouse.current.delta.ReadValue();
            if (_freeOrbitMode && delta.sqrMagnitude > 0.0001f)
            {
                _yaw += delta.x * mouseSensitivity;
                float minPitch = (modeType == SpectatorModeType.Monsters) ? -15f : -35f;
                float maxPitch = (modeType == SpectatorModeType.Monsters) ? 55f : 75f;
                _pitch = Mathf.Clamp(_pitch - delta.y * mouseSensitivity, minPitch, maxPitch);
            }

            // Zoom Input
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _currentDistance = Mathf.Clamp(_currentDistance - (scroll * 0.005f), zoomRange.x, zoomRange.y);
            }
        }
    }

    public void CycleNextSurvivor()
    {
        RefreshAliveTargets();
        if (_aliveTargets.Count == 0) return;

        if (_currentTarget != null)
        {
            int curIdx = _aliveTargets.IndexOf(_currentTarget);
            if (curIdx >= 0) _currentTargetIndex = curIdx;
        }

        _currentTargetIndex = (_currentTargetIndex + 1) % _aliveTargets.Count;
        SelectTargetByIndex(_currentTargetIndex, true);
    }

    public void CyclePreviousSurvivor()
    {
        RefreshAliveTargets();
        if (_aliveTargets.Count == 0) return;

        if (_currentTarget != null)
        {
            int curIdx = _aliveTargets.IndexOf(_currentTarget);
            if (curIdx >= 0) _currentTargetIndex = curIdx;
        }

        _currentTargetIndex = (_currentTargetIndex - 1 + _aliveTargets.Count) % _aliveTargets.Count;
        SelectTargetByIndex(_currentTargetIndex, true);
    }

    private void SelectTargetByIndex(int index, bool snapImmediate = false)
    {
        if (_aliveTargets.Count == 0)
        {
            _currentTarget = null;
            return;
        }

        _currentTargetIndex = Mathf.Clamp(index, 0, _aliveTargets.Count - 1);
        _currentTarget = _aliveTargets[_currentTargetIndex];

        if (_currentTarget != null)
        {
            string pName = GetPlayerName(_currentTarget);
            ShowToast($"SPECTATING: {pName}", new Color(0.1f, 0.9f, 0.5f, 1f));

            if (modeType == SpectatorModeType.Monsters)
            {
                // Align camera directly in front of the monster facing its snarling face when it spawns/roars!
                _yaw = _currentTarget.transform.eulerAngles.y + 180f;
                _pitch = 10f; // Eye/chest level, looking directly into its face
                _currentDistance = 2.1f;
                _collisionDistance = 2.1f;
            }
            else
            {
                // Align initial camera angle behind target
                _yaw = _currentTarget.transform.eulerAngles.y;
                _pitch = 18f;
                _currentDistance = defaultCameraDistance;
                _collisionDistance = defaultCameraDistance;
            }

            if (_spectatorAnchor != null)
            {
                _spectatorAnchor.position = GetTargetFocusPoint(_currentTarget);
                _spectatorAnchor.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            BindCinemachineToAnchor();
        }
    }

    private void ValidateCurrentTarget()
    {
        if (_currentTarget == null || _currentTarget.gameObject == null ||
            _currentTarget.isCorpse.Value || _currentTarget.CurrentHealth <= 0)
        {
            string fallenName = _currentTarget != null ? GetPlayerName(_currentTarget) : "Survivor";
            ShowToast($"{fallenName} HAS FALLEN!\n<size=80%>Switching camera...</size>", new Color(1f, 0.3f, 0.3f, 1f));

            RefreshAliveTargets();
            if (_aliveTargets.Count > 0)
            {
                _currentTargetIndex = _currentTargetIndex % _aliveTargets.Count;
                SelectTargetByIndex(_currentTargetIndex);
            }
            else
            {
                if (modeType == SpectatorModeType.Monsters)
                {
                    Debug.Log("[SpectatorController] All summoned monsters have fallen. Returning to Girl gameplay.");
                    if (NotificationManager.Instance != null)
                    {
                        NotificationManager.Instance.ShowNotification("All summoned monsters have fallen!", 3f);
                    }
                    StopSpectating();
                }
                else if (DeathUI.Instance != null)
                {
                    // No more survivors — stop spectating and return to own death screen
                    Debug.Log("[SpectatorController] Last survivor has fallen. Returning to death screen.");
                    DeathUI.Instance.ReturnFromSpectatorToDeath();
                }
                else
                {
                    StopSpectating();
                }
            }
        }
    }

    private void RefreshAliveTargets()
    {
        _aliveTargets.Clear();

        if (modeType == SpectatorModeType.Monsters)
        {
            var monsterHealths = new List<TargetHealth>();

            var controllers = FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
            foreach (var mc in controllers)
            {
                if (mc == null || mc.gameObject == null) continue;
                if (mc.TryGetComponent<TargetHealth>(out var mth))
                {
                    if (mth.isCorpse.Value || mth.CurrentHealth <= 0) continue;
                    if (!monsterHealths.Contains(mth)) monsterHealths.Add(mth);
                }
            }

            var ais = FindObjectsByType<MonsterAI>(FindObjectsSortMode.None);
            foreach (var ai in ais)
            {
                if (ai == null || ai.gameObject == null) continue;
                if (ai.currentState == MonsterAI.AIState.Dead) continue;
                var ath = ai.GetComponent<TargetHealth>() ?? ai.GetComponentInChildren<TargetHealth>();
                if (ath == null)
                {
                    ath = ai.gameObject.AddComponent<TargetHealth>();
                    ath.baseMaxHealth = (ai.stats != null && ai.stats.maxHealth > 0) ? ai.stats.maxHealth : 100f;
                }
                if (ath != null)
                {
                    if (ath.isCorpse.Value || ath.CurrentHealth <= 0) continue;
                    if (!monsterHealths.Contains(ath)) monsterHealths.Add(ath);
                }
            }

            // Stable sort by instance ID so target order doesn't change every frame
            monsterHealths.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            _aliveTargets.AddRange(monsterHealths);

            if (_currentTarget != null)
            {
                int curIdx = _aliveTargets.IndexOf(_currentTarget);
                if (curIdx >= 0) _currentTargetIndex = curIdx;
            }
            return;
        }

        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

        var allHealths = FindObjectsByType<TargetHealth>(FindObjectsSortMode.None);
        foreach (var th in allHealths)
        {
            if (th == null || th.gameObject == null) continue;

            // Exclude local dead player
            var netObj = th.GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerClientId == localClientId) continue;

            // NEVER spectate the Girl (anti-ghosting)
            if (th.GetComponent<GirlPossession>() != null || th.GetComponent<GirlStealth>() != null ||
                th.gameObject.name.ToLower().Contains("girl"))
            {
                continue;
            }

            // Exclude dead players / corpses
            if (th.isCorpse.Value || th.CurrentHealth <= 0) continue;
            if (th.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead) continue;

            _aliveTargets.Add(th);
        }

        // Keep current index in bounds
        if (_aliveTargets.Count > 0)
        {
            _currentTargetIndex = Mathf.Clamp(_currentTargetIndex, 0, _aliveTargets.Count - 1);
        }
    }

    private void UpdateAnchorTransform()
    {
        if (_spectatorAnchor == null) return;

        if (_currentTarget != null && _currentTarget.gameObject != null)
        {
            Vector3 targetFocus = GetTargetFocusPoint(_currentTarget);

            // Smooth position tracking
            _spectatorAnchor.position = Vector3.Lerp(_spectatorAnchor.position, targetFocus, Time.deltaTime * followSmoothness);

            // If the monster is crawling, smoothly bias pitch to an elevated top-down angle
            if (modeType == SpectatorModeType.Monsters && _currentTarget.TryGetComponent<MonsterAI>(out var crawlerAI))
            {
                if (crawlerAI.currentPosture == MonsterAI.ZombiePosture.Crawling)
                {
                    // High angle top-down view looking down from above at the crawling monster
                    _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(_pitch, 35f, 52f), Time.deltaTime * 3.5f);
                }
            }

            if (_freeOrbitMode)
            {
                // Free orbit: driven by mouse
                _spectatorAnchor.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else
            {
                // Over-the-shoulder follow: aligns with player facing
                Quaternion targetRot = Quaternion.Euler(15f, _currentTarget.transform.eulerAngles.y, 0f);
                _spectatorAnchor.rotation = Quaternion.Slerp(_spectatorAnchor.rotation, targetRot, Time.deltaTime * 8f);
                _yaw = _currentTarget.transform.eulerAngles.y;
            }
        }

        // Camera positioning
        ResolveCamera();
        if (_cinemachineCam != null && _spectatorCamTransform != null && _spectatorAnchor != null)
        {
            if (!_spectatorCamTransform.gameObject.activeSelf) _spectatorCamTransform.gameObject.SetActive(true);
            if (!_cinemachineCam.gameObject.activeSelf) _cinemachineCam.gameObject.SetActive(true);
            if (!_cinemachineCam.enabled) _cinemachineCam.enabled = true;
            if (_cinemachineCam.Priority < 999999) _cinemachineCam.Priority = 999999;

            _cinemachineCam.Follow = null;
            _cinemachineCam.LookAt = null;

            Vector3 focusPoint = _spectatorAnchor.position + Vector3.up * 0.15f;
            Vector3 backwardOffset = _spectatorAnchor.rotation * new Vector3(0f, 0.2f, -_currentDistance);
            Vector3 desiredCamPos = _spectatorAnchor.position + backwardOffset;
            Vector3 toCam = desiredCamPos - focusPoint;
            float targetDist = toCam.magnitude;

            if (targetDist > 0.05f)
            {
                Vector3 rayDir = toCam.normalized;
                float sphereRadius = 0.12f; // Compact radius so it doesn't snag on roof beams/thin ledges
                // Exclude characters, ragdolls, and non-physical UI/minimap layers
                int obstacleMask = ~LayerMask.GetMask("Ignore Raycast", "UI", "Monster", "Player", "Explorer", "Minimap", "Fog");

                float nearestDist = targetDist;
                bool obstacleHit = false;

                // 1. SphereCastAll to detect thick solid walls, pillars, tunnel ceilings
                RaycastHit[] sphereHits = Physics.SphereCastAll(focusPoint, sphereRadius, rayDir, targetDist, obstacleMask, QueryTriggerInteraction.Ignore);
                foreach (var h in sphereHits)
                {
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (_currentTarget != null && (h.collider.transform.root == _currentTarget.transform.root || h.collider.transform == _currentTarget.transform)) continue;

                    if (h.distance > 0.01f && h.distance < nearestDist)
                    {
                        nearestDist = h.distance;
                        obstacleHit = true;
                    }
                }

                // 2. RaycastAll as a precision check
                RaycastHit[] rayHits = Physics.RaycastAll(focusPoint, rayDir, targetDist, obstacleMask, QueryTriggerInteraction.Ignore);
                foreach (var rh in rayHits)
                {
                    if (rh.collider == null || rh.collider.isTrigger) continue;
                    if (_currentTarget != null && (rh.collider.transform.root == _currentTarget.transform.root || rh.collider.transform == _currentTarget.transform)) continue;

                    if (rh.distance > 0.01f && rh.distance < nearestDist)
                    {
                        nearestDist = rh.distance;
                        obstacleHit = true;
                    }
                }

                float targetCollisionDist = obstacleHit ? Mathf.Max(0.35f, nearestDist - 0.08f) : targetDist;

                // Smooth collision response:
                // Instantly pull in to prevent clipping inside walls/ceiling
                if (targetCollisionDist < _collisionDistance)
                {
                    _collisionDistance = targetCollisionDist;
                }
                else
                {
                    // Smoothly glide back out when obstacle clears, preventing glitching / bouncing
                    _collisionDistance = Mathf.MoveTowards(_collisionDistance, targetCollisionDist, Time.deltaTime * 5f);
                }

                desiredCamPos = focusPoint + (rayDir * _collisionDistance);
            }

            _spectatorCamTransform.position = desiredCamPos;
            Vector3 lookDir = focusPoint - _spectatorCamTransform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                _spectatorCamTransform.rotation = Quaternion.LookRotation(lookDir);
            }

            if (Camera.main != null)
            {
                Camera.main.transform.position = _spectatorCamTransform.position;
                Camera.main.transform.rotation = _spectatorCamTransform.rotation;
            }
        }
        else if (Camera.main != null && _spectatorAnchor != null)
        {
            // Direct camera positioning fallback if Cinemachine is not bound
            Vector3 focusPoint = _spectatorAnchor.position + Vector3.up * 0.15f;
            Vector3 backwardOffset = _spectatorAnchor.rotation * new Vector3(0f, 0.2f, -_currentDistance);
            Vector3 desiredCamPos = _spectatorAnchor.position + backwardOffset;
            Vector3 toCam = desiredCamPos - focusPoint;
            float targetDist = toCam.magnitude;

            if (targetDist > 0.05f)
            {
                Vector3 rayDir = toCam.normalized;
                float sphereRadius = 0.12f;
                int obstacleMask = ~LayerMask.GetMask("Ignore Raycast", "UI", "Monster", "Player", "Explorer", "Minimap", "Fog");

                float nearestDist = targetDist;
                bool obstacleHit = false;

                RaycastHit[] sphereHits = Physics.SphereCastAll(focusPoint, sphereRadius, rayDir, targetDist, obstacleMask, QueryTriggerInteraction.Ignore);
                foreach (var h in sphereHits)
                {
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (_currentTarget != null && (h.collider.transform.root == _currentTarget.transform.root || h.collider.transform == _currentTarget.transform)) continue;

                    if (h.distance > 0.01f && h.distance < nearestDist)
                    {
                        nearestDist = h.distance;
                        obstacleHit = true;
                    }
                }

                RaycastHit[] rayHits = Physics.RaycastAll(focusPoint, rayDir, targetDist, obstacleMask, QueryTriggerInteraction.Ignore);
                foreach (var rh in rayHits)
                {
                    if (rh.collider == null || rh.collider.isTrigger) continue;
                    if (_currentTarget != null && (rh.collider.transform.root == _currentTarget.transform.root || rh.collider.transform == _currentTarget.transform)) continue;

                    if (rh.distance > 0.01f && rh.distance < nearestDist)
                    {
                        nearestDist = rh.distance;
                        obstacleHit = true;
                    }
                }

                float targetCollisionDist = obstacleHit ? Mathf.Max(0.35f, nearestDist - 0.08f) : targetDist;

                if (targetCollisionDist < _collisionDistance)
                {
                    _collisionDistance = targetCollisionDist;
                }
                else
                {
                    _collisionDistance = Mathf.MoveTowards(_collisionDistance, targetCollisionDist, Time.deltaTime * 5f);
                }

                desiredCamPos = focusPoint + (rayDir * _collisionDistance);
            }

            Camera.main.transform.position = desiredCamPos;
            Camera.main.transform.LookAt(focusPoint);
        }
    }

    private Vector3 GetTargetFocusPoint(TargetHealth target)
    {
        if (target == null) return Vector3.zero;

        if (target.TryGetComponent<MonsterController>(out var mc) && mc.GetCameraTarget() != null)
        {
            return mc.GetCameraTarget().position;
        }

        // Try getting CinemachineCameraTarget or PlayerCameraRoot first
        if (target.TryGetComponent<StarterAssets.ThirdPersonController>(out var tpc) && tpc.CinemachineCameraTarget != null)
        {
            return tpc.CinemachineCameraTarget.transform.position;
        }

        Transform root = target.transform.Find("PlayerCameraRoot") ?? target.transform.Find("CameraFollowAnchor");
        if (root != null) return root.position;

        if (target.TryGetComponent<MonsterAI>(out var mai))
        {
            if (mai.currentPosture == MonsterAI.ZombiePosture.Crawling)
            {
                // Lower focus point directly over crawling monster
                return target.transform.position + Vector3.up * 0.45f;
            }
            return target.transform.position + Vector3.up * 1.25f;
        }

        return target.transform.position + Vector3.up * 1.35f;
    }

    private void ResolveCamera()
    {
        CreateSpectatorAnchor();
    }

    private void BindCinemachineToAnchor()
    {
        ResolveCamera();
        if (_cinemachineCam != null && _spectatorCamTransform != null && _spectatorAnchor != null)
        {
            _spectatorCamTransform.gameObject.SetActive(true);
            _cinemachineCam.gameObject.SetActive(true);
            _cinemachineCam.enabled = true;
            _cinemachineCam.Priority = 999999;
            _cinemachineCam.Follow = null;
            _cinemachineCam.LookAt = null;

            CinemachineBrain brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : FindFirstObjectByType<CinemachineBrain>();
            if (brain != null)
            {
                brain.ActiveBlend = null;
            }

            Vector3 backwardOffset = _spectatorAnchor.rotation * new Vector3(0f, 0.35f, -_currentDistance);
            _spectatorCamTransform.position = _spectatorAnchor.position + backwardOffset;
            Vector3 lookTarget = _spectatorAnchor.position + Vector3.up * 0.25f;
            Vector3 lookDir = lookTarget - _spectatorCamTransform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                _spectatorCamTransform.rotation = Quaternion.LookRotation(lookDir);
            }
        }
    }

    private void UpdateHUD()
    {
        RefreshAliveTargets();

        // 1. Survivors / Living Count & Total Connected Players
        int totalConnected = 1;
        if (NetworkManager.Singleton != null)
        {
            totalConnected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        }

        int aliveCount = _aliveTargets.Count;

        if (modeType == SpectatorModeType.Monsters)
        {
            int summoned = 0;
            if (NightCrawler.Monsters.DeadSpawnManager.Instance != null)
            {
                summoned = NightCrawler.Monsters.DeadSpawnManager.Instance.totalMonstersSummoned.Value;
            }
            if (NightCrawler.Monsters.GirlMonsterSummonHUD.Instance != null)
            {
                summoned = Mathf.Max(summoned, NightCrawler.Monsters.GirlMonsterSummonHUD.Instance.TotalSummoned);
            }

            // Strictly monotonic: only increases, never decreases when monsters die
            _maxMonstersSummoned = Mathf.Max(_maxMonstersSummoned, Mathf.Max(summoned, aliveCount));

            if (cleanCountText != null)
            {
                cleanCountText.text = aliveCount.ToString();
            }

            if (totalConnectedPlayersText != null)
            {
                totalConnectedPlayersText.text = _maxMonstersSummoned.ToString();
            }

            if (_survivorsCountText != null)
            {
                _survivorsCountText.text = $"MONSTERS: <b>{aliveCount}</b> / {_maxMonstersSummoned}";
            }
        }
        else
        {
            int totalSurvivors = Mathf.Max(1, totalConnected - 1);

            if (cleanCountText != null)
            {
                cleanCountText.text = aliveCount.ToString();
            }

            if (totalConnectedPlayersText != null)
            {
                totalConnectedPlayersText.text = totalSurvivors.ToString();
            }

            if (_survivorsCountText != null)
            {
                _survivorsCountText.text = $"SURVIVORS: <b>{aliveCount}</b> / {Mathf.Max(aliveCount, totalSurvivors)}";
            }
        }

        // 2. Target info or All Fallen state
        if (_currentTarget != null)
        {
            string pName = GetPlayerName(_currentTarget);
            string upperName = pName.ToUpper();
            string pRole = GetPlayerRole(_currentTarget);
            float curHp = Mathf.Max(0f, _currentTarget.CurrentHealth);
            float maxHp = Mathf.Max(1f, _currentTarget.MaxHealth);
            float hpPercent = Mathf.Clamp01(curHp / maxHp);

            if (cleanTargetNameText != null)
            {
                cleanTargetNameText.text = upperName;
            }
            if (_targetNameText != null)
            {
                _targetNameText.text = upperName;
            }
            if (_roleBadgeText != null)
            {
                _roleBadgeText.text = $"[{pRole.ToUpper()}]";
            }
            if (_healthReadoutText != null)
            {
                _healthReadoutText.text = $"{Mathf.CeilToInt(curHp)} / {Mathf.CeilToInt(maxHp)} HP";
            }
            if (_healthFillImage != null)
            {
                _healthFillImage.fillAmount = hpPercent;
                // Dynamic health color
                if (hpPercent > 0.5f) _healthFillImage.color = new Color(0f, 0.9f, 0.45f, 1f);
                else if (hpPercent > 0.25f) _healthFillImage.color = new Color(1f, 0.65f, 0.1f, 1f);
                else _healthFillImage.color = new Color(1f, 0.2f, 0.25f, 1f);
            }
            NightCrawler.UI.MichskyUIBridge.SetProgress(heatHealthProgressBar, curHp, maxHp);
        }
        else
        {
            string emptyMsg = modeType == SpectatorModeType.Monsters ? "NO ACTIVE MONSTERS" : "ALL SURVIVORS HAVE FALLEN";
            if (cleanTargetNameText != null) cleanTargetNameText.text = emptyMsg;
            if (_targetNameText != null) _targetNameText.text = emptyMsg;
            if (_roleBadgeText != null) _roleBadgeText.text = "";
            if (_healthReadoutText != null) _healthReadoutText.text = "Awaiting match outcome...";
            if (_healthFillImage != null) _healthFillImage.fillAmount = 0f;
            NightCrawler.UI.MichskyUIBridge.SetProgress(heatHealthProgressBar, 0f);
        }
    }

    private static bool IsMonsterTarget(TargetHealth th)
    {
        if (th == null) return false;
        if (th.GetComponentInParent<MonsterAI>() != null || th.GetComponentInChildren<MonsterAI>() != null) return true;
        if (th.GetComponentInParent<MonsterController>() != null || th.GetComponentInChildren<MonsterController>() != null) return true;
        string n = th.gameObject.name.ToLower();
        return n.Contains("monster") || n.Contains("undead") || n.Contains("berserker") || n.Contains("zombie") || (n.Contains("demon") && !n.Contains("girl"));
    }

    private string GetPlayerName(TargetHealth th)
    {
        if (th == null) return modeType == SpectatorModeType.Monsters ? "MONSTER" : "INVESTIGATOR";

        if (modeType == SpectatorModeType.Monsters || IsMonsterTarget(th))
        {
            return GetMonsterDisplayName(th);
        }

        if (th.TryGetComponent<NetworkPlayerName>(out var npn) && !string.IsNullOrEmpty(npn.playerName.Value.ToString()))
        {
            string val = npn.playerName.Value.ToString();
            if (!val.StartsWith("Player ") || val.Length > 8) return val;
        }

        if (th.TryGetComponent<NetworkObject>(out var netObj))
        {
            string pName = PlayerNameManager.GetPlayerName(netObj.OwnerClientId);
            if (!string.IsNullOrEmpty(pName) && !pName.StartsWith("Player ")) return pName;
        }

        string cleanName = th.gameObject.name.Replace("(Clone)", "").Trim();
        return cleanName;
    }

    private string GetMonsterDisplayName(TargetHealth th)
    {
        if (th == null) return "MONSTER";

        string baseName = "UNDEAD";
        if (th.TryGetComponent<MonsterAI>(out var ai))
        {
            baseName = ai.monsterType == MonsterAI.MonsterType.Berserker ? "BERSERKER" : "UNDEAD";
        }
        else if (th.gameObject.name.ToLower().Contains("berserker"))
        {
            baseName = "BERSERKER";
        }

        // Count index among monsters of the same type in _aliveTargets
        int typeIndex = 1;
        int totalOfType = 0;
        for (int i = 0; i < _aliveTargets.Count; i++)
        {
            var target = _aliveTargets[i];
            if (target == null) continue;

            string otherBase = "UNDEAD";
            if (target.TryGetComponent<MonsterAI>(out var otherAi))
            {
                otherBase = otherAi.monsterType == MonsterAI.MonsterType.Berserker ? "BERSERKER" : "UNDEAD";
            }
            else if (target.gameObject.name.ToLower().Contains("berserker"))
            {
                otherBase = "BERSERKER";
            }

            if (otherBase == baseName)
            {
                totalOfType++;
                if (target == th)
                {
                    typeIndex = totalOfType;
                }
            }
        }

        return $"{baseName} #{typeIndex}";
    }

    private string GetPlayerRole(TargetHealth th)
    {
        if (th == null) return modeType == SpectatorModeType.Monsters ? "Monster" : "Investigator";

        if (modeType == SpectatorModeType.Monsters || IsMonsterTarget(th))
        {
            string n = th.gameObject.name.ToLower();
            if (th.TryGetComponent<MonsterAI>(out var ai) && ai.monsterType == MonsterAI.MonsterType.Berserker) return "Berserker";
            if (n.Contains("berserker") || n.Contains("mutant") || n.Contains("brute")) return "Berserker";
            return "Undead";
        }

        string name = th.gameObject.name.ToLower();
        if (name.Contains("miner")) return "Miner";
        if (name.Contains("adventurer")) return "Adventurer";
        if (name.Contains("doctor") || name.Contains("medic")) return "Medic";
        if (name.Contains("detective")) return "Detective";
        return "Investigator";
    }

    private bool IsLocalPlayerGirl()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localObj != null && (localObj.GetComponent<GirlPossession>() != null || localObj.name.ToLower().Contains("girl")))
            {
                return true;
            }
        }
        return false;
    }

    private void ShowToast(string message, Color color)
    {
        if (_toastText == null || _toastCanvasGroup == null || _toastBanner == null) return;

        if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
        _toastCoroutine = StartCoroutine(ToastRoutine(message, color));
    }

    private IEnumerator ToastRoutine(string message, Color color)
    {
        _toastText.text = message;
        _toastText.color = color;
        _toastBanner.SetActive(true);

        // Fade in
        yield return StartCoroutine(FadeCanvasGroup(_toastCanvasGroup, _toastCanvasGroup.alpha, 1f, 0.2f));

        yield return new WaitForSeconds(1.8f);

        // Fade out
        yield return StartCoroutine(FadeCanvasGroup(_toastCanvasGroup, 1f, 0f, 0.35f));
        _toastBanner.SetActive(false);
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration, System.Action onComplete = null)
    {
        if (cg == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        cg.alpha = to;
        onComplete?.Invoke();
    }

    /// <summary>
    /// Re-asserts PlayerFollowCamera as the highest priority virtual camera across
    /// several end-of-frames. This forces the CinemachineBrain to stay locked onto
    /// the Girl's camera even if another system tries to reassert priority concurrently.
    /// We NEVER touch Camera.main.transform — the Brain owns that transform.
    /// </summary>
    private IEnumerator ReassertPlayerCamPriority(CinemachineVirtualCameraBase followCam, CinemachineBrain brain, int frames = 5)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return new WaitForEndOfFrame();

            if (followCam == null) yield break;

            followCam.gameObject.SetActive(true);
            followCam.enabled = true;
            followCam.Priority = 99999;
            followCam.PreviousStateIsValid = false;

            if (brain != null)
            {
                // NOTE: Do NOT call brain.ManualUpdate() — that requires the Brain to be in
                // ManualUpdate update mode (set via Inspector). In default mode the Brain
                // updates itself automatically via LateUpdate; setting priority high and
                // clearing the blend is sufficient to guarantee a cut on the next frame.
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
                brain.ActiveBlend = null;
            }
        }
    }

    private IEnumerator ClearStoppingFlag()
    {
        // Wait longer than ReassertPlayerCamPriority (5 frames) plus the HUD fade (0.4s)
        yield return new WaitForSecondsRealtime(0.6f);
        _isStopping = false;
    }

    // =========================================================================
    //  Procedural Tactical Dark HUD Generation
    // =========================================================================

    private void BuildProceduralHUD()
    {
        if (customCanvasRoot != null || cleanTargetNameText != null)
        {
            GameObject root = customCanvasRoot != null ? customCanvasRoot : cleanTargetNameText.gameObject;
            _canvasGroup = root.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = root.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            if (customCanvasRoot != null) customCanvasRoot.SetActive(false);

            _roleBadgeText = customRoleText;
            InitHotkeys();
            return;
        }

        // 1. Root Canvas
        var canvasObj = new GameObject("SpectatorHUD_Canvas");
        if (Application.isPlaying) DontDestroyOnLoad(canvasObj);

        _spectatorCanvas = canvasObj.AddComponent<Canvas>();
        _spectatorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _spectatorCanvas.sortingOrder = 95; // Right under match victory overlays

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        _canvasGroup = canvasObj.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        canvasObj.SetActive(false);

        // 2. Cinematic Vignette (Subtle Dark Observer Atmosphere)
        var vignetteObj = new GameObject("VignetteOverlay");
        vignetteObj.transform.SetParent(canvasObj.transform, false);
        var vigRect = vignetteObj.AddComponent<RectTransform>();
        vigRect.anchorMin = Vector2.zero;
        vigRect.anchorMax = Vector2.one;
        vigRect.sizeDelta = Vector2.zero;

        var vigImg = vignetteObj.AddComponent<Image>();
        vigImg.color = new Color(0f, 0f, 0f, 0.18f); // Gentle darkening
        vigImg.raycastTarget = false;

        // 3. Top Center Spectating Header (Frosted Glass Capsule)
        var topHeaderObj = new GameObject("TopSpectatorHeader");
        topHeaderObj.transform.SetParent(canvasObj.transform, false);
        var headerRect = topHeaderObj.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0.5f, 1f);
        headerRect.anchorMax = new Vector2(0.5f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0f, -35f);
        headerRect.sizeDelta = new Vector2(520f, 85f);

        var headerBg = topHeaderObj.AddComponent<Image>();
        headerBg.color = new Color(0.05f, 0.07f, 0.10f, 0.88f);
        headerBg.raycastTarget = false;

        var headerOutline = topHeaderObj.AddComponent<Outline>();
        headerOutline.effectColor = new Color(0.25f, 0.35f, 0.5f, 0.45f);
        headerOutline.effectDistance = new Vector2(1.5f, -1.5f);

        // Header Content: Target Name + Role
        var nameObj = new GameObject("TargetNameText");
        nameObj.transform.SetParent(topHeaderObj.transform, false);
        var nameRect = nameObj.AddComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 0.45f);
        nameRect.anchorMax = new Vector2(0.7f, 1f);
        nameRect.offsetMin = new Vector2(25f, 0f);
        nameRect.offsetMax = new Vector2(0f, -8f);

        _targetNameText = nameObj.AddComponent<TextMeshProUGUI>();
        _targetNameText.fontSize = 22f;
        _targetNameText.color = Color.white;
        _targetNameText.alignment = TextAlignmentOptions.MidlineLeft;
        _targetNameText.text = "<b>SPECTATING</b>";

        // Role Badge
        var roleObj = new GameObject("RoleBadgeText");
        roleObj.transform.SetParent(topHeaderObj.transform, false);
        var roleRect = roleObj.AddComponent<RectTransform>();
        roleRect.anchorMin = new Vector2(0.65f, 0.45f);
        roleRect.anchorMax = new Vector2(1f, 1f);
        roleRect.offsetMin = new Vector2(0f, 0f);
        roleRect.offsetMax = new Vector2(-25f, -8f);

        _roleBadgeText = roleObj.AddComponent<TextMeshProUGUI>();
        _roleBadgeText.fontSize = 18f;
        _roleBadgeText.color = new Color(1f, 0.75f, 0.1f, 1f);
        _roleBadgeText.alignment = TextAlignmentOptions.MidlineRight;
        _roleBadgeText.text = "INVESTIGATOR";

        // Health Bar Background
        var hpBgObj = new GameObject("HealthBarBg");
        hpBgObj.transform.SetParent(topHeaderObj.transform, false);
        var hpBgRect = hpBgObj.AddComponent<RectTransform>();
        hpBgRect.anchorMin = new Vector2(0f, 0f);
        hpBgRect.anchorMax = new Vector2(1f, 0.38f);
        hpBgRect.offsetMin = new Vector2(25f, 12f);
        hpBgRect.offsetMax = new Vector2(-25f, 0f);

        var hpBgImg = hpBgObj.AddComponent<Image>();
        hpBgImg.color = new Color(0.12f, 0.15f, 0.20f, 0.95f);
        hpBgImg.raycastTarget = false;

        // Health Bar Fill
        var hpFillObj = new GameObject("HealthBarFill");
        hpFillObj.transform.SetParent(hpBgObj.transform, false);
        var hpFillRect = hpFillObj.AddComponent<RectTransform>();
        hpFillRect.anchorMin = Vector2.zero;
        hpFillRect.anchorMax = Vector2.one;
        hpFillRect.offsetMin = Vector2.zero;
        hpFillRect.offsetMax = Vector2.zero;

        _healthFillImage = hpFillObj.AddComponent<Image>();
        _healthFillImage.type = Image.Type.Filled;
        _healthFillImage.fillMethod = Image.FillMethod.Horizontal;
        _healthFillImage.fillOrigin = 0;
        _healthFillImage.fillAmount = 1f;
        _healthFillImage.color = new Color(0f, 0.9f, 0.45f, 1f);
        _healthFillImage.raycastTarget = false;

        // Health Readout Text
        var hpTxtObj = new GameObject("HealthReadoutText");
        hpTxtObj.transform.SetParent(hpBgObj.transform, false);
        var hpTxtRect = hpTxtObj.AddComponent<RectTransform>();
        hpTxtRect.anchorMin = Vector2.zero;
        hpTxtRect.anchorMax = Vector2.one;
        hpTxtRect.offsetMin = Vector2.zero;
        hpTxtRect.offsetMax = Vector2.zero;

        _healthReadoutText = hpTxtObj.AddComponent<TextMeshProUGUI>();
        _healthReadoutText.fontSize = 12f;
        _healthReadoutText.color = Color.white;
        _healthReadoutText.alignment = TextAlignmentOptions.Center;
        _healthReadoutText.text = "100 / 100 HP";

        // 4. Top Right Survivors Counter Pill
        var survivorsObj = new GameObject("SurvivorsCountPill");
        survivorsObj.transform.SetParent(canvasObj.transform, false);
        var survRect = survivorsObj.AddComponent<RectTransform>();
        survRect.anchorMin = new Vector2(1f, 1f);
        survRect.anchorMax = new Vector2(1f, 1f);
        survRect.pivot = new Vector2(1f, 1f);
        survRect.anchoredPosition = new Vector2(-35f, -35f);
        survRect.sizeDelta = new Vector2(250f, 44f);

        var survBg = survivorsObj.AddComponent<Image>();
        survBg.color = new Color(0.05f, 0.07f, 0.10f, 0.88f);
        survBg.raycastTarget = false;

        var survOutline = survivorsObj.AddComponent<Outline>();
        survOutline.effectColor = new Color(0.25f, 0.35f, 0.5f, 0.45f);
        survOutline.effectDistance = new Vector2(1f, -1f);

        var survTxtObj = new GameObject("SurvivorsText");
        survTxtObj.transform.SetParent(survivorsObj.transform, false);
        var survTxtRect = survTxtObj.AddComponent<RectTransform>();
        survTxtRect.anchorMin = Vector2.zero;
        survTxtRect.anchorMax = Vector2.one;
        survTxtRect.offsetMin = new Vector2(15f, 0f);
        survTxtRect.offsetMax = new Vector2(-15f, 0f);

        _survivorsCountText = survTxtObj.AddComponent<TextMeshProUGUI>();
        _survivorsCountText.fontSize = 15f;
        _survivorsCountText.color = Color.white;
        _survivorsCountText.alignment = TextAlignmentOptions.Center;
        _survivorsCountText.text = "SURVIVORS: <b>0</b>";

        // 5. Bottom Navigation Bar Pill
        var bottomBarObj = new GameObject("BottomControlsBar");
        bottomBarObj.transform.SetParent(canvasObj.transform, false);
        var btmRect = bottomBarObj.AddComponent<RectTransform>();
        btmRect.anchorMin = new Vector2(0.5f, 0f);
        btmRect.anchorMax = new Vector2(0.5f, 0f);
        btmRect.pivot = new Vector2(0.5f, 0f);
        btmRect.anchoredPosition = new Vector2(0f, 35f);
        btmRect.sizeDelta = new Vector2(680f, 65f);

        var btmBg = bottomBarObj.AddComponent<Image>();
        btmBg.color = new Color(0.05f, 0.07f, 0.10f, 0.88f);
        btmBg.raycastTarget = false;

        var btmOutline = bottomBarObj.AddComponent<Outline>();
        btmOutline.effectColor = new Color(0.25f, 0.35f, 0.5f, 0.45f);
        btmOutline.effectDistance = new Vector2(1.5f, 1.5f);

        var navPromptObj = new GameObject("NavPromptText");
        navPromptObj.transform.SetParent(bottomBarObj.transform, false);
        var navRect = navPromptObj.AddComponent<RectTransform>();
        navRect.anchorMin = new Vector2(0f, 0.4f);
        navRect.anchorMax = new Vector2(1f, 1f);
        navRect.offsetMin = Vector2.zero;
        navRect.offsetMax = Vector2.zero;

        var navTxt = navPromptObj.AddComponent<TextMeshProUGUI>();
        navTxt.fontSize = 16f;
        navTxt.color = Color.white;
        navTxt.alignment = TextAlignmentOptions.Center;
        navTxt.text = "<  [A / L-CLICK] PREV      <b>CYCLE SURVIVORS</b>      [D / R-CLICK] NEXT  >";

        var subPromptObj = new GameObject("SubPromptText");
        subPromptObj.transform.SetParent(bottomBarObj.transform, false);
        var subRect = subPromptObj.AddComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0f, 0f);
        subRect.anchorMax = new Vector2(1f, 0.42f);
        subRect.offsetMin = Vector2.zero;
        subRect.offsetMax = Vector2.zero;

        _modePromptText = subPromptObj.AddComponent<TextMeshProUGUI>();
        _modePromptText.fontSize = 12f;
        _modePromptText.color = new Color(0.65f, 0.75f, 0.85f, 1f);
        _modePromptText.alignment = TextAlignmentOptions.Center;
        _modePromptText.text = "[SPACE] View Mode: FREE ORBIT   •   [MOUSE] Orbit   •   [SCROLL] Zoom   •   [ALT] Cursor";

        // 6. Center Toast Banner (Sleek Switching / Event Banner)
        _toastBanner = new GameObject("SpectatorToastBanner");
        _toastBanner.transform.SetParent(canvasObj.transform, false);
        var toastRect = _toastBanner.AddComponent<RectTransform>();
        toastRect.anchorMin = new Vector2(0.5f, 0.75f);
        toastRect.anchorMax = new Vector2(0.5f, 0.75f);
        toastRect.pivot = new Vector2(0.5f, 0.5f);
        toastRect.anchoredPosition = Vector2.zero;
        toastRect.sizeDelta = new Vector2(460f, 48f);

        var toastBg = _toastBanner.AddComponent<Image>();
        toastBg.color = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        toastBg.raycastTarget = false;

        var toastOutline = _toastBanner.AddComponent<Outline>();
        toastOutline.effectColor = new Color(0.2f, 0.8f, 1f, 0.5f);
        toastOutline.effectDistance = new Vector2(1f, 1f);

        _toastCanvasGroup = _toastBanner.AddComponent<CanvasGroup>();
        _toastCanvasGroup.alpha = 0f;

        var toastTxtObj = new GameObject("ToastText");
        toastTxtObj.transform.SetParent(_toastBanner.transform, false);
        var toastTxtRect = toastTxtObj.AddComponent<RectTransform>();
        toastTxtRect.anchorMin = Vector2.zero;
        toastTxtRect.anchorMax = Vector2.one;
        toastTxtRect.offsetMin = Vector2.zero;
        toastTxtRect.offsetMax = Vector2.zero;

        _toastText = toastTxtObj.AddComponent<TextMeshProUGUI>();
        _toastText.fontSize = 15f;
        _toastText.color = Color.white;
        _toastText.alignment = TextAlignmentOptions.Center;
        _toastText.text = "SPECTATING";

        _toastBanner.SetActive(false);
    }
}
