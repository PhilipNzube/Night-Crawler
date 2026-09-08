using UnityEngine;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: High-level investigator coordinator. Ensures appropriate character traits,
/// inventories, and ability modules are properly active based on role and profession.
/// </summary>
[RequireComponent(typeof(HealthSystem))]
public class InvestigatorAbilities : NetworkBehaviour
{
    [Header("Role Identification")]
    public InvestigatorProfession profession = InvestigatorProfession.Explorer;

    [Header("Sub-Systems (Auto-Cached or Configured)")]
    public InvestigatorCombatNet combatNet;
    public HealingVialInventoryNet vialInventory;
    public SuffocationSystemNet suffocationNet;
    public PriestExorcismNet priestExorcism;
    public CorpseLootableNet corpseLootable;
    public NetworkRagdollController ragdollController;
    public PlayerPossessableNet possessableNet;

    private void Awake()
    {
        combatNet = GetComponent<InvestigatorCombatNet>();
        vialInventory = GetComponent<HealingVialInventoryNet>();
        suffocationNet = GetComponent<SuffocationSystemNet>();
        priestExorcism = GetComponent<PriestExorcismNet>();
        corpseLootable = GetComponent<CorpseLootableNet>();
        ragdollController = GetComponent<NetworkRagdollController>();
        possessableNet = GetComponent<PlayerPossessableNet>();

        AutoDetectProfession();
    }

    public override void OnNetworkSpawn()
    {
        ApplyProfessionTraits();
    }

    private void AutoDetectProfession()
    {
        string objName = gameObject.name.ToLower();
        if (objName.Contains("miner") || objName.Contains("worker"))
            profession = InvestigatorProfession.MineWorker;
        else if (objName.Contains("medic"))
            profession = InvestigatorProfession.FieldMedic;
        else if (objName.Contains("hazard") || objName.Contains("protector"))
            profession = InvestigatorProfession.HazardSpecialist;
        else if (objName.Contains("priest"))
            profession = InvestigatorProfession.CursedPriest;
        else if (objName.Contains("adventure") || objName.Contains("explorer"))
            profession = InvestigatorProfession.Explorer;
    }

    private void ApplyProfessionTraits()
    {
        switch (profession)
        {
            case InvestigatorProfession.MineWorker:
                if (combatNet != null && IsOwner)
                {
                    combatNet.startArmed = true;
                    combatNet.SwitchWeapon(0);
                }
                break;

            case InvestigatorProfession.FieldMedic:
                if (vialInventory != null && IsServer)
                {
                    vialInventory.currentVials.Value = Mathf.Max(4, vialInventory.initialVials);
                }
                break;

            case InvestigatorProfession.HazardSpecialist:
                if (suffocationNet != null)
                {
                    // Suffocation system doubles lifespan for Hazard Specialist
                }
                break;

            case InvestigatorProfession.CursedPriest:
                if (priestExorcism != null)
                {
                    priestExorcism.enabled = true;
                }
                break;

            case InvestigatorProfession.Explorer:
                // Explorer minimap bound via AdventurerMinimapSetup
                break;
        }
    }
}
