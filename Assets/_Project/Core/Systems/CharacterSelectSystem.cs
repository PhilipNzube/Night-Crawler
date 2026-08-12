using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Naruto/FighterZ-style Character Select System.
///
/// Layout concept:
///   - One large 3D character model stands close to the camera (the FEATURED character).
///   - A horizontal row of slot cards (2D portrait cards) sits to the left/right — use SlimUI buttons.
///   - Left/Right arrow buttons cycle through characters.
///   - The featured model swaps with a smooth animation when selection changes.
///   - Side panel shows name, lore, stats, and abilities.
///
/// This is a full SYSTEM — no game-specific logic. Plug in CharacterDefinitionSO assets.
/// Add a component to your character select scene root and wire fields in Inspector.
/// </summary>
public class CharacterSelectSystem : MonoBehaviour
{
    // =========================================================================
    //  Inspector — Characters
    // =========================================================================

    [Header("Character Roster (ScriptableObjects)")]
    [Tooltip("Ordered list of CharacterDefinitionSO assets. " +
             "Create them via Right-click → Create → Night Crawler → Character Definition.")]
    public List<CharacterDefinitionSO> characters = new();

    // =========================================================================
    //  Inspector — Featured Model (3D Showcase)
    // =========================================================================

    [Header("Featured Model Stage")]
    [Tooltip("The world-space Transform where the featured 3D character model is spawned. " +
             "Position this directly in front of the camera, close and centered.")]
    public Transform featuredModelStage;

    [Tooltip("Duration in seconds to fade/swap the featured model when selection changes.")]
    public float modelSwapDuration = 0.4f;

    // =========================================================================
    //  Inspector — Slot Cards Row
    // =========================================================================

    [Header("Slot Cards (2D Portrait Row)")]
    [Tooltip("Parent Transform under which slot card GameObjects are spawned. " +
             "Use a Horizontal Layout Group for automatic spacing.")]
    public Transform slotCardContainer;

    [Tooltip("Prefab for a single slot card. Must have Image (portrait) and TMP_Text (name). " +
             "Create a simple UI card with a Button component — wire nothing in it.")]
    public GameObject slotCardPrefab;

    [Tooltip("Color applied to the border/frame of the currently selected slot card.")]
    public Color selectedCardHighlightColor = new Color(1f, 0.85f, 0.2f);

    [Tooltip("Color applied to unselected slot cards.")]
    public Color unselectedCardColor = Color.white;

    // =========================================================================
    //  Inspector — Info Panel
    // =========================================================================

    [Header("Info Panel")]
    public TMP_Text characterNameText;
    public TMP_Text characterDescriptionText;
    public TMP_Text characterAbilitiesText;
    public TMP_Text characterLoreText;
    public Image    characterPortraitImage;

    [Header("Stats Bars (Optional)")]
    public Slider speedBar;
    public Slider strengthBar;
    public Slider stealthBar;

    // =========================================================================
    //  Inspector — Navigation Buttons
    // =========================================================================

    [Header("Navigation Buttons")]
    public Button arrowLeft;
    public Button arrowRight;

    // =========================================================================
    //  Inspector — Action Buttons
    // =========================================================================

    [Header("Action Buttons")]
    public Button confirmButton;

    // =========================================================================
    //  Private State
    // =========================================================================

    private int                  _selectedIndex   = 0;
    private GameObject           _featuredInstance;
    private readonly List<Button> _slotCardButtons = new();
    private readonly List<Image>  _slotCardFrames  = new();
    private Coroutine            _swapCoroutine;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    private void Start()
    {
        if (arrowLeft  != null) arrowLeft.onClick.AddListener(SelectPrev);
        if (arrowRight != null) arrowRight.onClick.AddListener(SelectNext);
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);

        BuildSlotCards();
        SelectCharacter(PersistentCharacterSelection.GetSelectedCharacterIndex());
    }

    private void OnDestroy()
    {
        if (_featuredInstance != null)
            Destroy(_featuredInstance);
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    public CharacterDefinitionSO GetSelected() =>
        characters.Count > 0 ? characters[Mathf.Clamp(_selectedIndex, 0, characters.Count - 1)] : null;

    public void SelectNext()
    {
        SelectCharacter((_selectedIndex + 1) % characters.Count);
    }

    public void SelectPrev()
    {
        SelectCharacter((_selectedIndex - 1 + characters.Count) % characters.Count);
    }

    public void SelectCharacter(int index)
    {
        if (characters.Count == 0) return;
        _selectedIndex = Mathf.Clamp(index, 0, characters.Count - 1);

        PersistentCharacterSelection.SetSelectedCharacterIndex(_selectedIndex);

        UpdateInfoPanel();
        UpdateSlotCardHighlights();
        SwapFeaturedModel();
    }

    // =========================================================================
    //  Slot Card Building
    // =========================================================================

    private void BuildSlotCards()
    {
        if (slotCardContainer == null || slotCardPrefab == null) return;

        // Clear existing
        foreach (Transform child in slotCardContainer)
            Destroy(child.gameObject);

        _slotCardButtons.Clear();
        _slotCardFrames.Clear();

        for (int i = 0; i < characters.Count; i++)
        {
            int capturedIndex = i;
            CharacterDefinitionSO charDef = characters[i];

            GameObject card = Instantiate(slotCardPrefab, slotCardContainer);
            card.name = $"SlotCard_{charDef.characterName}";

            // Set portrait
            Image portraitImg = card.GetComponentInChildren<Image>();
            if (portraitImg != null && charDef.portrait != null)
                portraitImg.sprite = charDef.portrait;

            // Set name label
            TMP_Text nameLabel = card.GetComponentInChildren<TMP_Text>();
            if (nameLabel != null)
                nameLabel.text = charDef.characterName;

            // Wire card button click
            Button btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => SelectCharacter(capturedIndex));
                _slotCardButtons.Add(btn);
                _slotCardFrames.Add(btn.GetComponent<Image>());
            }
        }
    }

    private void UpdateSlotCardHighlights()
    {
        for (int i = 0; i < _slotCardFrames.Count; i++)
        {
            if (_slotCardFrames[i] != null)
                _slotCardFrames[i].color = (i == _selectedIndex) ? selectedCardHighlightColor : unselectedCardColor;
        }
    }

    // =========================================================================
    //  Info Panel
    // =========================================================================

    private void UpdateInfoPanel()
    {
        CharacterDefinitionSO d = GetSelected();
        if (d == null) return;

        if (characterNameText        != null) characterNameText.text        = d.characterName;
        if (characterDescriptionText != null) characterDescriptionText.text = d.description;
        if (characterAbilitiesText   != null) characterAbilitiesText.text   = d.abilityDescriptions;
        if (characterLoreText        != null) characterLoreText.text        = d.lore;

        if (characterPortraitImage != null)
        {
            characterPortraitImage.sprite  = d.portrait;
            characterPortraitImage.enabled = (d.portrait != null);
        }

        if (speedBar    != null) speedBar.value    = d.speed    / 10f;
        if (strengthBar != null) strengthBar.value = d.strength / 10f;
        if (stealthBar  != null) stealthBar.value  = d.stealth  / 10f;
    }

    // =========================================================================
    //  Featured 3D Model Swap
    // =========================================================================

    private void SwapFeaturedModel()
    {
        if (_swapCoroutine != null) StopCoroutine(_swapCoroutine);
        _swapCoroutine = StartCoroutine(SwapModelRoutine());
    }

    private IEnumerator SwapModelRoutine()
    {
        CharacterDefinitionSO charDef = GetSelected();

        // Fade out old model
        if (_featuredInstance != null)
        {
            yield return FadeModel(_featuredInstance, fadeIn: false, modelSwapDuration * 0.5f);
            Destroy(_featuredInstance);
            _featuredInstance = null;
        }

        if (charDef?.characterPrefab == null || featuredModelStage == null) yield break;

        // Spawn new model at stage
        _featuredInstance = Instantiate(charDef.characterPrefab,
                                        featuredModelStage.position,
                                        featuredModelStage.rotation,
                                        featuredModelStage);

        // Disable any gameplay logic on preview instance
        foreach (var mb in _featuredInstance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is CharacterAnimationSystem || mb is CharacterAnimationController) continue;
            mb.enabled = false;
        }

        // Ensure animation system
        CharacterAnimationSystem animSys = _featuredInstance.GetComponent<CharacterAnimationSystem>();
        if (animSys == null) animSys = _featuredInstance.AddComponent<CharacterAnimationSystem>();

        // Fade in new model
        yield return FadeModel(_featuredInstance, fadeIn: true, modelSwapDuration * 0.5f);
    }

    private IEnumerator FadeModel(GameObject modelRoot, bool fadeIn, float duration)
    {
        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>();
        float start = fadeIn ? 0f : 1f;
        float end   = fadeIn ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            foreach (var rend in renderers)
            {
                foreach (var mat in rend.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.color;
                        c.a = Mathf.Lerp(start, end, t);
                        mat.color = c;
                    }
                }
            }
            yield return null;
        }
    }

    // =========================================================================
    //  Confirm Selection
    // =========================================================================

    private void OnConfirm()
    {
        CharacterDefinitionSO selected = GetSelected();
        if (selected == null) return;

        int confirmedIndex = _selectedIndex;
        PersistentCharacterSelection.SetSelectedCharacterIndex(confirmedIndex);

        if (NetworkManager.Singleton != null)
        {
            var csm = FindFirstObjectByType<CharacterSelectManager>();
            if (csm != null)
                csm.RequestSelectCharacterServerRpc(confirmedIndex);
        }

        Debug.Log($"[CharacterSelectSystem] Confirmed: {selected.characterName} (index {confirmedIndex})");
    }
}
