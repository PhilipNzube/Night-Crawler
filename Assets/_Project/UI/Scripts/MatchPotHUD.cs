using UnityEngine;
using TMPro;
using NightCrawler.Economy;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — SRP: Compact in-game HUD element displaying the shared match stake pot.
    /// Updates dynamically when players confirm stakes.
    /// </summary>
    public class MatchPotHUD : MonoBehaviour
    {
        [Header("Heat UI TotalStakeText Structure")]
        [Tooltip("Text displaying the pot amount (child 'Total'). If null, auto-finds child named 'Total'.")]
        public TextMeshProUGUI amountText;

        [Tooltip("Text displaying the header label (child 'Header/Text').")]
        public TextMeshProUGUI headerText;

        [Tooltip("Default header title text if headerText is assigned.")]
        public string headerTitle = "Total Match Stake";

        [Header("Legacy / Direct UI References")]
        [Tooltip("Direct pot text component if not using the Header/Total structure.")]
        public TextMeshProUGUI potText;

        [Header("Format Settings")]
        public string prefix = "POT: ";
        public bool showCurrencySymbol = true;

        private void Awake()
        {
            // Auto-detect structure of TotalStakeText if references are unassigned or miswired
            if (amountText == null)
            {
                Transform totalTrans = transform.Find("Total");
                if (totalTrans != null)
                {
                    amountText = totalTrans.GetComponent<TextMeshProUGUI>();
                }
            }

            if (headerText == null)
            {
                Transform headerTrans = transform.Find("Header");
                if (headerTrans != null)
                {
                    headerText = headerTrans.GetComponentInChildren<TextMeshProUGUI>();
                }
            }

            // If potText was accidentally pointed to the Header Text, redirect it
            if (potText != null && headerText != null && potText == headerText)
            {
                if (amountText == null)
                {
                    Transform totalTrans = transform.Find("Total");
                    if (totalTrans != null) amountText = totalTrans.GetComponent<TextMeshProUGUI>();
                }
            }
        }

        private void Start()
        {
            MatchEconomyManager.OnTotalPotChanged += HandlePotChanged;

            if (MatchEconomyManager.Instance != null)
            {
                HandlePotChanged(MatchEconomyManager.Instance.totalPot.Value);
            }
            else
            {
                UpdateText(0);
            }
        }

        private void OnDestroy()
        {
            MatchEconomyManager.OnTotalPotChanged -= HandlePotChanged;
        }

        private void HandlePotChanged(int newPot)
        {
            UpdateText(newPot);
        }

        private void UpdateText(int pot)
        {
            string sym = CurrencyConfig.CurrencySymbol;

            // 1. Update the TotalStakeText amount (e.g. "$60" or "60")
            if (amountText != null)
            {
                amountText.text = showCurrencySymbol ? $"{sym}{pot}" : pot.ToString();
            }

            // 2. Ensure header text displays proper title
            if (headerText != null && !string.IsNullOrEmpty(headerTitle))
            {
                headerText.text = headerTitle;
            }

            // 3. Update legacy/fallback potText if assigned and separate from header
            if (potText != null && potText != headerText && potText != amountText)
            {
                potText.text = $"{prefix}{pot}{(showCurrencySymbol ? $" {sym}" : "")}";
            }
        }
    }
}
