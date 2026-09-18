using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.Rendering;

public class GirlStateController : MonoBehaviour
{
    public enum GirlState { Normal, Invisible }
    public GirlState currentState = GirlState.Normal;

    [Header("References")]
    public EntityStats stats; // Drag your GirlStats ScriptableObject here
    public GirlMovement girlMovement; 
    public Renderer[] renderers;
    public Volume invisibilityVolume;

    [Header("UI References")]
    public Image invisCooldownBar;
    public Image fearBar;

    [Header("Michsky Heat / Dark UI")]
    [Tooltip("Optional: Michsky ProgressBar for Invisibility Cooldown.")]
    public Michsky.UI.Heat.ProgressBar heatInvisCooldownBar;

    [Tooltip("Optional: Michsky ProgressBar for Fear Bar.")]
    public Michsky.UI.Heat.ProgressBar heatFearBar;

    private float fearValue = 0f;
    private bool canUseInvis = true;

    void Start()
    {
        if (!girlMovement) girlMovement = GetComponent<GirlMovement>();
        
        // Walk speeds are now natively handled by the ScriptableObject architecture in GirlMovement!
        
        ApplyState(GirlState.Normal);
    }

    void Update()
    {
        if (PauseManager.IsGamePaused) return;

        if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
        {
            TryActivateInvisibility();
        }
        UpdateFear();
    }

    void UpdateFear()
    {
        if (!stats) return;

        float rate = stats.fearIncreaseRate;
        if (currentState == GirlState.Invisible) rate *= stats.invisFearMultiplier;

        fearValue += rate * Time.deltaTime;
        fearValue = Mathf.Clamp01(fearValue);

        if (fearBar) fearBar.fillAmount = fearValue;
        NightCrawler.UI.MichskyUIBridge.SetProgress(heatFearBar, fearValue);
    }

    void TryActivateInvisibility()
    {
        if (!canUseInvis || currentState == GirlState.Invisible || !stats) return;
        StartCoroutine(InvisibilityRoutine());
    }

    IEnumerator InvisibilityRoutine()
    {
        canUseInvis = false;
        SetState(GirlState.Invisible);
        
        if (invisCooldownBar) invisCooldownBar.fillAmount = 0f;
        NightCrawler.UI.MichskyUIBridge.SetProgress(heatInvisCooldownBar, 0f);

        yield return new WaitForSeconds(stats.invisDuration);

        SetState(GirlState.Normal);

        float timer = 0f;
        while (timer < stats.invisCooldown)
        {
            timer += Time.deltaTime;
            float prog = timer / stats.invisCooldown;
            if (invisCooldownBar) invisCooldownBar.fillAmount = prog;
            NightCrawler.UI.MichskyUIBridge.SetProgress(heatInvisCooldownBar, prog);
            yield return null;
        }

        canUseInvis = true;
        NightCrawler.UI.MichskyUIBridge.SetProgress(heatInvisCooldownBar, 1f);
    }

    void SetState(GirlState newState)
    {
        currentState = newState;
        ApplyState(newState);
    }

    void ApplyState(GirlState state)
    {
        bool isInvisible = (state == GirlState.Invisible);
        
        foreach (Renderer r in renderers)
        {
            if (r != null) r.enabled = !isInvisible;
        }

        if (invisibilityVolume) invisibilityVolume.weight = isInvisible ? 1f : 0f;
    }
}