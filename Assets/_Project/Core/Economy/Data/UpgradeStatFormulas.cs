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
        /// Base cost in Credits for Level 2 upgrades (upgrading from starting Level 1).
        /// Balanced for a healthy, rewarding economy without player frustration.
        /// </summary>
        public static int GetBaseCost(UpgradeStatType stat)
        {
            switch (stat)
            {
                case UpgradeStatType.DamageResistance:     return 16;
                case UpgradeStatType.WeaponDamage:        return 20;
                case UpgradeStatType.MaskFilter:          return 14;
                case UpgradeStatType.SpiritualLevel:      return 18;
                case UpgradeStatType.MapPower:            return 12;
                case UpgradeStatType.VialCount:           return 18;
                case UpgradeStatType.VialHealingPower:    return 15;

                case UpgradeStatType.PossessionDuration:  return 22;
                case UpgradeStatType.DealCapacity:        return 20;
                case UpgradeStatType.VisibilityCount:
                case UpgradeStatType.VisibilityDuration:  return 16;
                case UpgradeStatType.DeadSummonCharges:   return 22;
                default: return 16;
            }
        }

        /// <summary>
        /// Calculates the upgrade cost from currentLevel to (currentLevel + 1).
        /// Uses a fair, motivating progression curve that prevents player burnout:
        /// - Level 1 -> 2: ~12-22 Credits (Immediate rewarding milestone with 60 starting credits)
        /// - Level 2 -> 3: ~26-48 Credits (Earnable in 1-2 good matches)
        /// - Level 3 -> 4: ~52-96 Credits (Mid-game specialization milestone)
        /// - Level 4 -> 5: ~90-165 Credits (Prestigious apex capstone, completely achievable)
        /// </summary>
        public static int CalculateUpgradeCost(UpgradeStatType stat, int currentLevel)
        {
            if (currentLevel >= 5) return 0;

            float baseCost = GetBaseCost(stat);
            switch (currentLevel)
            {
                case 0:
                case 1:
                    return Mathf.RoundToInt(baseCost);
                case 2:
                    return Mathf.RoundToInt(baseCost * 2.2f);
                case 3:
                    return Mathf.RoundToInt(baseCost * 4.4f);
                case 4:
                    return Mathf.RoundToInt(baseCost * 7.5f);
                default:
                    return Mathf.RoundToInt(baseCost * 8f);
            }
        }

        // =========================================================================
        //  Investigator Stat Formulas
        // =========================================================================

        /// <summary>
        /// Damage resistance percentage (0.10 to 0.35 max).
        /// Level 1 baseline = 10%, scaling up to 35% at Level 5.
        /// </summary>
        public static float GetDamageResistanceFraction(int level)
        {
            if (level <= 0) return 0.10f;
            return Mathf.Clamp(0.05f + (level * 0.06f), 0.10f, 0.35f);
        }

        /// <summary>
        /// Weapon damage multiplier.
        /// Level 1 baseline = 1.10 (+10%), scaling up to +45% at Level 5.
        /// </summary>
        public static float GetWeaponDamageMultiplier(int level)
        {
            if (level <= 0) return 1.10f;
            return 1.02f + (level * 0.08f);
        }

        /// <summary>
        /// Mask Filter: reduces poison tick damage over time (0.12 to 0.45 max).
        /// Level 1 baseline = 12% reduction, scaling to 45% at Level 5.
        /// </summary>
        public static float GetMaskPoisonDamageReduction(int level)
        {
            if (level <= 0) return 0.12f;
            return Mathf.Clamp(0.04f + (level * 0.08f), 0.12f, 0.45f);
        }

        /// <summary>
        /// Spiritual Level: Exorcism channel speed multiplier.
        /// Level 1 baseline = +12% speed, scaling to +55% at Level 5.
        /// </summary>
        public static float GetExorcismSpeedMultiplier(int level)
        {
            if (level <= 0) return 1.12f;
            return 1.02f + (level * 0.10f);
        }

        /// <summary>
        /// Map Power: ping/reveal radius in meters for clues/exorcism site.
        /// Level 1 baseline = 20m, scaling to 40m at Level 5.
        /// </summary>
        public static float GetMapPingRadius(int level)
        {
            if (level <= 0) return 20f;
            return 15f + (level * 5f);
        }

        /// <summary>
        /// Medic starting vial count. Level 1 baseline = 4, +1 per level up to 8.
        /// </summary>
        public static int GetStartingVialCount(int level)
        {
            if (level <= 0) return 4;
            return 3 + level;
        }

        /// <summary>
        /// Medic vial healing amount. Level 1 baseline = 55 HP, +10 HP per level up to 95 HP.
        /// </summary>
        public static float GetVialHealAmount(int level)
        {
            if (level <= 0) return 55f;
            return 45f + (level * 10f);
        }

        // =========================================================================
        //  The Girl (Wraith) Persistent Upgrades
        // =========================================================================

        /// <summary>
        /// Total possession time pool in seconds across the match.
        /// Level 1 baseline = 120s (2 mins), +25s per level up to 220s.
        /// </summary>
        public static float GetGirlPossessionTimePool(int level)
        {
            if (level <= 0) return 120f;
            return 95f + (level * 25f);
        }

        /// <summary>
        /// Maximum number of deals the Girl can send per match.
        /// Level 1 baseline = 1 deal, scaling to 5 at Level 5.
        /// </summary>
        public static int GetGirlDealCapacity(int level)
        {
            if (level <= 0) return 1;
            return level;
        }

        /// <summary>
        /// Maximum times the Girl can reveal/manifest herself per match (deprecated, now time bank driven).
        /// </summary>
        public static int GetGirlVisibilityCharges(int level)
        {
            return 1;
        }

        /// <summary>
        /// Total match time bank (in seconds) the Girl is permitted to remain physically manifested and visible.
        /// Level 1 baseline = 45s, scaling up to 105s at Level 5.
        /// </summary>
        public static float GetGirlVisibilityDuration(int level)
        {
            if (level <= 0) return 45f;
            return 30f + (level * 15f);
        }

        /// <summary>
        /// Girl dead / monster summon charges per match.
        /// Level 1 baseline = 2 Undead.
        /// Level 2 = 3 Undead.
        /// Level 3 = 4 Monsters (Includes Berserker Unlock).
        /// Level 4 = 6 Monsters (Includes Berserker Unlock).
        /// Level 5 (Max) = 8 Monsters (6 Undead + 2 Berserkers).
        /// </summary>
        public static int GetGirlDeadSummonCharges(int level)
        {
            if (level >= 5) return 8;
            if (level >= 4) return 6;
            if (level >= 3) return 4;
            if (level <= 1) return 2;
            return 3;
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
                case UpgradeStatType.VisibilityCount:
                case UpgradeStatType.VisibilityDuration:  return "Manifestation Pool";
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
                    return $"{GetGirlVisibilityDuration(level):0}s Manifestation Time Bank";
                case UpgradeStatType.VisibilityDuration:
                    return $"{GetGirlVisibilityDuration(level):0}s Manifestation Time Bank";
                case UpgradeStatType.DeadSummonCharges:
                    if (level >= 5) return $"{GetGirlDeadSummonCharges(level)} Monsters (6 Undead + 2 Berserkers)";
                    if (level >= 3) return $"{GetGirlDeadSummonCharges(level)} Monsters (Includes Berserker Unlock)";
                    return $"{GetGirlDeadSummonCharges(level)} Undead Monsters / Match";
                default: return string.Empty;
            }
        }
    }
}
