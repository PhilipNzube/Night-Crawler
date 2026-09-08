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

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            SetPromptVisible(false);
            return;
        }

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null)
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
            if (corpse.gameObject == localPlayer.gameObject) continue;
            if (!corpse.HasLoot) continue;

            float dist = Vector3.Distance(localPlayer.transform.position, corpse.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestLootable = corpse;
            }
        }

        if (nearestLootable != null)
        {
            int vials = nearestLootable.RemainingVials;
            SetPromptVisible(true, $"[E] Loot Corpse ({vials} Vial{(vials > 1 ? "s" : "")})");
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
            if (visible) promptText.text = text;
            else if (promptPanel == null) promptText.text = "";
        }
    }
}
