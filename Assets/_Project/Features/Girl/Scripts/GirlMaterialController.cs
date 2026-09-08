using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

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

    private void ApplyVisualStateImmediate(bool visible)
    {
        if (TryGetComponent<GirlMovement>(out var movement))
        {
            movement.UpdateCollisionState(visible);
        }

        if (visible)
        {
            // Full real girl model visible to everyone
            RestoreOriginalMaterials();
            ToggleRenderers(true);
            if (_dissolveController != null) _dissolveController.SetDissolveImmediate(0f);
        }
        else
        {
            // Spirit / invisible mode
            if (IsOwner)
            {
                ApplyInvisibleVFXMaterial();
                ToggleRenderers(true);
            }
            else
            {
                ToggleRenderers(false);
            }
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
            // 1. Switch to dissolve material and start from dissolved (1.0)
            ApplyDissolveMaterial();
            ToggleRenderers(true);
            if (_dissolveController != null)
            {
                _dissolveController.CollectRenderers();
                _dissolveController.SetDissolveImmediate(1.0f);
                _dissolveController.Materialize(dissolveDuration);
            }

            yield return new WaitForSeconds(dissolveDuration);

            // 2. Dissolve complete -> swap to real girl original materials!
            RestoreOriginalMaterials();
            ToggleRenderers(true);
        }
        else
        {
            // Dissolve back out
            ApplyDissolveMaterial();
            if (_dissolveController != null)
            {
                _dissolveController.CollectRenderers();
                _dissolveController.SetDissolveImmediate(0f);
                _dissolveController.Dissolve(dissolveDuration);
            }

            yield return new WaitForSeconds(dissolveDuration);

            if (IsOwner)
            {
                ApplyInvisibleVFXMaterial();
                ToggleRenderers(true);
            }
            else
            {
                ToggleRenderers(false);
            }
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