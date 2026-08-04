using UnityEngine;
using TMPro;

/// <summary>
/// SOLID — SRP: Controls the visual content of a single player name card
///              in the Call of Duty style squad lineup UI.
///
/// Attach this component to the SquadTag prefab alongside two TextMeshProUGUI children:
///   • NameText       — the player's display name
///   • ProfessionText — the character's profession (Mine Worker, Medic, etc.)
/// </summary>
public class SquadTagUI : MonoBehaviour
{
    [Header("Text References")]
    [Tooltip("TextMeshProUGUI displaying the player's in-game name.")]
    public TextMeshProUGUI nameText;

    [Tooltip("TextMeshProUGUI displaying the character's profession.")]
    public TextMeshProUGUI professionText;

    /// <summary>
    /// Populates the name card with the player's name and profession.
    /// Safe to call even if text references are null — no crash.
    /// </summary>
    public void SetTag(string playerName, string profession)
    {
        if (nameText != null)
            nameText.text = playerName;

        if (professionText != null)
            professionText.text = profession;
    }
}
