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
    public static SpectatorController Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SpectatorController>(FindObjectsInactive.Include);
                if (_instance == null)
                {
                    var go = new GameObject("SpectatorController");
                    _instance = go.AddComponent<SpectatorController>();
                    if (Application.isPlaying)
                    {
                        DontDestroyOnLoad(go);
                    }
                }
            }
            return _instance;
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
    [Tooltip("Keyboard key to exit spectator mode (default: Escape).")]
    public Key exitHotkey = Key.Escape;

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

    [Header("Michsky Heat / Dark UI")]
    [Tooltip("Optional: Michsky ProgressBar to display spectated player's health.")]
    public Michsky.UI.Heat.ProgressBar heatHealthProgressBar;

    // Runtime state
    private bool _isSpectating = false;
    public bool IsSpectating => _isSpectating;

    private readonly List<TargetHealth> _aliveTargets = new List<TargetHealth>();
    private int _currentTargetIndex = 0;
    private TargetHealth _currentTarget;

    // Camera targets & Cinemachine binding
    private Transform _spectatorAnchor;
    private CinemachineVirtualCameraBase _cinemachineCam;
    private float _yaw = 0f;
    private float _pitch = 18f;
    private float _currentDistance = 3.8f;
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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _currentDistance = defaultCameraDistance;
        CreateSpectatorAnchor();
        BuildProceduralHUD();
    }

    private void OnEnable()
    {
        PauseManager.OnPauseStateChanged += HandlePauseStateChanged;
    }

    private void OnDisable()
    {
        PauseManager.OnPauseStateChanged -= HandlePauseStateChanged;
    }

    private void HandlePauseStateChanged(bool isPaused)
    {
        if (_canvasGroup != null && _isSpectating)
        {
            _canvasGroup.alpha = isPaused ? 0f : 1f;
            _canvasGroup.blocksRaycasts = !isPaused;
        }

        SetHotkeysActive(!isPaused);
    }

    private void SetHotkeysActive(bool active)
    {
        if (prevHotkey != null) prevHotkey.enabled = active;
        if (nextHotkey != null) nextHotkey.enabled = active;
        if (viewModeHotkey != null) viewModeHotkey.enabled = active;
        if (cursorHotkey != null) cursorHotkey.enabled = active;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_spectatorAnchor != null)
        {
            Destroy(_spectatorAnchor.gameObject);
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

        // Lock cursor for smooth mouse look
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Refresh targets and snap to the first alive teammate
        RefreshAliveTargets();
        _currentTargetIndex = 0;
        SelectTargetByIndex(_currentTargetIndex, true);

        // Initialize and sync Hotkey labels
        InitHotkeys();
        if (viewModeHotkey != null) viewModeHotkey.SetLabel(_freeOrbitMode ? "FREE ORBIT" : "SHOULDER CAM");
        if (cursorHotkey != null) cursorHotkey.SetLabel(Cursor.lockState == CursorLockMode.Locked ? "UNLOCK CURSOR" : "LOCK CURSOR");

        // Fade in HUD
        if (_canvasGroup != null)
        {
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
        }
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
            if (customCanvasRoot != null) customCanvasRoot.SetActive(false);
            if (_spectatorCanvas != null) _spectatorCanvas.gameObject.SetActive(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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
    public void TryOpenMonsterSpectator(Michsky.UI.Heat.ModalWindowManager modal = null)
    {
        if (!HasActiveMonstersInScene())
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
    }

    /// <summary>
    /// Toggles between Free Orbit and Follow Over-Shoulder camera modes.
    /// </summary>
    public void ToggleOrbitMode()
    {
        _freeOrbitMode = !_freeOrbitMode;
        string modeName = _freeOrbitMode ? "FREE ORBIT" : "SHOULDER CAM";
        ShowToast($"CAMERA MODE: {modeName}", new Color(1f, 0.85f, 0.2f, 1f));
        if (viewModeHotkey != null)
        {
            viewModeHotkey.SetLabel(modeName);
        }
        if (_modePromptText != null)
        {
            _modePromptText.text = $"[SPACE] View Mode: {modeName}   •   [MOUSE] Orbit   •   [SCROLL] Zoom   •   [ALT] Cursor";
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

    /// <summary>
    /// Gracefully exits spectator mode (e.g. if revived or match ends).
    /// </summary>
    public void StopSpectating()
    {
        if (!_isSpectating) return;

        _isSpectating = false;
        _currentTarget = null;

        // Restore camera priority & binding so local player's camera resumes normally
        if (_cinemachineCam != null)
        {
            _cinemachineCam.Priority = 10;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                Transform camTarget = (localObj.TryGetComponent<StarterAssets.ThirdPersonController>(out var tpc) && tpc.CinemachineCameraTarget != null)
                    ? tpc.CinemachineCameraTarget.transform
                    : (localObj.transform.Find("PlayerCameraRoot") ?? localObj.transform);
                _cinemachineCam.Follow = camTarget;
                _cinemachineCam.LookAt = camTarget;
            }
        }

        if (_canvasGroup != null)
        {
            StartCoroutine(FadeCanvasGroup(_canvasGroup, 1f, 0f, 0.4f, () =>
            {
                _canvasGroup.gameObject.SetActive(false);
            }));
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (!_isSpectating) return;

        // Living players MUST NEVER stay in spectator mode!
        if (!IsLocalPlayerDead())
        {
            Debug.Log("[SpectatorController] Local player is alive — Exiting Spectator Mode immediately.");
            StopSpectating();
            return;
        }

        // If game is paused, freeze spectator controls and HUD updates
        if (PauseManager.IsGamePaused) return;

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
        if (Keyboard.current == null && Mouse.current == null) return;

        // Exit Spectating: [ESC]
        if (Keyboard.current != null && Keyboard.current[exitHotkey].wasPressedThisFrame)
        {
            ExitSpectating();
            return;
        }

        // Cycle Previous: [A], [Left Arrow], [Mouse Left Button]
        bool prevPressed = (Keyboard.current != null && (Keyboard.current.aKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame)) ||
                           (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);

        // Cycle Next: [D], [Right Arrow], [Mouse Right Button]
        bool nextPressed = (Keyboard.current != null && (Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame)) ||
                           (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);

        // Toggle Free Orbit vs Follow Facing: [Space]
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ToggleOrbitMode();
        }

        // Toggle Cursor Lock: [Left Alt]
        if (Keyboard.current != null && Keyboard.current.leftAltKey.wasPressedThisFrame)
        {
            ToggleCursorLock();
        }

        if (prevPressed) CyclePreviousSurvivor();
        else if (nextPressed) CycleNextSurvivor();

        // Mouse Orbit Input
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            if (_freeOrbitMode)
            {
                _yaw += delta.x * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * mouseSensitivity, -35f, 75f);
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

        _currentTargetIndex = (_currentTargetIndex + 1) % _aliveTargets.Count;
        SelectTargetByIndex(_currentTargetIndex);
    }

    public void CyclePreviousSurvivor()
    {
        RefreshAliveTargets();
        if (_aliveTargets.Count == 0) return;

        _currentTargetIndex = (_currentTargetIndex - 1 + _aliveTargets.Count) % _aliveTargets.Count;
        SelectTargetByIndex(_currentTargetIndex);
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
            string pRole = GetPlayerRole(_currentTarget);
            ShowToast($"SPECTATING: {pName}", new Color(0.1f, 0.9f, 0.5f, 1f));

            // Align initial camera angle behind teammate
            _yaw = _currentTarget.transform.eulerAngles.y;
            _pitch = 18f;

            if (snapImmediate && _spectatorAnchor != null)
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
                // No more survivors — stop spectating and return to own death screen
                Debug.Log("[SpectatorController] Last survivor has fallen. Returning to death screen.");
                if (DeathUI.Instance != null)
                {
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
            var monsterHealths = new HashSet<TargetHealth>();

            var controllers = FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
            foreach (var mc in controllers)
            {
                if (mc == null || mc.gameObject == null) continue;
                if (mc.TryGetComponent<TargetHealth>(out var mth))
                {
                    if (mth.isCorpse.Value || mth.CurrentHealth <= 0) continue;
                    monsterHealths.Add(mth);
                }
            }

            var ais = FindObjectsByType<MonsterAI>(FindObjectsSortMode.None);
            foreach (var ai in ais)
            {
                if (ai == null || ai.gameObject == null) continue;
                if (ai.currentState == MonsterAI.AIState.Dead) continue;
                if (ai.TryGetComponent<TargetHealth>(out var ath))
                {
                    if (ath.isCorpse.Value || ath.CurrentHealth <= 0) continue;
                    monsterHealths.Add(ath);
                }
            }

            _aliveTargets.AddRange(monsterHealths);

            if (_aliveTargets.Count > 0)
            {
                _currentTargetIndex = Mathf.Clamp(_currentTargetIndex, 0, _aliveTargets.Count - 1);
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
        if (_cinemachineCam != null)
        {
            if (_cinemachineCam.Follow != _spectatorAnchor)
            {
                _cinemachineCam.Follow = _spectatorAnchor;
                _cinemachineCam.LookAt = _spectatorAnchor;
            }
        }
        else if (Camera.main != null)
        {
            // Direct camera positioning fallback if Cinemachine is not bound
            Vector3 backwardOffset = _spectatorAnchor.rotation * new Vector3(0f, 0.4f, -_currentDistance);
            Camera.main.transform.position = _spectatorAnchor.position + backwardOffset;
            Camera.main.transform.LookAt(_spectatorAnchor.position + Vector3.up * 0.25f);
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

        return target.transform.position + Vector3.up * 1.4f;
    }

    private void ResolveCamera()
    {
        if (_cinemachineCam == null)
        {
            // 1. Check local player's OWN NetworkPlayer camera strictly
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                var netPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<NetworkPlayer>();
                if (netPlayer != null && netPlayer.virtualCamera != null)
                {
                    _cinemachineCam = netPlayer.virtualCamera;
                }
            }

            // 2. If null, create/bind a dedicated spectator camera on the spectator anchor (never hijack teammate cameras!)
            if (_cinemachineCam == null && _spectatorAnchor != null)
            {
                _cinemachineCam = _spectatorAnchor.GetComponent<CinemachineVirtualCameraBase>();
                if (_cinemachineCam == null)
                {
                    _cinemachineCam = _spectatorAnchor.gameObject.AddComponent<CinemachineCamera>();
                }
            }
        }
    }

    private void BindCinemachineToAnchor()
    {
        ResolveCamera();
        if (_cinemachineCam != null && _spectatorAnchor != null)
        {
            _cinemachineCam.gameObject.SetActive(true);
            _cinemachineCam.enabled = true;
            _cinemachineCam.Priority = 99999;
            _cinemachineCam.Follow = _spectatorAnchor;
            _cinemachineCam.LookAt = _spectatorAnchor;
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

        if (cleanCountText != null)
        {
            cleanCountText.text = aliveCount.ToString();
        }

        if (totalConnectedPlayersText != null)
        {
            totalConnectedPlayersText.text = totalConnected.ToString();
        }

        if (_survivorsCountText != null)
        {
            int totalSurvivors = Mathf.Max(1, totalConnected - 1);
            _survivorsCountText.text = $"SURVIVORS: <b>{aliveCount}</b> / {Mathf.Max(aliveCount, totalSurvivors)}";
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

    private string GetPlayerName(TargetHealth th)
    {
        if (th == null) return modeType == SpectatorModeType.Monsters ? "Monster" : "Investigator";

        if (th.TryGetComponent<MonsterController>(out var mc))
        {
            return mc.gameObject.name.Replace("(Clone)", "").Trim();
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

    private string GetPlayerRole(TargetHealth th)
    {
        if (th == null) return "Investigator";

        string n = th.gameObject.name.ToLower();
        if (n.Contains("miner")) return "Miner";
        if (n.Contains("adventurer")) return "Adventurer";
        if (n.Contains("doctor") || n.Contains("medic")) return "Medic";
        if (n.Contains("detective")) return "Detective";
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
