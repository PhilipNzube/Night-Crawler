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
        /// - The Miner's axe can only damage monsters normally; it cannot hurt other players unless the Miner accepted a deal.
        /// - The Miner's axe can always damage monsters, even after accepting a deal.
        /// - Deal-granted weapons from other sources cannot harm monsters (only investigators).
        /// </summary>
        public static bool CanDealDamage(WeaponOrigin origin, GameObject target, bool isMiner = false, bool hasAcceptedDeal = false, bool isMelee = false)
        {
            if (target == null) return false;

            bool isMonster = target.GetComponentInParent<MonsterAI>() != null ||
                             target.GetComponentInParent<MonsterController>() != null ||
                             target.name.ToLower().Contains("monster") ||
                             target.name.ToLower().Contains("creep") ||
                             (target.name.ToLower().Contains("demon") && !target.name.ToLower().Contains("girl"));

            bool isPlayer = !isMonster && (
                target.GetComponentInParent<global::InvestigatorCombatNet>() != null ||
                target.GetComponentInParent<global::HealthSystem>() != null ||
                target.GetComponentInParent<global::PlayerPossessableNet>() != null ||
                target.GetComponentInParent<global::NetworkPlayerName>() != null ||
                target.CompareTag("Player") ||
                (target.transform.root != null && target.transform.root.CompareTag("Player"))
            );

            // Case A: Target is a Monster
            if (isMonster)
            {
                // The Miner's axe ALWAYS harms monsters (even if that miner accepted a deal)
                if (isMiner && isMelee)
                {
                    return true;
                }

                // Non-miner deal-granted weapons CANNOT damage monsters!
                if (origin == WeaponOrigin.DealGranted)
                {
                    Debug.Log("[CombatEconomyFilter] Deal-granted weapon attack suppressed against monster! (Deals only harm investigators)");
                    return false;
                }

                return true;
            }

            // Case B: Target is another Player / Investigator
            if (isPlayer)
            {
                // The axe weapon wielded by the miner:
                // Cannot hurt another player, only monsters.
                // The only time that it would hurt a player is when that miner accepts a deal.
                if (isMiner && isMelee)
                {
                    if (!hasAcceptedDeal)
                    {
                        Debug.Log("[CombatEconomyFilter] Miner's axe attack suppressed against teammate! (Miner has not accepted a deal)");
                        return false;
                    }

                    // Miner accepted a deal -> Traitor axe CAN hurt other players!
                    return true;
                }

                // Non-miner weapon logic:
                // Only players with a deal weapon or active deal can hurt other players
                if (origin == WeaponOrigin.DealGranted || hasAcceptedDeal)
                {
                    return true;
                }

                // Friendly fire blocked for uncorrupted investigators
                return false;
            }

            return true;
        }

        /// <summary>
        /// Backward-compatible overload for standard weapon origin checks.
        /// </summary>
        public static bool CanDealDamage(WeaponOrigin origin, GameObject target)
        {
            return CanDealDamage(origin, target, isMiner: false, hasAcceptedDeal: false, isMelee: false);
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
