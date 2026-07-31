using UnityEngine;

[CreateAssetMenu(fileName = "NewAbility", menuName = "TheBuriedOne/AbilityData")]
public class AbilityData : ScriptableObject
{
    public float range = 15f;
    public float cooldown = 5f;
    public float offset = 1.5f;
    public int damage = 25;
    // You can even store the VFX here!
    public GameObject effectPrefab; 
}