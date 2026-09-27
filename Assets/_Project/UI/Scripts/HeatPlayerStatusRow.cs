using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NightCrawler.UI
{
    /// <summary>
    /// Binds player lobby data directly to the HeatPlayerStatusRow prefab.
    /// Controls player name, role/level subtext, preset icon, and ready/not-ready indicators.
    /// Matches the serialized inspector fields on HeatPlayerStatusRow.prefab.
    /// </summary>
    public class HeatPlayerStatusRow : MonoBehaviour
    {
        [Header("Icon Settings")]
        [Tooltip("If false (default), keeps the preset icon configured on this prefab and does not overwrite it at runtime.")]
        public bool updatePortrait = false;

        [Tooltip("The Image component displaying the icon / portrait.")]
        public Image characterPortraitImage;

        [Header("Text Labels")]
        [Tooltip("TextMeshPro label for player name.")]
        public TextMeshProUGUI playerNameText;

        [Tooltip("TextMeshPro label for subtext (e.g. 'Miner' or 'Lv. 1 • Miner').")]
        public TextMeshProUGUI playerLevelText;

        [Header("Ready / Not Ready Indicators (Prefab Icons)")]
        [Tooltip("Visual indicator / icon active when player is READY (e.g. Unlocked Indicator).")]
        public GameObject readyIndicator;

        [Tooltip("Visual indicator / icon active when player is NOT READY (e.g. Locked Indicator).")]
        public GameObject waitingIndicator;

        /// <summary>
        /// Populates this status row with the player's info.
        /// Activates the readyIndicator / waitingIndicator icons set on the prefab.
        /// </summary>
        public void Setup(string playerName, bool isReady, string subtext, Sprite portrait = null)
        {
            if (playerNameText != null)
            {
                playerNameText.text = playerName;
            }

            if (playerLevelText != null)
            {
                playerLevelText.text = subtext;
            }

            // Set Ready / Not Ready using the indicators/icons set on the prefab
            if (readyIndicator != null)
                readyIndicator.SetActive(isReady);

            if (waitingIndicator != null)
                waitingIndicator.SetActive(!isReady);

            // Icon Handling: Keep preset icon unless explicitly requested to overwrite
            if (characterPortraitImage != null)
            {
                if (updatePortrait)
                {
                    if (portrait != null)
                    {
                        characterPortraitImage.sprite = portrait;
                        characterPortraitImage.enabled = true;
                    }
                    else
                    {
                        characterPortraitImage.enabled = false;
                    }
                }
                else
                {
                    if (!characterPortraitImage.enabled && characterPortraitImage.sprite != null)
                    {
                        characterPortraitImage.enabled = true;
                    }
                }
            }
        }
    }
}
