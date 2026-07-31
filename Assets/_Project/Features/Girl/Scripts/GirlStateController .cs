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

        yield return new WaitForSeconds(stats.invisDuration);

        SetState(GirlState.Normal);

        float timer = 0f;
        while (timer < stats.invisCooldown)
        {
            timer += Time.deltaTime;
            if (invisCooldownBar) invisCooldownBar.fillAmount = timer / stats.invisCooldown;
            yield return null;
        }

        canUseInvis = true;
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