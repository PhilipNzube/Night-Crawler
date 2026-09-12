using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Controls the Girl's spectral invisibility, distortion effects, and dissolve manifestation.
/// - When in Spirit Form (Default):
///     - Owner (Girl player): Sees character rendered with M_VFX_Invisible_03 (distortion / invisible cloak).
///     - Other players: Renderers disabled / completely invisible.
/// - When Manifested (Visible):
///     - ModelDissolveController plays dissolve materialization animation.
///     - Switches to full real girl model materials visible to all players.
/// </summary>
public class GirlMaterialController : NetworkBehaviour
{
    [Header("Materials")]
    [Tooltip("Distortion material from Invisible VFX package (M_VFX_Invisible_03.mat).")]
    public Material invisibleVFXMaterial;

    [Tooltip("Dissolve material from ShaderGraph_Dissolve package.")]
    public Material dissolveMaterial;

    [Header("Timing")]
    public float dissolveDuration = 1.2f;

    [Header("Network State")]
    public NetworkVariable<bool> isManifested = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Renderer[] _allRenderers;
    private Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
    private ModelDissolveController _dissolveController;
    private Coroutine _manifestRoutine;

    private void Awake()
    {
        _allRenderers = GetComponentsInChildren<Renderer>(true);
        _dissolveController = GetComponent<ModelDissolveController>();
        if (_dissolveController == null)
        {
            _dissolveController = gameObject.AddComponent<ModelDissolveController>();
            _dissolveController.dissolveMaterial = dissolveMaterial;
        }

        // Cache original character materials
        foreach (Renderer r in _allRenderers)
        {
            if (r == null) continue;
            _originalMaterials[r] = r.sharedMaterials;
        }
    }

    public override void OnNetworkSpawn()
    {
        isManifested.OnValueChanged += HandleManifestationChanged;
        ApplyVisualState(isManifested.Value, immediate: true);
    }

    public override void OnNetworkDespawn()
    {
        isManifested.OnValueChanged -= HandleManifestationChanged;
    }

    private void HandleManifestationChanged(bool previous, bool current)
    {
        ApplyVisualState(current, immediate: false);
    }

    /// <summary>
    /// Toggles manifestation on/off across the network (callable by Owner or Server).
    /// </summary>
    public void SetManifested(bool visible)
    {
        if (IsServer)
        {
            isManifested.Value = visible;
        }
        else
        {
            SetManifestedServerRpc(visible);
        }
    }

    [Rpc(SendTo.Server)]
    private void SetManifestedServerRpc(bool visible)
    {
        isManifested.Value = visible;
    }

    private CharacterController _characterController;
    private Coroutine _fadeJob;
    private float _currentAlpha = 1f;
    private bool _isDissolvingActive = false;

    void Update()
    {
        // Owner controls: Press [T] to toggle Manifestation (Visible to all) vs Spirit Form (Invisible)
        if (IsOwner)
        {
            if (PauseManager.IsGamePaused || _isCurrentlyPossessing) return;

            // Do NOT trigger if Deal UI modal is open or if any text input field is focused!
            bool isDealOpen = GirlDealUI.Instance != null && GirlDealUI.Instance.IsOpen;
            bool isInputFocused = GirlDealUI.IsAnyInputFocused();

            if (!isDealOpen && !isInputFocused)
            {
                bool tPressed = (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
                             || Input.GetKeyDown(KeyCode.T);

                if (tPressed)
                {
                    bool newState = !isManifested.Value;
                    SetManifested(newState);

                    string statusMsg = newState
                        ? "[MANIFESTATION] You are now VISIBLE to all players!"
                        : "[SPIRIT FORM] You are INVISIBLE to investigators.";

                    if (NotificationManager.Instance != null)
                    {
                        NotificationManager.Instance.ShowNotification(statusMsg, 2.5f);
                    }
                    Debug.Log($"[GirlMaterialController] [T] toggled manifestation -> {newState}");
                }
            }
        }

        // Periodic safeguard running on all clients (host and remotes) to ensure
        // newly spawned or connected players ignore collision with spirit form
        if (Time.frameCount % 30 == 0)
        {
            ApplyPassThrough(!isManifested.Value);
        }
    }

    public void ApplyPassThrough(bool enablePassThrough)
    {
        if (_characterController == null) _characterController = GetComponent<CharacterController>();
        if (_characterController == null) return;

        var allPlayers = FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
        foreach (var otherCc in allPlayers)
        {
            if (otherCc != _characterController)
            {
                Physics.IgnoreCollision(_characterController, otherCc, enablePassThrough);
            }
        }
    }

    public void SetAlphaInstant(float alpha)
    {
        if (_fadeJob != null) StopCoroutine(_fadeJob);
        _currentAlpha = alpha;

        if (!IsOwner || (_isCurrentlyPossessing && !isManifested.Value))
        {
            // Remote players (or owner while possessing in spirit mode) only see renderers if manifested
            ToggleRenderers(isManifested.Value && alpha >= 0.05f);
        }
        else
        {
            ToggleRenderers(true);
        }
    }

    public void RequestAlpha(float target, float duration)
    {
        if (_fadeJob != null) StopCoroutine(_fadeJob);
        _fadeJob = StartCoroutine(FadeRoutine(target, duration));
    }

    private IEnumerator FadeRoutine(float target, float duration)
    {
        float start = _currentAlpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _currentAlpha = Mathf.Lerp(start, target, elapsed / duration);
            if (!IsOwner)
            {
                // In spirit mode (!isManifested), remote players NEVER have renderers active
                ToggleRenderers(isManifested.Value && _currentAlpha > 0.05f);
            }
            yield return null;
        }

        _currentAlpha = target;
        if (!IsOwner)
        {
            ToggleRenderers(isManifested.Value && _currentAlpha > 0.05f);
        }
        _fadeJob = null;
    }

    public void ToggleOutline(bool show)
    {
        // Outline highlight hook for local predator perspective
    }

    private void ApplyVisualState(bool visible, bool immediate)
    {
        if (_manifestRoutine != null)
        {
            StopCoroutine(_manifestRoutine);
        }

        if (immediate)
        {
            ApplyVisualStateImmediate(visible);
        }
        else
        {
            _manifestRoutine = StartCoroutine(ManifestTransitionRoutine(visible));
        }
    }

    private bool _isCurrentlyPossessing = false;
    public bool IsCurrentlyPossessing => _isCurrentlyPossessing;

    public void OnPossessionStateChanged(bool possessing)
    {
        _isCurrentlyPossessing = possessing;
        if (_manifestRoutine != null)
        {
            StopCoroutine(_manifestRoutine);
            _manifestRoutine = null;
        }
        ApplyVisualStateImmediate(isManifested.Value);
    }

    public void RefreshVisualState()
    {
        ApplyVisualStateImmediate(isManifested.Value);
    }

    /// <summary>
    /// Explicitly controls spirit mode visibility.
    /// When possessing in spirit mode: showGhostVfx = false (nobody sees anything, including the Girl).
    /// When roaming in spirit mode: showGhostVfx = true (owner sees ghost VFX, remote players see nothing).
    /// </summary>
    public void SetSpiritMode(bool showGhostVfx)
    {
        if (showGhostVfx && IsOwner)
        {
            ApplyInvisibleVFXMaterial();
            ToggleRenderers(true);
        }
        else
        {
            ToggleRenderers(false);
        }
        if (_dissolveController != null) _dissolveController.SetDissolveImmediate(1f);
    }

    private void ApplyVisualStateImmediate(bool visible)
    {
        _isDissolvingActive = false;

        if (TryGetComponent<GirlMovement>(out var movement))
        {
            movement.UpdateCollisionState(visible);
        }

        if (visible)
        {
            // Full real girl model visible to everyone (whether roaming or possessing)
            RestoreOriginalMaterials();
            ToggleRenderers(true);
            if (_dissolveController != null) _dissolveController.SetDissolveImmediate(0f);
        }
        else
        {
            // Spirit / invisible mode:
            // If currently possessing, nobody sees the Girl (showGhostVfx = false).
            // If roaming, the owner sees ghost VFX while remotes see nothing (showGhostVfx = true).
            SetSpiritMode(!_isCurrentlyPossessing);
        }
    }

    private IEnumerator ManifestTransitionRoutine(bool targetVisible)
    {
        if (TryGetComponent<GirlMovement>(out var movement))
        {
            movement.UpdateCollisionState(targetVisible);
        }

        if (targetVisible)
        {
            // Target is solid / materialized (Dissolve = 0.0)
            if (!_isDissolvingActive)
            {
                ApplyDissolveMaterial();
                ToggleRenderers(true);
                if (_dissolveController != null)
                {
                    _dissolveController.CollectRenderers();
                    _dissolveController.SetDissolveImmediate(1.0f);
                }
                _isDissolvingActive = true;
            }
            else
            {
                // Reversing midway! Keep dissolve materials active
                ToggleRenderers(true);
            }

            float currentDissolve = _dissolveController != null ? _dissolveController.CurrentDissolve : 1f;
            float remainingTime = Mathf.Max(0.05f, dissolveDuration * currentDissolve);

            if (_dissolveController != null)
            {
                _dissolveController.StartTransition(0f, remainingTime);
            }

            yield return new WaitForSeconds(remainingTime);

            // Dissolve complete -> swap to real girl original materials!
            RestoreOriginalMaterials();
            ToggleRenderers(true);
            _isDissolvingActive = false;
        }
        else
        {
            // Target is spirit / dissolved (Dissolve = 1.0)
            if (!_isDissolvingActive)
            {
                ApplyDissolveMaterial();
                ToggleRenderers(true);
                if (_dissolveController != null)
                {
                    _dissolveController.CollectRenderers();
                    _dissolveController.SetDissolveImmediate(0f);
                }
                _isDissolvingActive = true;
            }
            else
            {
                // Reversing midway! Keep dissolve materials active
                ToggleRenderers(true);
            }

            float currentDissolve = _dissolveController != null ? _dissolveController.CurrentDissolve : 0f;
            float remainingTime = Mathf.Max(0.05f, dissolveDuration * (1f - currentDissolve));

            if (_dissolveController != null)
            {
                _dissolveController.StartTransition(1f, remainingTime);
            }

            yield return new WaitForSeconds(remainingTime);

            if (_isCurrentlyPossessing)
            {
                ToggleRenderers(false);
            }
            else if (IsOwner)
            {
                ApplyInvisibleVFXMaterial();
                ToggleRenderers(true);
            }
            else
            {
                ToggleRenderers(false);
            }
            _isDissolvingActive = false;
        }

        _manifestRoutine = null;
    }

    private void ApplyInvisibleVFXMaterial()
    {
        if (invisibleVFXMaterial == null) return;

        foreach (var r in _allRenderers)
        {
            if (r == null) continue;
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = invisibleVFXMaterial;
            r.materials = mats;
        }
    }

    private void ApplyDissolveMaterial()
    {
        if (dissolveMaterial == null) return;

        foreach (var r in _allRenderers)
        {
            if (r == null) continue;
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = dissolveMaterial;
            r.materials = mats;
        }
    }

    private void RestoreOriginalMaterials()
    {
        foreach (var kvp in _originalMaterials)
        {
            if (kvp.Key != null && kvp.Value != null)
            {
                kvp.Key.sharedMaterials = kvp.Value;
            }
        }
    }

    private void ToggleRenderers(bool enable)
    {
        foreach (var r in _allRenderers)
        {
            if (r != null) r.enabled = enable;
        }
    }
}