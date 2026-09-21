using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Michsky.UI.Heat;
using NightCrawler.Economy;

namespace NightCrawler.UI
{
    /// <summary>
    /// Encapsulates a single upgrade stat progress bar row item.
    /// Exposes fields for manual inspector assignment (no fragile auto-finding).
    /// Works with Michsky Heat UI ProgressBar, Unity UI Slider, and TextMeshPro labels.
    /// </summary>
    [Serializable]
    public class StatProgressBarItem
    {
        [Tooltip("The persistent upgrade stat category represented by this progress bar.")]
        public UpgradeStatType statType;

        [Tooltip("Root GameObject of this stat row / bar container to show or hide dynamically.")]
        public GameObject container;

        [Tooltip("Michsky Heat UI ProgressBar component.")]
        public ProgressBar progressBar;

        [Tooltip("Optional standard Unity UI Slider fallback.")]
        public Slider slider;

        [Tooltip("Optional label displaying stat title (e.g. 'Armor Resistance'). If left empty, will use default display name.")]
        public TextMeshProUGUI titleText;

        [Tooltip("Optional text displaying current tier level (e.g. 'Lv. 3 / 5' or 'MAX').")]
        public TextMeshProUGUI levelText;

        [Tooltip("Optional text displaying active stat effect / value (e.g. '+24% Weapon Damage').")]
        public TextMeshProUGUI effectText;

        /// <summary>
        /// Toggles the visibility of this stat row container.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (container != null)
            {
                container.SetActive(visible);
            }
            else if (progressBar != null)
            {
                progressBar.gameObject.SetActive(visible);
            }
            else if (slider != null)
            {
                slider.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Updates the bar fill and text labels based on the exact upgrade level (0 to 5).
        /// </summary>
        public void Refresh(int level)
        {
            bool isMax = level >= 5;

            if (titleText != null)
            {
                titleText.text = UpgradeStatFormulas.GetStatDisplayName(statType);
            }

            if (levelText != null)
            {
                levelText.text = isMax ? "MAX" : $"Lv. {level} / 5";
            }

            if (effectText != null)
            {
                effectText.text = UpgradeStatFormulas.GetStatEffectDescription(statType, level);
            }

            float fraction = Mathf.Clamp01(level / 5f);

            if (progressBar != null)
            {
                MichskyUIBridge.SetProgress(progressBar, fraction);
            }

            if (slider != null)
            {
                slider.value = fraction;
            }
        }
    }
}
