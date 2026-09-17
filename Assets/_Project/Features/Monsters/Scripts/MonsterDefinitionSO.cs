using UnityEngine;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// SOLID — OCP: Defines a summonable monster type that the Girl can awaken from the dead.
    /// Easily add more monsters by creating new assets via Right-click -> Create -> Night Crawler -> Monster Definition.
    /// </summary>
    [CreateAssetMenu(fileName = "NewMonsterDefinition", menuName = "Night Crawler/Monster Definition", order = 20)]
    public class MonsterDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string monsterName = "Abyssal Lurker";
        public Sprite icon;

        [Header("Prefab")]
        [Tooltip("The networked monster prefab to instantiate (must have NetworkObject component).")]
        public GameObject monsterPrefab;

        [Header("Lore & Details")]
        [TextArea(2, 4)]
        public string description = "A feral subterranean beast that stalks investigators in the dark.";

        [Tooltip("Danger rating from 1 to 5.")]
        [Range(1, 5)]
        public int dangerRating = 3;

        [Tooltip("Optional spawn sound effect played when this monster is risen.")]
        public AudioClip spawnSound;
    }
}
