using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Shows on-screen prompt when looking at or standing near a lootable corpse.
/// Displays "[E] Loot Corpse (X Vials)" when in range.
/// Features self-contained procedural UI generation if not wired in the Inspector,
/// and measures distance directly to the fallen ragdoll's physical body.
/// </summary>
public class CorpseInteractionHUD : MonoBehaviour
{
    [Header("UI Reference")]
    public TMP_Text promptText;
    public GameObject promptPanel;

    [Header("Detection Settings")]
    public float maxPromptDistance = 3.5f;

    [Header("Prompt Formatting")]
    [Tooltip("Format string for the prompt text. {0} is the key, {1} is the loot summary.")]
    public string promptFormat = "Press <color=#00E5FF><b>[{0}]</b></color> to loot body";
    public bool showLootSummaryInPrompt = true;

    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        EnsureUI();
        SetPromptVisible(false);
    }

    private void OnEnable()
    {
        EnsureUI();
        SetPromptVisible(false);
    }

    private void EnsureUI()
    {
        if (promptPanel == null && promptText != null)
        {
            promptPanel = promptText.gameObject;
        }

        if (promptPanel != null)
        {
            _canvasGroup = promptPanel.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = promptPanel.AddComponent<CanvasGroup>();
            }
        }

        // If no UI exists in scene/prefab, generate a sleek tactical prompt procedurally
        if (promptText == null)
        {
            BuildProceduralPrompt();
        }
    }

    private void BuildProceduralPrompt()
    {
        Transform canvasRoot = transform;
        if (GetComponentInParent<Canvas>() == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null) canvasRoot = canvas.transform;
        }

        var panelObj = new GameObject("CorpseLootPrompt_Panel");
        panelObj.transform.SetParent(canvasRoot, false);

        var rect = panelObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.32f);
        rect.anchorMax = new Vector2(0.5f, 0.32f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(360f, 48f);

        var bgImg = panelObj.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.06f, 0.09f, 0.90f);
        bgImg.raycastTarget = false;

        var outline = panelObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.15f, 0.75f, 0.95f, 0.65f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        _canvasGroup = panelObj.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        var textObj = new GameObject("PromptText");
        textObj.transform.SetParent(panelObj.transform, false);

        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(15f, 0f);
        textRect.offsetMax = new Vector2(-15f, 0f);

        promptText = textObj.AddComponent<TextMeshProUGUI>();
        promptText.fontSize = 17f;
        promptText.color = Color.white;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.text = "Press <color=#00E5FF><b>[E]</b></color> to loot body";

        promptPanel = panelObj;
        promptPanel.SetActive(false);
    }

    private GameObject GetActiveControlledCharacter()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return null;
        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return null;

        if (localPlayer.TryGetComponent<GirlPossession>(out var possession) && possession.isPossessing.Value)
        {
            if (possession.CurrentTarget is Component comp && comp != null)
            {
                return comp.gameObject;
            }
        }
        return localPlayer.gameObject;
    }

    private void Update()
    {
        var activePlayer = GetActiveControlledCharacter();
        if (activePlayer == null)
        {
            SetPromptVisible(false);
            return;
        }

        // The Girl character in her own body cannot loot corpses — suppress prompt completely
        if (activePlayer.GetComponent<GirlPossession>() != null || 
            activePlayer.GetComponent<GirlMovement>() != null || 
            activePlayer.GetComponent<GirlStealth>() != null)
        {
            SetPromptVisible(false);
            return;
        }

        // Dead players cannot loot
        if (activePlayer.TryGetComponent<TargetHealth>(out var myHealth) && (myHealth.isCorpse.Value || myHealth.CurrentHealth <= 0))
        {
            SetPromptVisible(false);
            return;
        }
        if (activePlayer.TryGetComponent<HealthSystem>(out var myHs) && myHs.IsDead)
        {
            SetPromptVisible(false);
            return;
        }

        // Find nearest corpse with loot
        CorpseLootableNet nearestLootable = null;
        float nearestDist = maxPromptDistance;

        var allLootables = FindObjectsByType<CorpseLootableNet>(FindObjectsSortMode.None);
        foreach (var corpse in allLootables)
        {
            if (corpse.gameObject == activePlayer) continue;

            // Only actual dead corpses can be looted!
            bool isDead = false;
            if (corpse.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) isDead = true;
            else if (corpse.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead) isDead = true;
            if (!isDead) continue;

            if (!corpse.HasLoot) continue;

            // Measure distance to physical ragdoll bone if fallen
            Vector3 corpsePos = corpse.GetCorpsePosition();
            float dist = Vector3.Distance(activePlayer.transform.position, corpsePos);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestLootable = corpse;
            }
        }

        if (nearestLootable != null)
        {
            string keyName = nearestLootable.lootKey.ToString();
            string lootDesc = nearestLootable.GetLootDescription();
            string msg = showLootSummaryInPrompt && !string.IsNullOrEmpty(lootDesc) && lootDesc != "Empty"
                ? string.Format(promptFormat, keyName) + $" <size=85%><color=#FFC107>({lootDesc})</color></size>"
                : string.Format(promptFormat, keyName);

            SetPromptVisible(true, msg);
        }
        else
        {
            SetPromptVisible(false);
        }
    }

    private void SetPromptVisible(bool visible, string text = "")
    {
        if (promptPanel != null)
        {
            // CRITICAL: Never deactivate our own GameObject, as that stops Update() permanently!
            if (promptPanel == gameObject)
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = visible ? 1f : 0f;
                    _canvasGroup.blocksRaycasts = false;
                    _canvasGroup.interactable = false;
                }
                else if (promptText != null && promptText.gameObject != gameObject)
                {
                    promptText.gameObject.SetActive(visible);
                }
            }
            else
            {
                if (promptPanel.activeSelf != visible)
                {
                    promptPanel.SetActive(visible);
                }
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = visible ? 1f : 0f;
                }
            }
        }

        if (promptText != null)
        {
            promptText.text = visible ? text : "";
            if (promptPanel == null && promptText.gameObject != gameObject && promptText.gameObject.activeSelf != visible)
            {
                promptText.gameObject.SetActive(visible);
            }
        }
    }
}
