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

        [Tooltip("Optional label displaying stat title (e.g. 'Armor Resistance'). If left empty, will use default display name.")]
        public TextMeshProUGUI titleText;

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
        }

        /// <summary>
        /// Updates the bar fill and title label based on the exact upgrade level (0 to 5).
        /// </summary>
        public void Refresh(int level)
        {
            if (titleText != null)
            {
                titleText.text = UpgradeStatFormulas.GetStatDisplayName(statType);
            }

            float fraction = Mathf.Clamp01(level / 5f);

            if (progressBar != null)
            {
                MichskyUIBridge.SetProgress(progressBar, fraction);
            }
        }
    }
}
