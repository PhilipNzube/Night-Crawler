using System;
using UnityEngine;
using NightCrawler.Economy;

namespace NightCrawler.Economy
{
    /// <summary>
    /// SOLID — SRP: Authoritative combat economy filter.
    /// Enforces:
    /// 1. Weapon Restrictions: Deal-granted weapons deal 0 damage to monsters, but full damage to investigators.
    ///    Native weapons (Miner's axe) always damage monsters, even if that player accepted a deal.
    /// 2. Attacker Damage Scaling: Applies attacker's persistent Weapon Damage Level.
    /// 3. Defender Damage Resistance: Applies defender's persistent Damage Resistance Level.
    /// </summary>
    public static class CombatEconomyFilter
    {
        /// <summary>
        /// Evaluates whether an attack is permitted to deal damage to the target.
        /// Returns false if attacking a monster with a Deal-granted weapon.
        /// </summary>
        public static bool CanDealDamage(WeaponOrigin origin, GameObject target)
        {
            if (target == null) return false;

            bool isMonster = target.CompareTag("Monster") ||
                             target.name.ToLower().Contains("monster") ||
                             target.name.ToLower().Contains("creep") ||
                             target.name.ToLower().Contains("demon") && !target.name.ToLower().Contains("girl");

            // Deal-granted weapons CANNOT damage monsters!
            if (origin == WeaponOrigin.DealGranted && isMonster)
            {
                Debug.Log("[CombatEconomyFilter] Deal-granted weapon attack suppressed against monster! (Deals only harm investigators)");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Scales attack damage based on attacker's weapon mastery and defender's armor resistance.
        /// </summary>
        public static float CalculateEffectiveDamage(
            float baseDamage,
            int attackerWeaponLvl,
            int defenderResistanceLvl,
            bool isPhysicalAttack = true)
        {
            float dmg = baseDamage;

            // 1. Attacker weapon damage boost
            if (attackerWeaponLvl > 0)
            {
                dmg *= UpgradeStatFormulas.GetWeaponDamageMultiplier(attackerWeaponLvl);
            }

            // 2. Defender damage resistance (applies to physical/weapon attacks)
            if (isPhysicalAttack && defenderResistanceLvl > 0)
            {
                float resistance = UpgradeStatFormulas.GetDamageResistanceFraction(defenderResistanceLvl);
                dmg *= (1.0f - resistance);
            }

            return Mathf.Max(1f, dmg);
        }
    }
}
