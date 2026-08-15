using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// HOW TO SET UP THE SLOT CARD PREFAB:
///
///   [SlotCard (GameObject)]   ← Button + Image (card background) + this component
///     └─ [Portrait]           ← Image (character icon)
///     └─ [Label]              ← TMP_Text (character name)
///     └─ [SelectGlow]         ← Image (full-rect, glowing ring or colored overlay)  ← optional
///
/// HOW IT WORKS:
///   • Selected card: scales up, glows with the selected color, brighter background.
///   • Hovered card (not selected): subtle scale-up + tinted background.
///   • Normal card: rests at default scale, neutral color.
///   • CharacterSelectUI calls SetSelected(true/false) on each card after selection changes.
///
/// COLORS: Assign in Inspector.
///   normalColor       — unselected resting background tint
///   hoverColor        — tint when pointer is over an unselected card
///   selectedColor     — tint when this card is selected
///   glowColor         — color of the SelectGlow child (optional)
/// </summary>
[RequireComponent(typeof(Button))]
public class CharacterSlotCard : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    // =========================================================================
    //  Inspector
    // =========================================================================

    [Header("─── Visual References")]
    [Tooltip("The background Image of the card. Automatically found on this GameObject if not assigned.")]
    public Image cardBackground;

    [Tooltip("Optional glow overlay child Image. Only visible when selected. Set its color to match selectedGlowColor.")]
    public Image selectGlowImage;

    [Header("─── Card Colors")]
    [Tooltip("Background tint when this card is unselected and not hovered.")]
    public Color normalColor      = new Color(0.15f, 0.15f, 0.20f, 1f);

    [Tooltip("Background tint when the pointer hovers over an unselected card.")]
    public Color hoverColor       = new Color(0.28f, 0.28f, 0.38f, 1f);

    [Tooltip("Background tint when this card is selected.")]
    public Color selectedColor    = new Color(0.20f, 0.16f, 0.06f, 1f);

    [Tooltip("Color used for the glow overlay image when selected.")]
    public Color selectedGlowColor = new Color(1f, 0.82f, 0.18f, 0.65f);

    [Header("─── Scale Animation")]
    [Tooltip("Scale multiplier applied to the card when it is selected.")]
    public float selectedScale = 1.12f;

    [Tooltip("Scale multiplier applied to the card on hover (unselected only).")]
    public float hoverScale    = 1.05f;

    [Tooltip("Duration of all scale and color lerp transitions in seconds.")]
    public float animDuration  = 0.18f;

    // =========================================================================
    //  Private State
    // =========================================================================

    private bool      _isSelected  = false;
    private bool      _isHovered   = false;
    private Coroutine _anim;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (cardBackground == null)
            cardBackground = GetComponent<Image>();

        // Ensure glow starts hidden
        if (selectGlowImage != null)
        {
            Color c = selectedGlowColor;
            c.a = 0f;
            selectGlowImage.color = c;
        }

        // Snap to normal immediately (no animation on first frame)
        if (cardBackground != null)
            cardBackground.color = normalColor;
    }

    // =========================================================================
    //  Public API — called by CharacterSelectUI
    // =========================================================================

    /// <summary>
    /// Call this from CharacterSelectUI after selection changes.
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        RefreshVisualState();
    }

    // =========================================================================
    //  Pointer Events — hover
    // =========================================================================

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovered = true;
        if (!_isSelected) RefreshVisualState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;
        if (!_isSelected) RefreshVisualState();
    }

    // =========================================================================
    //  Visual State Machine
    // =========================================================================

    private void RefreshVisualState()
    {
        float targetScale;
        Color targetBg;
        float targetGlowAlpha;

        if (_isSelected)
        {
            targetScale     = selectedScale;
            targetBg        = selectedColor;
            targetGlowAlpha = selectedGlowColor.a;
        }
        else if (_isHovered)
        {
            targetScale     = hoverScale;
            targetBg        = hoverColor;
            targetGlowAlpha = 0f;
        }
        else
        {
            targetScale     = 1f;
            targetBg        = normalColor;
            targetGlowAlpha = 0f;
        }

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(AnimateToState(targetScale, targetBg, targetGlowAlpha));
    }

    private IEnumerator AnimateToState(float targetScale, Color targetBg, float targetGlowAlpha)
    {
        float   elapsed      = 0f;
        Vector3 startScale   = transform.localScale;
        Color   startBg      = cardBackground != null ? cardBackground.color : targetBg;
        float   startGlowA   = 0f;
        Color   startGlowCol = selectedGlowColor;

        if (selectGlowImage != null)
        {
            startGlowCol = selectGlowImage.color;
            startGlowA   = startGlowCol.a;
        }

        Vector3 endScale = new Vector3(targetScale, targetScale, 1f);

        while (elapsed < animDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t  = Mathf.SmoothStep(0f, 1f, elapsed / animDuration);

            transform.localScale = Vector3.Lerp(startScale, endScale, t);

            if (cardBackground != null)
                cardBackground.color = Color.Lerp(startBg, targetBg, t);

            if (selectGlowImage != null)
            {
                Color gc = selectedGlowColor;
                gc.a = Mathf.Lerp(startGlowA, targetGlowAlpha, t);
                selectGlowImage.color = gc;
            }

            yield return null;
        }

        // Snap to final values
        transform.localScale = endScale;
        if (cardBackground    != null) cardBackground.color    = targetBg;
        if (selectGlowImage   != null)
        {
            Color gc = selectedGlowColor;
            gc.a = targetGlowAlpha;
            selectGlowImage.color = gc;
        }

        _anim = null;
    }
}
