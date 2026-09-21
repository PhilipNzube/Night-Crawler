using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NightCrawler.UI
{
    /// <summary>
    /// Binds player lobby data (Name, Ready state, Player Level, Character Portrait)
    /// to a Heat UI card or list item (such as the adapted Heat Achievement Item).
    /// </summary>
    public class HeatPlayerStatusRow : MonoBehaviour
    {
        [Header("Character Portrait")]
        [Tooltip("The Image component displaying the chosen operative portrait or the Girl's icon.")]
        public Image characterPortraitImage;

        [Header("Text Content")]
        [Tooltip("TextMeshPro label for player name.")]
        public TextMeshProUGUI playerNameText;

        [Tooltip("TextMeshPro label for player level and rank badge (e.g. 'Lv. 14 • Operative').")]
        public TextMeshProUGUI playerLevelText;

        [Tooltip("TextMeshPro label for ready status (e.g. 'READY' / 'NOT READY').")]
        public TextMeshProUGUI statusText;

        [Header("Indicators / Badges")]
        [Tooltip("Visual indicator active when player is READY (e.g. Heat Unlocked Indicator / green glow).")]
        public GameObject readyIndicator;

        [Tooltip("Visual indicator active when player is NOT READY (e.g. Heat Locked Indicator / dim frame).")]
        public GameObject waitingIndicator;

        [Header("Status Colors")]
        public Color readyColor = new Color(0.18f, 0.80f, 0.44f);   // Bright Emerald Green
        public Color waitingColor = new Color(0.91f, 0.30f, 0.24f); // Vibrant Crimson Red

        /// <summary>
        /// Populates this status row with the player's info.
        /// </summary>
        public void Setup(string playerName, bool isReady, int playerLevel, string rankTitle, Sprite portrait, bool isGirl)
        {
            if (playerNameText != null)
            {
                playerNameText.text = isGirl ? $"{playerName} <color=#E74C3C>[SPIRIT]</color>" : playerName;
            }

            if (playerLevelText != null)
            {
                string rank = string.IsNullOrEmpty(rankTitle) ? "Recruit" : rankTitle;
                playerLevelText.text = $"Lv. {playerLevel} • {rank}";
            }

            if (statusText != null)
            {
                statusText.text = isReady ? "READY" : "NOT READY";
                statusText.color = isReady ? readyColor : waitingColor;
            }

            if (readyIndicator != null)
                readyIndicator.SetActive(isReady);

            if (waitingIndicator != null)
                waitingIndicator.SetActive(!isReady);

            if (characterPortraitImage != null)
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
        }
    }
}
