using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Shows on-screen prompt when looking at or standing near a lootable corpse.
/// Displays "[E] Loot Corpse (X Vials)" when in range.
/// </summary>
public class CorpseInteractionHUD : MonoBehaviour
{
    [Header("UI Reference")]
    public TMP_Text promptText;
    public GameObject promptPanel;

    [Header("Detection Settings")]
    public float maxPromptDistance = 3.0f;

    private void Awake()
    {
        SetPromptVisible(false);
    }

    private void OnEnable()
    {
        SetPromptVisible(false);
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

            float dist = Vector3.Distance(activePlayer.transform.position, corpse.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestLootable = corpse;
            }
        }

        if (nearestLootable != null)
        {
            string lootDesc = nearestLootable.GetLootDescription();
            SetPromptVisible(true, $"[E] Loot Corpse ({lootDesc})");
        }
        else
        {
            SetPromptVisible(false);
        }
    }

    private void SetPromptVisible(bool visible, string text = "")
    {
        if (promptPanel != null && promptPanel.activeSelf != visible)
        {
            promptPanel.SetActive(visible);
        }

        if (promptText != null)
        {
            promptText.text = visible ? text : "";
        }
    }
}
