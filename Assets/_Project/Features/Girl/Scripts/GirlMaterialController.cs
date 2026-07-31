using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GirlMaterialController : MonoBehaviour
{
    private Renderer[] _allRenderers;
    private MaterialPropertyBlock _propBlock;
    private Dictionary<Renderer, Color> _origColors = new Dictionary<Renderer, Color>();
    private Coroutine _fadeJob;
    private float _currentAlpha = 1f;

    void Awake()
    {
        _allRenderers = GetComponentsInChildren<Renderer>(true);
        _propBlock = new MaterialPropertyBlock();

        foreach (Renderer r in _allRenderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            
            // Read the original color without cloning the material to prevent FBX bug
            if (r.sharedMaterial.HasProperty("_BaseColor"))
                _origColors[r] = r.sharedMaterial.GetColor("_BaseColor");
            else if (r.sharedMaterial.HasProperty("_Color"))
                _origColors[r] = r.sharedMaterial.GetColor("_Color");
            else
                _origColors[r] = Color.white;
        }
    }

    public void SetAlphaInstant(float alpha)
    {
        if (_fadeJob != null) StopCoroutine(_fadeJob);
        _currentAlpha = alpha;
        UpdateMaterials(alpha);
    }

    public void RequestAlpha(float target, float duration)
    {
        if (_fadeJob != null) StopCoroutine(_fadeJob);
        _fadeJob = StartCoroutine(FadeRoutine(target, duration));
    }

    private IEnumerator FadeRoutine(float target, float duration)
    {
        float start = _currentAlpha;
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _currentAlpha = Mathf.Lerp(start, target, elapsed / duration);
            UpdateMaterials(_currentAlpha);
            yield return null;
        }
        UpdateMaterials(target);
    }

    private void UpdateMaterials(float alpha)
    {
        bool isVisible = alpha > 0.01f;
        foreach (Renderer r in _allRenderers)
        {
            if (r != null && r.enabled != isVisible)
                r.enabled = isVisible;
        }

        if (!isVisible) return; 

        float stealthLerp = Mathf.Clamp01((1f - alpha) * 1.5f); 
        Color shadowBase = new Color(0.0f, 0.0f, 0.0f, 1f); 
        Color glowingShadow = new Color(0.4f, 0.0f, 0.8f, 1f) * 1.5f; 

        foreach (Renderer r in _allRenderers)
        {
            if (r == null || !_origColors.ContainsKey(r)) continue;

            r.GetPropertyBlock(_propBlock);
            
            Color origBase = _origColors[r];
            Color finalBase = Color.Lerp(origBase, shadowBase, stealthLerp);

            _propBlock.SetColor("_BaseColor", finalBase);
            _propBlock.SetColor("_Color", finalBase);

            // Animate Emission intensity mathematically
            Color finalEmission = Color.Lerp(Color.black, glowingShadow, stealthLerp);
            _propBlock.SetColor("_EmissionColor", finalEmission);

            r.SetPropertyBlock(_propBlock);
        }
    }

    public void ToggleOutline(bool show)
    {
        foreach (Renderer r in _allRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_propBlock);
            
            _propBlock.SetFloat("_OutlineWidth", show ? 0.05f : 0f);
            
            // Note: Most URP Lit shaders don't have these properties by default, 
            // but we'll set them in the block just in case yours does.
            Color c = Color.white;
            c.a = show ? 1f : 0f;
            _propBlock.SetColor("_OutlineColor", c);
            
            r.SetPropertyBlock(_propBlock);
        }
    }


}