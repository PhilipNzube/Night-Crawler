using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Michsky.UI.Heat;

/// <summary>
/// SOLID — SRP: Shows on-screen prompt when looking at or standing near a lootable corpse.
/// Integrates with Michsky Heat UI QuestItem:
/// - Header: Displays strictly the bound button (e.g. 'E', not '[E]')
/// - Body: Displays the action prompt (e.g. 'Loot Body' or 'Loot Body (3 Vials)')
/// - Animated In/Out smoothly using QuestItem Expand/Minimize.
/// </summary>
public class CorpseInteractionHUD : MonoBehaviour
{
    [Header("Heat UI Quest Structure")]
    [Tooltip("Heat UI QuestItem component on PromptTextGO (handles In/Out animations).")]
    public QuestItem questItem;

    [Tooltip("Text component inside Header displaying strictly the bound button (e.g. 'E').")]
    public TMP_Text headerButtonText;

    [Tooltip("Text component inside PromptText displaying the prompt body text (e.g. 'Loot Body').")]
    public TMP_Text promptBodyText;

    [Header("Legacy / Direct UI Reference")]
    public TMP_Text promptText;
    public GameObject promptPanel;

    [Header("Detection Settings")]
    public float maxPromptDistance = 3.5f;
    public bool showLootSummaryInPrompt = true;

    private CanvasGroup _canvasGroup;
    private bool _isPromptVisible = false;

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
        if (questItem == null)
        {
            questItem = GetComponentInChildren<QuestItem>(true);
        }

        if (headerButtonText == null)
        {
            Transform headerTextTrans = transform.Find("PromptTextGO/PromptTextMain/Header/Text");
            if (headerTextTrans == null) headerTextTrans = transform.Find("PromptTextGO/Header/Text");
            if (headerTextTrans != null)
            {
                headerButtonText = headerTextTrans.GetComponent<TMP_Text>();
            }
        }

        if (promptBodyText == null)
        {
            Transform bodyTextTrans = transform.Find("PromptTextGO/PromptTextMain/PromptText");
            if (bodyTextTrans != null)
            {
                promptBodyText = bodyTextTrans.GetComponent<TMP_Text>();
            }
            else if (promptText != null)
            {
                promptBodyText = promptText;
            }
        }

        if (promptPanel == null)
        {
            if (questItem != null) promptPanel = questItem.gameObject;
            else if (promptText != null) promptPanel = promptText.gameObject;
        }

        if (promptPanel != null)
        {
            _canvasGroup = promptPanel.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = promptPanel.AddComponent<CanvasGroup>();
            }
        }
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
            // Strictly the key name (e.g. "E", not "[E]")
            string keyName = nearestLootable.lootKey.ToString().Replace("[", "").Replace("]", "").Trim();
            string lootDesc = nearestLootable.GetLootDescription();

            string bodyMsg = showLootSummaryInPrompt && !string.IsNullOrEmpty(lootDesc) && lootDesc != "Empty"
                ? $"Loot Body <size=85%>({lootDesc})</size>"
                : "Loot Body";

            SetPromptVisible(true, keyName, bodyMsg);
        }
        else
        {
            SetPromptVisible(false);
        }
    }

    private void SetPromptVisible(bool visible, string keyName = "", string bodyMsg = "")
    {
        if (visible)
        {
            // 1. Header shows strictly the bound button ("E")
            if (headerButtonText != null)
            {
                headerButtonText.text = keyName;
            }

            // 2. Main body shows strictly the prompt text ("Loot Body")
            if (promptBodyText != null)
            {
                promptBodyText.text = bodyMsg;
            }

            // 3. Fallback single promptText if present
            if (promptText != null && promptText != promptBodyText)
            {
                promptText.text = $"[{keyName}] {bodyMsg}";
            }

            // 4. Trigger In animation / display
            if (!_isPromptVisible)
            {
                _isPromptVisible = true;
                if (questItem != null)
                {
                    questItem.minimizeAfter = 0; // Don't auto-minimize while player is still near corpse
                    questItem.afterMinimize = QuestItem.AfterMinimize.Disable;
                    questItem.ExpandQuest();
                }
                else if (promptPanel != null)
                {
                    if (promptPanel != gameObject) promptPanel.SetActive(true);
                    if (_canvasGroup != null) _canvasGroup.alpha = 1f;
                }
            }
        }
        else
        {
            // Trigger Out animation / hide
            if (_isPromptVisible)
            {
                _isPromptVisible = false;
                if (questItem != null)
                {
                    questItem.MinimizeQuest();
                }
                else if (promptPanel != null)
                {
                    if (_canvasGroup != null) _canvasGroup.alpha = 0f;
                    if (promptPanel != gameObject) promptPanel.SetActive(false);
                }
            }
        }
    }
}

