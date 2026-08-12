using UnityEngine;

/// <summary>
/// ScriptableObject data container for a single selectable character.
/// Create one asset per character via: Right-click → Create → Night Crawler → Character Definition.
/// This replaces the old InvestigatorCharacterData plain class — it is now a proper project asset.
/// </summary>
[CreateAssetMenu(menuName = "Night Crawler/Character Definition", fileName = "NewCharacterDefinition")]
public class CharacterDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string characterName = "New Character";

    [TextArea(2, 4)]
    public string description = "";

    [TextArea(2, 4)]
    public string lore = "";

    [Header("Role")]
    public CharacterRole role = CharacterRole.Investigator;

    [Header("Visuals")]
    [Tooltip("The 3D prefab spawned for preview in the Character Select scene.")]
    public GameObject characterPrefab;

    [Tooltip("2D portrait / thumbnail used in selection slot cards.")]
    public Sprite portrait;

    [Tooltip("Background card color tint for this character's slot.")]
    public Color cardColor = Color.white;

    [Header("Stats Display")]
    [Range(0f, 10f)] public float speed    = 5f;
    [Range(0f, 10f)] public float strength = 5f;
    [Range(0f, 10f)] public float stealth  = 5f;

    [Header("Abilities")]
    [TextArea(2, 6)]
    public string abilityDescriptions = "";
}

public enum CharacterRole
{
    Investigator,
    VengefulSpirit,
    Monster
}
