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
        [Header("UI References")]
        public TextMeshProUGUI potText;
        public GameObject rootContainer;
        public CanvasGroup canvasGroup;

        [Header("Format Settings")]
        public string prefix = "POT: ";
        public bool showCurrencySymbol = true;

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
            if (potText != null)
            {
                string sym = showCurrencySymbol ? $" {CurrencyConfig.CurrencySymbol}" : "";
                potText.text = $"{prefix}{pot}{sym}";
            }
        }
    }
}
