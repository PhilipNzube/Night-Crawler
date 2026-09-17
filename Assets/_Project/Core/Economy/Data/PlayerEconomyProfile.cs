using System;
using UnityEngine;

namespace NightCrawler.Economy
{
    /// <summary>
    /// Supported upgrade stat categories for both Investigators and The Girl.
    /// </summary>
    public enum UpgradeStatType
    {
        // Investigator Stats
        DamageResistance,
        WeaponDamage,
        MaskFilter,
        SpiritualLevel,
        MapPower,
        VialCount,
        VialHealingPower,

        // Girl (Vengeful Spirit) Stats
        PossessionDuration,
        DealCapacity,
        VisibilityCount,
        VisibilityDuration,
        DeadSummonCharges
    }

    /// <summary>
    /// Currency configuration constants. Replaces plain dollars with thematic lore currency.
    /// </summary>
    public static class CurrencyConfig
    {
        public const string CurrencyName = "Cinders";
        public const string CurrencySymbol = "₵";
        public const int DefaultStartingBalance = 60; // Enough for several starting matches & early upgrades
        public const int MinimumStake = 2;
        public const float MaxStakeCapPercentage = 0.60f; // Max 60% of balance (no all-in)
        public const int GirlLossPenalty = 25; // Penalty deducted from Girl on loss and redistributed
        public const int TraitorPenalty = 15; // Penalty deducted from traitor if Girl loses
    }

    /// <summary>
    /// Serializable persistent economy profile storing credits and upgrade levels.
    /// Carried over across matches and serialized to Unity Cloud Save + Local PlayerPrefs.
    /// </summary>
    [Serializable]
    public class PlayerEconomyProfile
    {
        [Header("Currency")]
        public int credits = CurrencyConfig.DefaultStartingBalance;

        [Header("Investigator Persistent Upgrades")]
        public int damageResistanceLevel = 0;
        public int weaponDamageLevel = 0;
        public int maskFilterLevel = 0;
        public int spiritualLevel = 0;
        public int mapPowerLevel = 0;
        public int vialCountLevel = 0;
        public int vialHealingPowerLevel = 0;

        [Header("The Girl Persistent Upgrades")]
        public int possessionDurationLevel = 0;
        public int dealCapacityLevel = 0;
        public int visibilityCountLevel = 0;
        public int visibilityDurationLevel = 0;
        public int deadSummonChargesLevel = 0;

        /// <summary>
        /// Gets the current upgrade level for any stat type.
        /// </summary>
        public int GetLevel(UpgradeStatType stat)
        {
            switch (stat)
            {
                case UpgradeStatType.DamageResistance:     return damageResistanceLevel;
                case UpgradeStatType.WeaponDamage:        return weaponDamageLevel;
                case UpgradeStatType.MaskFilter:          return maskFilterLevel;
                case UpgradeStatType.SpiritualLevel:      return spiritualLevel;
                case UpgradeStatType.MapPower:            return mapPowerLevel;
                case UpgradeStatType.VialCount:           return vialCountLevel;
                case UpgradeStatType.VialHealingPower:    return vialHealingPowerLevel;

                case UpgradeStatType.PossessionDuration:  return possessionDurationLevel;
                case UpgradeStatType.DealCapacity:        return dealCapacityLevel;
                case UpgradeStatType.VisibilityCount:     return visibilityCountLevel;
                case UpgradeStatType.VisibilityDuration:  return visibilityDurationLevel;
                case UpgradeStatType.DeadSummonCharges:   return deadSummonChargesLevel;
                default: return 0;
            }
        }

        /// <summary>
        /// Sets the upgrade level for any stat type.
        /// </summary>
        public void SetLevel(UpgradeStatType stat, int level)
        {
            level = Mathf.Max(0, level);
            switch (stat)
            {
                case UpgradeStatType.DamageResistance:     damageResistanceLevel = level; break;
                case UpgradeStatType.WeaponDamage:        weaponDamageLevel = level; break;
                case UpgradeStatType.MaskFilter:          maskFilterLevel = level; break;
                case UpgradeStatType.SpiritualLevel:      spiritualLevel = level; break;
                case UpgradeStatType.MapPower:            mapPowerLevel = level; break;
                case UpgradeStatType.VialCount:           vialCountLevel = level; break;
                case UpgradeStatType.VialHealingPower:    vialHealingPowerLevel = level; break;

                case UpgradeStatType.PossessionDuration:  possessionDurationLevel = level; break;
                case UpgradeStatType.DealCapacity:        dealCapacityLevel = level; break;
                case UpgradeStatType.VisibilityCount:     visibilityCountLevel = level; break;
                case UpgradeStatType.VisibilityDuration:  visibilityDurationLevel = level; break;
                case UpgradeStatType.DeadSummonCharges:   deadSummonChargesLevel = level; break;
            }
        }

        /// <summary>
        /// Returns a baseline economy profile with 0 upgrades, ensuring looted items are always functional.
        /// </summary>
        public static PlayerEconomyProfile CreateBaseline()
        {
            return new PlayerEconomyProfile
            {
                credits = CurrencyConfig.DefaultStartingBalance,
                damageResistanceLevel = 0,
                weaponDamageLevel = 0,
                maskFilterLevel = 0,
                spiritualLevel = 0,
                mapPowerLevel = 0,
                vialCountLevel = 0,
                vialHealingPowerLevel = 0,
                possessionDurationLevel = 0,
                dealCapacityLevel = 0,
                visibilityCountLevel = 0,
                visibilityDurationLevel = 0,
                deadSummonChargesLevel = 0
            };
        }
    }
}
