using System;
using UnityEngine;
using TMPro;
using Michsky.UI.Heat;
using NightCrawler.Economy;

namespace NightCrawler.UI
{
    /// <summary>
    /// Encapsulates a single upgrade stat progress bar row item.
    /// Exposes fields for manual inspector assignment.
    /// Works with Michsky Heat UI ProgressBar and TextMeshPro labels.
    /// Cleaned: Removed unassigned fallback fields (slider, levelText, effectText).
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

        [Tooltip("Optional standard Unity Slider component fallback.")]
        public UnityEngine.UI.Slider slider;

        [Tooltip("Optional standard Unity Image with Filled type.")]
        public UnityEngine.UI.Image fillImage;

        [Tooltip("Optional label displaying stat title (e.g. 'Armor Resistance'). If left empty, will use default display name.")]
        public TextMeshProUGUI titleText;

        [Tooltip("Optional label displaying the stat effect or value (e.g. '-15% Physical Damage', '45s Manifestation Bank').")]
        public TextMeshProUGUI valueText;

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
        /// Updates the bar fill, title label, and effect text based on the exact upgrade level (1 to 5).
        /// Beginners start with a filled baseline tier so progress bars are never empty.
        /// </summary>
        public void Refresh(int level)
        {
            if (titleText != null)
            {
                titleText.text = UpgradeStatFormulas.GetStatDisplayName(statType);
            }

            if (valueText != null)
            {
                valueText.text = UpgradeStatFormulas.GetStatEffectDescription(statType, level);
            }

            // Baseline: Level 0 or 1 displays at least 20% starting bar fill, scaling to 100% at Tier 5
            float fraction = Mathf.Clamp(Mathf.Max(0.20f, level / 5f), 0.20f, 1.0f);

            if (progressBar != null)
            {
                MichskyUIBridge.SetProgress(progressBar, fraction);
            }

            if (slider != null)
            {
                slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, fraction);
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = fraction;
            }
        }
    }
}
