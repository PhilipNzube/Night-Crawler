using UnityEngine;
using Unity.Netcode;

public class PredatorAuraNet : MonoBehaviour
{
    [Header("Settings")]
    public float auraDistance = 35f;
    public Color auraColor = Color.red;
    
    private Renderer[] _renderers;
    private GirlStealth _localDemonStealth;
    private bool _isCurrentlyGlowing;

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
    }

    void Update()
    {
        // 1. Find the local Demon player (only the Demon sees this aura!)
        if (_localDemonStealth == null)
        {
            FindLocalDemon();
            return;
        }

        // 2. Check if we should be glowing
        bool shouldGlow = _localDemonStealth.IsStealthActive.Value && 
                         Vector3.Distance(transform.position, _localDemonStealth.transform.position) < auraDistance;

        if (shouldGlow != _isCurrentlyGlowing)
        {
            ToggleGlow(shouldGlow);
        }
    }

    private void FindLocalDemon()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayer != null)
        {
            _localDemonStealth = localPlayer.GetComponent<GirlStealth>();
        }
    }

    private void ToggleGlow(bool active)
    {
        _isCurrentlyGlowing = active;

        foreach (var r in _renderers)
        {
            // PRO TIP: Instead of changing the whole material, we'll use a simple 
            // Outline or Emissive pulse if the shader supports it.
            // For now, we'll use a Red tint to represent the "Predator Heat Signature".
            if (active)
            {
                if (r.material.HasProperty("_EmissionColor"))
                {
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", auraColor * 2f);
                }
                else if (r.material.HasProperty("_BaseColor"))
                {
                    r.material.SetColor("_BaseColor", Color.Lerp(Color.white, auraColor, 0.5f));
                }
            }
            else
            {
                // Reset to normal
                if (r.material.HasProperty("_EmissionColor"))
                {
                    r.material.SetColor("_EmissionColor", Color.black);
                }
                else if (r.material.HasProperty("_BaseColor"))
                {
                    r.material.SetColor("_BaseColor", Color.white);
                }
            }
        }
    }
}
