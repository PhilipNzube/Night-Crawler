using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SOLID — SRP: Controls dissolve and materialization transitions using Shader Graph dissolve materials.
/// Targets the "_Dissolve" float property (0 = fully visible/solid, 1 = fully dissolved/invisible).
/// Works with Shader Graphs_Dissolve_Dissolve_Metallic.mat and compatible shaders.
/// </summary>
public class ModelDissolveController : MonoBehaviour
{
    [Header("Material Settings")]
    [Tooltip("Dissolve material template (e.g. Shader Graphs_Dissolve_Dissolve_Metallic). If assigned, meshes can switch to this during dissolve.")]
    public Material dissolveMaterial;

    [Tooltip("If true, automatically creates material instances on Start to prevent modifying shared project assets.")]
    public bool instantiateMaterials = true;

    [Tooltip("Name of the float property in the shader graph.")]
    public string dissolvePropertyName = "_Dissolve";

    [Header("Default Timings")]
    [Range(0.1f, 10f)]
    public float defaultDuration = 1.5f;

    // Internal tracking
    private readonly List<Material> _activeMaterials = new List<Material>();
    private readonly List<Renderer> _renderers = new List<Renderer>();
    private Coroutine _currentTransition;
    private int _dissolvePropId;
    private float _currentDissolve = 0f;

    public float CurrentDissolve => _currentDissolve;
    public bool IsTransitioning => _currentTransition != null;

    private void Awake()
    {
        _dissolvePropId = Shader.PropertyToID(dissolvePropertyName);
        CollectRenderers();
    }

    /// <summary>
    /// Collects all child MeshRenderers and SkinnedMeshRenderers and prepares material instances.
    /// </summary>
    public void CollectRenderers()
    {
        _renderers.Clear();
        _activeMaterials.Clear();

        var rends = GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            if (r == null) continue;
            _renderers.Add(r);

            var mats = instantiateMaterials ? r.materials : r.sharedMaterials;
            foreach (var m in mats)
            {
                if (m != null && m.HasProperty(_dissolvePropId))
                {
                    _activeMaterials.Add(m);
                }
            }
        }
    }

    /// <summary>
    /// Sets dissolve amount instantly (0 = Solid, 1 = Dissolved).
    /// </summary>
    public void SetDissolveImmediate(float value)
    {
        if (_currentTransition != null)
        {
            StopCoroutine(_currentTransition);
            _currentTransition = null;
        }

        _currentDissolve = Mathf.Clamp01(value);
        ApplyDissolveToMaterials(_currentDissolve);
    }

    /// <summary>
    /// Transitions model from current dissolve value to fully dissolved (1.0).
    /// </summary>
    public void Dissolve(float duration = -1f, Action onComplete = null)
    {
        float targetTime = duration > 0f ? duration : defaultDuration;
        StartTransition(1f, targetTime, onComplete);
    }

    /// <summary>
    /// Transitions model from current dissolve value to fully materialized (0.0).
    /// </summary>
    public void Materialize(float duration = -1f, Action onComplete = null)
    {
        float targetTime = duration > 0f ? duration : defaultDuration;
        StartTransition(0f, targetTime, onComplete);
    }

    /// <summary>
    /// Coroutine transition between current dissolve and target value.
    /// </summary>
    public void StartTransition(float targetValue, float duration, Action onComplete = null)
    {
        if (_currentTransition != null)
        {
            StopCoroutine(_currentTransition);
        }

        _currentTransition = StartCoroutine(TransitionRoutine(targetValue, duration, onComplete));
    }

    private IEnumerator TransitionRoutine(float targetValue, float duration, Action onComplete)
    {
        float startValue = _currentDissolve;
        float elapsed = 0f;

        // Ensure renderers are enabled during transition
        ToggleRenderers(true);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _currentDissolve = Mathf.Lerp(startValue, targetValue, t);
            ApplyDissolveToMaterials(_currentDissolve);
            yield return null;
        }

        _currentDissolve = targetValue;
        ApplyDissolveToMaterials(_currentDissolve);

        // If completely dissolved, renderers can be disabled to save draw calls
        if (Mathf.Approximately(_currentDissolve, 1f))
        {
            ToggleRenderers(false);
        }

        _currentTransition = null;
        onComplete?.Invoke();
    }

    private void ApplyDissolveToMaterials(float value)
    {
        for (int i = 0; i < _activeMaterials.Count; i++)
        {
            if (_activeMaterials[i] != null)
            {
                _activeMaterials[i].SetFloat(_dissolvePropId, value);
            }
        }
    }

    private void ToggleRenderers(bool enable)
    {
        for (int i = 0; i < _renderers.Count; i++)
        {
            if (_renderers[i] != null)
            {
                _renderers[i].enabled = enable;
            }
        }
    }
}
