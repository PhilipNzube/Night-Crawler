using System;
using UnityEngine;

namespace NightCrawler.Economy
{
    /// <summary>
    /// Mathematical formulas and cost scaling for all persistent upgrades.
    /// Follows SOLID — Open/Closed Principle: formulas and costs are centralized in one engine.
    /// </summary>
    public static class UpgradeStatFormulas
    {
        // =========================================================================
        //  Cost Formulas (Exponential Scaling)
        // =========================================================================

        /// <summary>
        /// Base cost in Cinders for Level 1 upgrades.
        /// </summary>
        public static int GetBaseCost(UpgradeStatType stat)
        {
            switch (stat)
            {
                case UpgradeStatType.DamageResistance:     return 20;
                case UpgradeStatType.WeaponDamage:        return 25;
                case UpgradeStatType.MaskFilter:          return 20;
                case UpgradeStatType.SpiritualLevel:      return 30;
                case UpgradeStatType.MapPower:            return 15;
                case UpgradeStatType.VialCount:           return 25;
                case UpgradeStatType.VialHealingPower:    return 20;

                case UpgradeStatType.PossessionDuration:  return 35;
                case UpgradeStatType.DealCapacity:        return 30;
                case UpgradeStatType.VisibilityCount:     return 25;
                case UpgradeStatType.VisibilityDuration:  return 20;
                case UpgradeStatType.DeadSummonCharges:   return 30;
                default: return 20;
            }
        }

        /// <summary>
        /// Calculates the upgrade cost from currentLevel to (currentLevel + 1).
        /// Cost(L) = BaseCost * (1.55)^L.
        /// </summary>
        public static int CalculateUpgradeCost(UpgradeStatType stat, int currentLevel)
        {
            float baseCost = GetBaseCost(stat);
            float multiplier = Mathf.Pow(1.55f, currentLevel);
            return Mathf.RoundToInt(baseCost * multiplier);
        }

        // =========================================================================
        //  Investigator Stat Formulas
        // =========================================================================

        /// <summary>
        /// Damage resistance percentage (0.0 to 0.40 max).
        /// Each level grants +5% damage reduction, capped at 40%.
        /// </summary>
        public static float GetDamageResistanceFraction(int level)
        {
            return Mathf.Clamp(level * 0.05f, 0f, 0.40f);
        }

        /// <summary>
        /// Weapon damage multiplier (1.0 = normal, +8% per level).
        /// </summary>
        public static float GetWeaponDamageMultiplier(int level)
        {
            return 1.0f + (level * 0.08f);
        }

        /// <summary>
        /// Mask Filter: reduces poison tick damage over time (0.0 to 0.50 max).
        /// Does NOT increase lifespan or duration, preserving time pressure.
        /// Each level grants +6% reduction on tick damage, capped at 50%.
        /// </summary>
        public static float GetMaskPoisonDamageReduction(int level)
        {
            return Mathf.Clamp(level * 0.06f, 0f, 0.50f);
        }

        /// <summary>
        /// Spiritual Level: Exorcism channel speed multiplier (+10% speed per level).
        /// </summary>
        public static float GetExorcismSpeedMultiplier(int level)
        {
            return 1.0f + (level * 0.10f);
        }

        /// <summary>
        /// Map Power: ping/reveal radius in meters for clues/exorcism site.
        /// Baseline = 15m, +5m per level.
        /// </summary>
        public static float GetMapPingRadius(int level)
        {
            return 15f + (level * 5f);
        }

        /// <summary>
        /// Medic starting vial count. Baseline = 4, +1 per level.
        /// </summary>
        public static int GetStartingVialCount(int level)
        {
            return 4 + level;
        }

        /// <summary>
        /// Medic vial healing amount. Baseline = 50 HP, +6 HP per level.
        /// </summary>
        public static float GetVialHealAmount(int level)
        {
            return 50f + (level * 6f);
        }

        // =========================================================================
        //  The Girl (Vengeful Spirit) Persistent Upgrades
        // =========================================================================

        /// <summary>
        /// Total possession time pool in seconds across the match.
        /// Baseline = 120s (2 mins), +25s per level.
        /// </summary>
        public static float GetGirlPossessionTimePool(int level)
        {
            return 120f + (level * 25f);
        }

        /// <summary>
        /// Maximum number of deals the Girl can send per match.
        /// Baseline = 1 deal, +1 per level.
        /// </summary>
        public static int GetGirlDealCapacity(int level)
        {
            return 1 + level;
        }

        /// <summary>
        /// Maximum times the Girl can reveal/manifest herself per match.
        /// Baseline = 3 reveals, +1 per level.
        /// </summary>
        public static int GetGirlVisibilityCharges(int level)
        {
            return 3 + level;
        }

        /// <summary>
        /// Duration in seconds of each visibility/manifestation.
        /// Baseline = 8s, +2s per level.
        /// </summary>
        public static float GetGirlVisibilityDuration(int level)
        {
            return 8f + (level * 2f);
        }

        /// <summary>
        /// Girl dead / monster summon charges per match. Baseline = 2, +1 per level.
        /// </summary>
        public static int GetGirlDeadSummonCharges(int level)
        {
            return 2 + level;
        }

        // =========================================================================
        //  UI Helpers
        // =========================================================================

        public static string GetStatDisplayName(UpgradeStatType stat)
        {
            switch (stat)
            {
                case UpgradeStatType.DamageResistance:     return "Armor Resistance";
                case UpgradeStatType.WeaponDamage:        return "Weapon Mastery";
                case UpgradeStatType.MaskFilter:          return "Respirator Filter";
                case UpgradeStatType.SpiritualLevel:      return "Spiritual Attunement";
                case UpgradeStatType.MapPower:            return "Cartography Power";
                case UpgradeStatType.VialCount:           return "Vial Bandolier";
                case UpgradeStatType.VialHealingPower:    return "Healing Potency";

                case UpgradeStatType.PossessionDuration:  return "Possession Pool";
                case UpgradeStatType.DealCapacity:        return "Dark Deal Capacity";
                case UpgradeStatType.VisibilityCount:     return "Manifest Charges";
                case UpgradeStatType.VisibilityDuration:  return "Manifest Duration";
                case UpgradeStatType.DeadSummonCharges:   return "Necrotic Summons";
                default: return stat.ToString();
            }
        }

        public static string GetStatEffectDescription(UpgradeStatType stat, int level)
        {
            switch (stat)
            {
                case UpgradeStatType.DamageResistance:
                    return $"-{GetDamageResistanceFraction(level) * 100f:0}% Physical Damage";
                case UpgradeStatType.WeaponDamage:
                    return $"+{(GetWeaponDamageMultiplier(level) - 1.0f) * 100f:0}% Weapon Damage";
                case UpgradeStatType.MaskFilter:
                    return $"-{GetMaskPoisonDamageReduction(level) * 100f:0}% Poison Damage Per Tick";
                case UpgradeStatType.SpiritualLevel:
                    return $"+{(GetExorcismSpeedMultiplier(level) - 1.0f) * 100f:0}% Exorcism Speed";
                case UpgradeStatType.MapPower:
                    return $"{GetMapPingRadius(level):0}m Clue Ping Radius";
                case UpgradeStatType.VialCount:
                    return $"{GetStartingVialCount(level)} Starting Vials";
                case UpgradeStatType.VialHealingPower:
                    return $"{GetVialHealAmount(level):0} HP Restored / Vial";

                case UpgradeStatType.PossessionDuration:
                    return $"{GetGirlPossessionTimePool(level):0}s Possession Time Bank";
                case UpgradeStatType.DealCapacity:
                    return $"{GetGirlDealCapacity(level)} Dark Deals / Match";
                case UpgradeStatType.VisibilityCount:
                    return $"{GetGirlVisibilityCharges(level)} Manifestations / Match";
                case UpgradeStatType.VisibilityDuration:
                    return $"{GetGirlVisibilityDuration(level):0}s Manifest Duration";
                case UpgradeStatType.DeadSummonCharges:
                    return $"{GetGirlDeadSummonCharges(level)} Monsters Can Be Risen / Match";
                default: return string.Empty;
            }
        }
    }
}
