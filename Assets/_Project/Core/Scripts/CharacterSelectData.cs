using UnityEngine;

public enum InvestigatorProfession
{
    MineWorker,
    HazardSpecialist,
    Explorer,
    CursedPriest,
    FieldMedic
}

[System.Serializable]
public class InvestigatorCharacterData
{
    public string characterName;
    public InvestigatorProfession profession;
    public Sprite characterIcon;
    public GameObject characterPrefab;

    [TextArea(2, 4)]
    public string description;

    [TextArea(2, 4)]
    public string specialAbilities;
}
