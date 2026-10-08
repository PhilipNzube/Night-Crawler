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
    /// Centralized Currency Configuration.
    /// Change CurrencyName and CurrencySymbol here to change the currency across the entire game and UI!
    /// </summary>
    public static class CurrencyConfig
    {
        // -----------------------------------------------------------------
        //  CHANGE YOUR CURRENCY NAME & SYMBOL HERE:
        // -----------------------------------------------------------------
        public const string CurrencyName = "Cinders";
        public const string CurrencyPlural = "Cinders";
        public const string CurrencySymbol = "C";

        // Economic Constants
        public const int DefaultStartingBalance = 60; // Enough for starting matches & early upgrades
        public const int MinimumStake = 2;
        public const float MaxStakeCapPercentage = 0.60f; // Max 60% of balance (no all-in)
        public const int GirlLossPenalty = 25; // Penalty deducted from Girl on loss and redistributed
        public const int TraitorPenalty = 15; // Penalty deducted from traitor if Girl loses

        // Formatting Helpers
        public static string Format(int amount) => $"{amount} {CurrencySymbol}";
        public static string FormatFull(int amount) => $"{amount} {CurrencyName}";
        public static string FormatBalance(int balance) => $"{balance} {CurrencyPlural}";

        /// <summary>
        /// Maximum allowed stake (strictly capped at 60% of total balance, no all-in).
        /// </summary>
        public static int GetMaxStake(int balance) => Mathf.FloorToInt(balance * MaxStakeCapPercentage);

        /// <summary>
        /// Returns true if 60% of the total Cinders amount can be used to place at least the minimum stake (2 Cinders).
        /// When false, the player cannot stake and emergency stipend countdown activates!
        /// </summary>
        public static bool CanMeetMinimumStake(int balance)
        {
            if (balance < MinimumStake) return false;
            return GetMaxStake(balance) >= MinimumStake;
        }
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
        /// Guaranteed minimum is 1 so beginners always have positive starting baseline stats.
        /// </summary>
        public int GetLevel(UpgradeStatType stat)
        {
            int val = 1;
            switch (stat)
            {
                case UpgradeStatType.DamageResistance:     val = damageResistanceLevel; break;
                case UpgradeStatType.WeaponDamage:        val = weaponDamageLevel; break;
                case UpgradeStatType.MaskFilter:          val = maskFilterLevel; break;
                case UpgradeStatType.SpiritualLevel:      val = spiritualLevel; break;
                case UpgradeStatType.MapPower:            val = mapPowerLevel; break;
                case UpgradeStatType.VialCount:           val = vialCountLevel; break;
                case UpgradeStatType.VialHealingPower:    val = vialHealingPowerLevel; break;

                case UpgradeStatType.PossessionDuration:  val = possessionDurationLevel; break;
                case UpgradeStatType.DealCapacity:        val = dealCapacityLevel; break;
                case UpgradeStatType.VisibilityCount:
                case UpgradeStatType.VisibilityDuration:  val = visibilityDurationLevel; break;
                case UpgradeStatType.DeadSummonCharges:   val = deadSummonChargesLevel; break;
                default: val = 1; break;
            }
            return Mathf.Max(1, val);
        }

        /// <summary>
        /// Sets the upgrade level for any stat type.
        /// </summary>
        public void SetLevel(UpgradeStatType stat, int level)
        {
            level = Mathf.Max(1, level);
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
                case UpgradeStatType.VisibilityCount:
                case UpgradeStatType.VisibilityDuration:
                    visibilityDurationLevel = level;
                    visibilityCountLevel = level;
                    break;
                case UpgradeStatType.DeadSummonCharges:   deadSummonChargesLevel = level; break;
            }
        }

        /// <summary>
        /// Returns a baseline economy profile with starting level 1 on all stats, ensuring beginners have functional starting stats.
        /// </summary>
        public static PlayerEconomyProfile CreateBaseline()
        {
            return new PlayerEconomyProfile
            {
                credits = CurrencyConfig.DefaultStartingBalance,
                damageResistanceLevel = 1,
                weaponDamageLevel = 1,
                maskFilterLevel = 1,
                spiritualLevel = 1,
                mapPowerLevel = 1,
                vialCountLevel = 1,
                vialHealingPowerLevel = 1,
                possessionDurationLevel = 1,
                dealCapacityLevel = 1,
                visibilityCountLevel = 1,
                visibilityDurationLevel = 1,
                deadSummonChargesLevel = 1
            };
        }
    }
}
