using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Unity.Netcode;
using Michsky.UI.Heat;

/// <summary>
/// SOLID — SRP: Universal contextual interaction prompt HUD.
/// Detects nearby interactables based on situational context:
/// 1. Critically wounded teammates (Health <= 25%): Prompts to send/administer a healing vial.
/// 2. Fallen player corpses with loot: Prompts to loot the corpse.
/// Integrates with Michsky Heat UI QuestItem:
/// - Header: Displays strictly the bound button (e.g. 'E', not '[E]')
/// - Body: Displays the action prompt (e.g. 'Send Healing Vial' or 'Loot Body')
/// - Animated In/Out smoothly using QuestItem Expand/Minimize.
/// </summary>
public class ContextInteractionHUD : MonoBehaviour
{
    public enum InteractionType
    {
        None,
        HealTeammate,
        LootCorpse
    }

    [Header("Heat UI Quest Structure")]
    [Tooltip("Heat UI QuestItem component on PromptTextGO (handles In/Out animations).")]
    public QuestItem questItem;

    [Tooltip("Text component inside Header displaying strictly the bound button (e.g. 'E').")]
    public TMP_Text headerButtonText;

    [Tooltip("Text component inside PromptText displaying the prompt body text (e.g. 'Loot Body' or 'Send Healing Vial').")]
    public TMP_Text promptBodyText;

    public GameObject promptPanel;

    [Header("Healing Interaction Settings")]
    [Tooltip("Health percentage below which a teammate triggers the healing prompt (e.g. 0.25 = 25% HP).")]
    [Range(0.05f, 0.5f)] public float criticalHealthThreshold = 0.25f;
    [Tooltip("Maximum distance to a critically wounded teammate to show the heal prompt.")]
    public float maxHealDistance = 3.5f;
    [Tooltip("Key to administer/send a healing vial (default [E]).")]
    public Key healKey = Key.E;

    [Header("Corpse Looting Settings")]
    [Tooltip("Maximum distance to a lootable corpse.")]
    public float maxCorpseDistance = 3.5f;
    public bool showLootSummaryInPrompt = true;

    private CanvasGroup _canvasGroup;
    private bool _isPromptVisible = false;

    // Current interaction context
    private InteractionType _currentType = InteractionType.None;
    private HealthSystem _targetTeammateToHeal = null;
    private CorpseLootableNet _targetCorpseToLoot = null;
    private GameObject _activePlayer = null;

    protected virtual void Awake()
    {
        EnsureUI();
        SetPromptVisible(false);


    }

    protected virtual void OnEnable()
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
        }

        if (promptPanel == null)
        {
            if (questItem != null) promptPanel = questItem.gameObject;
    
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

    protected virtual void Update()
    {
        _activePlayer = GetActiveControlledCharacter();
        if (_activePlayer == null || PauseManager.IsGamePaused)
        {
            SetPromptVisible(false);
            return;
        }

        // The Girl in her own body cannot loot or administer vials
        if (_activePlayer.GetComponent<GirlPossession>() != null || 
            _activePlayer.GetComponent<GirlMovement>() != null || 
            _activePlayer.GetComponent<GirlStealth>() != null)
        {
            SetPromptVisible(false);
            return;
        }

        // Dead players cannot interact
        if (_activePlayer.TryGetComponent<TargetHealth>(out var myHealth) && (myHealth.isCorpse.Value || myHealth.CurrentHealth <= 0))
        {
            SetPromptVisible(false);
            return;
        }
        if (_activePlayer.TryGetComponent<HealthSystem>(out var myHs) && myHs.IsDead)
        {
            SetPromptVisible(false);
            return;
        }

        // 1. PRIORITY 1: Check for critically wounded teammates nearby (Health <= 25%)
        HealingVialInventoryNet vialInventory = _activePlayer.GetComponent<HealingVialInventoryNet>();
        HealthSystem nearestCriticalTeammate = null;
        float nearestCritDist = maxHealDistance;

        if (vialInventory != null && vialInventory.VialCount > 0)
        {
            var allHealthSystems = FindObjectsByType<HealthSystem>(FindObjectsSortMode.None);
            foreach (var hs in allHealthSystems)
            {
                if (hs == null || hs.gameObject == _activePlayer || hs.IsDead) continue;

                // Ignore monsters
                if (hs.CompareTag("Monster") || hs.GetComponent<TargetHealth>() != null && !hs.GetComponent<TargetHealth>().isCorpse.Value && hs.GetComponent<MonsterAI>() != null)
                    continue;

                // Check critical health threshold (<= 25%)
                if (hs.MaxHealth > 0 && (hs.CurrentHealth / hs.MaxHealth) <= criticalHealthThreshold)
                {
                    float dist = Vector3.Distance(_activePlayer.transform.position, hs.transform.position);
                    if (dist < nearestCritDist)
                    {
                        nearestCritDist = dist;
                        nearestCriticalTeammate = hs;
                    }
                }
            }
        }

        if (nearestCriticalTeammate != null)
        {
            _currentType = InteractionType.HealTeammate;
            _targetTeammateToHeal = nearestCriticalTeammate;
            _targetCorpseToLoot = null;

            string keyName = healKey.ToString().Trim();
            string bodyMsg = $"Send Healing Vial <size=85%>({vialInventory.VialCount} left)</size>";

            SetPromptVisible(true, keyName, bodyMsg);

            // Handle Key press
            if (Keyboard.current != null && Keyboard.current[healKey].wasPressedThisFrame)
            {
                ExecuteCurrentAction();
            }
            return;
        }

        // 2. PRIORITY 2: Check for nearby lootable corpse
        CorpseLootableNet nearestLootable = null;
        float nearestDist = maxCorpseDistance;

        var allLootables = FindObjectsByType<CorpseLootableNet>(FindObjectsSortMode.None);
        foreach (var corpse in allLootables)
        {
            if (corpse == null || corpse.gameObject == _activePlayer) continue;

            bool isDead = false;
            if (corpse.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) isDead = true;
            else if (corpse.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead) isDead = true;
            if (!isDead || !corpse.HasLoot) continue;

            Vector3 corpsePos = corpse.GetCorpsePosition();
            float dist = Vector3.Distance(_activePlayer.transform.position, corpsePos);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestLootable = corpse;
            }
        }

        if (nearestLootable != null)
        {
            _currentType = InteractionType.LootCorpse;
            _targetCorpseToLoot = nearestLootable;
            _targetTeammateToHeal = null;

            string keyName = nearestLootable.lootKey.ToString().Replace("[", "").Replace("]", "").Trim();
            string lootDesc = nearestLootable.GetLootDescription();

            string bodyMsg = showLootSummaryInPrompt && !string.IsNullOrEmpty(lootDesc) && lootDesc != "Empty"
                ? $"Loot Body <size=85%>({lootDesc})</size>"
                : "Loot Body";

            SetPromptVisible(true, keyName, bodyMsg);

            // Handle Key press
            if (Keyboard.current != null && Keyboard.current[nearestLootable.lootKey].wasPressedThisFrame)
            {
                ExecuteCurrentAction();
            }
        }
        else
        {
            _currentType = InteractionType.None;
            _targetTeammateToHeal = null;
            _targetCorpseToLoot = null;
            SetPromptVisible(false);
        }
    }

    /// <summary>
    /// Executes the active context action (triggered either by keyboard hotkey or by clicking the UI prompt).
    /// </summary>
    public void ExecuteCurrentAction()
    {
        if (_activePlayer == null) return;

        if (_currentType == InteractionType.HealTeammate && _targetTeammateToHeal != null)
        {
            HealingVialInventoryNet vialInventory = _activePlayer.GetComponent<HealingVialInventoryNet>();
            if (vialInventory != null)
            {
                vialInventory.TryHealTarget(_targetTeammateToHeal);
            }
        }
        else if (_currentType == InteractionType.LootCorpse && _targetCorpseToLoot != null)
        {
            var netObj = _activePlayer.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                _targetCorpseToLoot.StartLooting(_activePlayer, netObj);
            }
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

            // 2. Main body shows strictly the prompt text ("Loot Body" / "Send Healing Vial")
            if (promptBodyText != null)
            {
                promptBodyText.text = bodyMsg;
            }



            // 4. Trigger In animation / display
            if (!_isPromptVisible)
            {
                _isPromptVisible = true;
                if (questItem != null)
                {
                    questItem.minimizeAfter = 0;
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
