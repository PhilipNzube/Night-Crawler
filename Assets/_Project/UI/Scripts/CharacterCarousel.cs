using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Manages a 3D circular character carousel for the character select screen.
///
/// Characters are arranged in a ring around a center pivot. The front-most slot
/// is automatically focused (larger scale, highlight ring visible, side panel updated).
/// Scroll left/right via UI buttons or click any unfocused character to focus it.
///
/// Each model plays idle + gesture animations via CharacterAnimationController.
/// Add gesture state names to the idleGestureSteps list on each character's
/// CharacterAnimationController in the Inspector — same workflow as the girl's dance steps.
///
/// All fields are Inspector-exposed — drag and drop, no auto-wiring.
///
/// Setup:
///   1. Create an empty "CarouselPivot" in your character select world.
///   2. Add this script to it.
///   3. Drag your character prefabs into characterPrefabs[] in desired order.
///   4. Drag this component into CharacterSelectUI.carousel.
///   5. Wire scrollButtonLeft / scrollButtonRight to your UI arrow buttons.
/// </summary>
public class CharacterCarousel : MonoBehaviour
{
    // =========================================================================
    //  Inspector — Characters
    // =========================================================================

    [Header("Character Prefabs")]
    [Tooltip("Ordered list of character prefabs to display in the carousel. " +
             "Each prefab should have a CharacterAnimationController.")]
    public List<GameObject> characterPrefabs = new List<GameObject>();

    // =========================================================================
    //  Inspector — Layout
    // =========================================================================

    [Header("Ring Layout")]
    [Tooltip("World-space distance of each character from the carousel center.")]
    public float carouselRadius = 2.5f;

    [Tooltip("Which slot index faces the camera at startup (usually 0).")]
    public int focusSlotIndex = 0;

    // =========================================================================
    //  Inspector — Focus Visuals
    // =========================================================================

    [Header("Focus Visuals")]
    [Tooltip("Scale applied to the focused (center) character model.")]
    public float focusScale = 1.15f;

    [Tooltip("Scale applied to all non-focused character models.")]
    public float unfocusedScale = 0.85f;

    [Tooltip("Optional GameObject (ring glow, ground highlight, spotlight) placed under the focused model.")]
    public GameObject focusHighlightObject;

    [Tooltip("Duration in seconds to animate the scale change when focus changes.")]
    public float scaleAnimDuration = 0.25f;

    // =========================================================================
    //  Inspector — Scroll Animation
    // =========================================================================

    [Header("Scroll Animation")]
    [Tooltip("Duration in seconds to animate one carousel rotation step.")]
    public float rotateDuration = 0.35f;

    [Tooltip("Ease curve for the rotation. Ease-in-out gives a snappy, polished feel.")]
    public AnimationCurve rotateCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // =========================================================================
    //  Inspector — UI Buttons
    // =========================================================================

    [Header("Scroll Buttons (optional)")]
    [Tooltip("UI Button to scroll to the previous character (left).")]
    public Button scrollButtonLeft;

    [Tooltip("UI Button to scroll to the next character (right).")]
    public Button scrollButtonRight;

    // =========================================================================
    //  Inspector — Notification
    // =========================================================================

    [Header("CharacterSelectUI")]
    [Tooltip("Drag the CharacterSelectUI component here. " +
             "The carousel will call SelectProfession(index) whenever focus changes.")]
    public CharacterSelectUI characterSelectUI;

    // =========================================================================
    //  Private State
    // =========================================================================

    private readonly List<GameObject>                   _instances = new List<GameObject>();
    private readonly List<CharacterAnimationController> _animCtrls = new List<CharacterAnimationController>();
    private Coroutine _rotateCoroutine;
    private bool      _isRotating;

    // Current ring angle offset (used during rotation animation)
    private float _ringAngleOffset = 0f;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (scrollButtonLeft  != null) scrollButtonLeft.onClick.AddListener(ScrollLeft);
        if (scrollButtonRight != null) scrollButtonRight.onClick.AddListener(ScrollRight);
    }

    void OnEnable()
    {
        BuildCarousel();
    }

    void OnDisable()
    {
        ClearCarousel();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>Returns the currently focused character index.</summary>
    public int GetFocusedIndex() => focusSlotIndex;

    /// <summary>Scrolls to the next character (right).</summary>
    public void ScrollRight()
    {
        if (_isRotating || _instances.Count < 2) return;
        int next = (focusSlotIndex + 1) % characterPrefabs.Count;
        StartScrollTo(next);
    }

    /// <summary>Scrolls to the previous character (left).</summary>
    public void ScrollLeft()
    {
        if (_isRotating || _instances.Count < 2) return;
        int prev = (focusSlotIndex - 1 + characterPrefabs.Count) % characterPrefabs.Count;
        StartScrollTo(prev);
    }

    /// <summary>
    /// Focuses a character by index, animating the ring to bring it to front.
    /// Called by CarouselClickProxy when a model is tapped.
    /// </summary>
    public void ScrollToIndex(int targetIndex)
    {
        if (_isRotating) return;
        if (targetIndex < 0 || targetIndex >= characterPrefabs.Count) return;

        if (targetIndex == focusSlotIndex)
        {
            NotifySelectionChanged(targetIndex);
            return;
        }

        StartScrollTo(targetIndex);
    }

    // =========================================================================
    //  Build / Clear
    // =========================================================================

    private void BuildCarousel()
    {
        ClearCarousel();

        ResolvePrefabsIfEmpty();

        int count = characterPrefabs.Count;
        if (count == 0) return;

        _ringAngleOffset = 0f;

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = characterPrefabs[i];
            if (prefab == null)
            {
                _instances.Add(null);
                _animCtrls.Add(null);
                continue;
            }

            Vector3    slotPos = SlotLocalPosition(i, count, 0f);
            Quaternion slotRot = SlotLocalRotation(slotPos);
            GameObject inst    = Instantiate(prefab, transform.TransformPoint(slotPos), transform.rotation * slotRot, transform);
            inst.transform.localPosition = slotPos;
            inst.transform.localRotation = slotRot;
            _instances.Add(inst);

            // Disable everything except CharacterAnimationController
            foreach (MonoBehaviour mb in inst.GetComponentsInChildren<MonoBehaviour>())
            {
                if (mb is CharacterAnimationController) continue;
                mb.enabled = false;
            }

            // Attach animation controller
            CharacterAnimationController ctrl = inst.GetComponent<CharacterAnimationController>();
            if (ctrl == null)
                ctrl = inst.AddComponent<CharacterAnimationController>();
            _animCtrls.Add(ctrl);

            // Scale
            inst.transform.localScale = Vector3.one * (i == focusSlotIndex ? focusScale : unfocusedScale);

            // Start idle gesture loop
            ctrl.StartNaturalGestureLoop();

            // Click handler
            AddClickHandler(inst, i);
        }

        RefreshHighlight();
        NotifySelectionChanged(focusSlotIndex);

        // Trigger entrance stance for initial focus character
        if (focusSlotIndex >= 0 && focusSlotIndex < _animCtrls.Count && _animCtrls[focusSlotIndex] != null)
        {
            _animCtrls[focusSlotIndex].PlayCinematicSequence(startGestureLoopAfter: true);
        }
    }

    private void ResolvePrefabsIfEmpty()
    {
        if (characterPrefabs != null && characterPrefabs.Count > 0) return;

        characterPrefabs = new List<GameObject>();

        if (characterSelectUI != null && characterSelectUI.characterDataList != null)
        {
            for (int i = 0; i < characterSelectUI.characterDataList.Count; i++)
            {
                var data = characterSelectUI.characterDataList[i];
                GameObject prefab = (data != null) ? data.characterPrefab : null;
                characterPrefabs.Add(prefab);
            }
        }

        if (characterPrefabs.Count == 0 && CharacterSelectManager.Instance != null &&
            CharacterSelectManager.Instance.availableCharacters != null)
        {
            for (int i = 0; i < CharacterSelectManager.Instance.availableCharacters.Count; i++)
            {
                var data = CharacterSelectManager.Instance.availableCharacters[i];
                GameObject prefab = (data != null) ? data.characterPrefab : null;
                characterPrefabs.Add(prefab);
            }
        }
    }

    private void ClearCarousel()
    {
        if (_rotateCoroutine != null) { StopCoroutine(_rotateCoroutine); _rotateCoroutine = null; }
        _isRotating = false;

        foreach (GameObject inst in _instances)
            if (inst != null) Destroy(inst);
        _instances.Clear();
        _animCtrls.Clear();
    }

    // =========================================================================
    //  Scroll Animation
    // =========================================================================

    private void StartScrollTo(int targetIndex)
    {
        if (_rotateCoroutine != null) StopCoroutine(_rotateCoroutine);
        _rotateCoroutine = StartCoroutine(AnimateScrollTo(targetIndex));
    }

    private IEnumerator AnimateScrollTo(int targetIndex)
    {
        _isRotating = true;

        int count = characterPrefabs.Count;
        float anglePerSlot = 360f / count;

        // Find the shortest rotation direction
        int diff = targetIndex - focusSlotIndex;
        if (diff >  count / 2) diff -= count;
        if (diff < -count / 2) diff += count;

        float startOffset = _ringAngleOffset;
        float endOffset   = _ringAngleOffset + diff * anglePerSlot;

        float elapsed = 0f;
        while (elapsed < rotateDuration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / rotateDuration);
            float eased = rotateCurve.Evaluate(t);
            _ringAngleOffset = Mathf.Lerp(startOffset, endOffset, eased);

            RepositionAll(count);
            yield return null;
        }

        // Snap clean
        _ringAngleOffset = 0f;
        focusSlotIndex   = targetIndex;
        RepositionAll(count);
        RefreshScales();
        RefreshHighlight();
        NotifySelectionChanged(focusSlotIndex);

        // Trigger focus entrance stance for the newly selected character that arrived in front
        if (focusSlotIndex >= 0 && focusSlotIndex < _animCtrls.Count && _animCtrls[focusSlotIndex] != null)
        {
            _animCtrls[focusSlotIndex].PlayCinematicSequence(startGestureLoopAfter: true);
        }

        _isRotating      = false;
        _rotateCoroutine = null;
    }

    // =========================================================================
    //  Layout
    // =========================================================================

    /// <summary>
    /// Returns the local position for slot [i] in a ring of [count],
    /// with an angular offset applied (used during scroll animation).
    /// focusSlotIndex is always at the "front" (positive local Z).
    /// </summary>
    private Vector3 SlotLocalPosition(int slotIndex, int count, float angleOffsetDegrees)
    {
        float baseAngle = ((slotIndex - focusSlotIndex) / (float)count) * 360f;
        float angle     = (baseAngle + angleOffsetDegrees) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(angle) * carouselRadius, 0f, Mathf.Cos(angle) * carouselRadius);
    }

    /// <summary>Models face inward (toward the carousel center) then rotated 180° to face the camera.</summary>
    private Quaternion SlotLocalRotation(Vector3 localPos)
    {
        if (localPos == Vector3.zero) return Quaternion.identity;
        return Quaternion.LookRotation(-localPos.normalized, Vector3.up);
    }

    private void RepositionAll(int count)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] == null) continue;
            Vector3    pos = SlotLocalPosition(i, count, _ringAngleOffset);
            Quaternion rot = SlotLocalRotation(pos);
            _instances[i].transform.localPosition = pos;
            _instances[i].transform.localRotation = rot;
        }
    }

    private void RefreshScales()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] == null) continue;
            float target = (i == focusSlotIndex) ? focusScale : unfocusedScale;
            StartCoroutine(AnimateScale(_instances[i].transform, target));
        }
    }

    private IEnumerator AnimateScale(Transform t, float targetScale)
    {
        if (t == null) yield break;
        Vector3 start   = t.localScale;
        Vector3 end     = Vector3.one * targetScale;
        float   elapsed = 0f;
        while (elapsed < scaleAnimDuration)
        {
            elapsed     += Time.deltaTime;
            t.localScale = Vector3.Lerp(start, end,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / scaleAnimDuration)));
            yield return null;
        }
        t.localScale = end;
    }

    private void RefreshHighlight()
    {
        if (focusHighlightObject == null) return;
        if (_instances.Count == 0 || focusSlotIndex >= _instances.Count) return;

        GameObject focused = _instances[focusSlotIndex];
        if (focused == null) { focusHighlightObject.SetActive(false); return; }

        focusHighlightObject.transform.position = focused.transform.position;
        focusHighlightObject.SetActive(true);
    }

    // =========================================================================
    //  Click Handler — attaches proxy to ALL colliders on model instance
    // =========================================================================

    private void AddClickHandler(GameObject inst, int index)
    {
        if (inst == null) return;

        Collider[] colliders = inst.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            CapsuleCollider cap = inst.AddComponent<CapsuleCollider>();
            cap.height = 1.8f;
            cap.radius = 0.4f;
            cap.center = new Vector3(0f, 0.9f, 0f);
            colliders = new Collider[] { cap };
        }

        foreach (Collider col in colliders)
        {
            if (col == null) continue;
            CarouselClickProxy proxy = col.gameObject.GetComponent<CarouselClickProxy>();
            if (proxy == null)
                proxy = col.gameObject.AddComponent<CarouselClickProxy>();
            proxy.Init(this, index);
        }
    }

    // =========================================================================
    //  Selection Notification
    // =========================================================================

    private void NotifySelectionChanged(int index)
    {
        if (characterSelectUI != null)
            characterSelectUI.SelectProfession(index);
    }
}

// =============================================================================
//  CarouselClickProxy — sits on each collider of a carousel model instance
// =============================================================================

/// <summary>
/// Lightweight proxy added to every collider on a carousel model. Forwards OnMouseDown clicks
/// to CharacterCarousel.ScrollToIndex so players can tap anywhere on any character model to focus it.
/// </summary>
public class CarouselClickProxy : MonoBehaviour
{
    private CharacterCarousel _carousel;
    private int               _index;

    public void Init(CharacterCarousel carousel, int index)
    {
        _carousel = carousel;
        _index    = index;
    }

    void OnMouseDown()
    {
        if (_carousel != null)
            _carousel.ScrollToIndex(_index);
    }
}
