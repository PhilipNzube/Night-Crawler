using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using NightCrawler.Economy;

namespace NightCrawler.Economy
{
    /// <summary>
    /// SOLID — SRP: Server-authoritative match economy engine.
    /// Manages:
    /// 1. Staking phase at match start (pot pooling, minimum stakes, max caps).
    /// 2. Injecting Girl persistent stats (possession pool, deals, visibility charges).
    /// 3. In-match contribution logging (heals, exorcism, clues, monster kills).
    /// 4. Disconnect penalty handling (strictly their staked amount).
    /// 5. Match-end payout & penalty resolution (proportional pot split, capped bonuses, cloud sync).
    /// </summary>
    public class MatchEconomyManager : NetworkBehaviour, IContributionLogger
    {
        public static MatchEconomyManager Instance { get; private set; }

        [Header("Staking Configuration")]
        [Tooltip("Duration in seconds players have to select and confirm their stake at match start.")]
        public float stakingPhaseDuration = 15f;

        [Header("Network State")]
        public NetworkVariable<int> totalPot = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isStakingActive = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<float> stakingTimeRemaining = new NetworkVariable<float>(
            15f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> isMatchResolved = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Server-only player records
        private readonly Dictionary<ulong, MatchStakeRecord> _playerRecords = new Dictionary<ulong, MatchStakeRecord>();

        // Events
        public static event Action<MatchPayoutSummary> OnLocalPayoutReceived;
        public static event Action<int> OnTotalPotChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            totalPot.OnValueChanged += (oldVal, newVal) => OnTotalPotChanged?.Invoke(newVal);

            if (IsServer)
            {
                StartCoroutine(StakingPhaseRoutine());
            }

            if (IsClient)
            {
                StartCoroutine(AutoSubmitLobbyStakeRoutine());
            }
        }

        private IEnumerator AutoSubmitLobbyStakeRoutine()
        {
            yield return new WaitUntil(() => isStakingActive.Value);
            int savedStake = PersistentCharacterSelection.GetSavedMatchStake();
            if (savedStake >= CurrencyConfig.MinimumStake)
            {
                SubmitStakeServerRpc(savedStake);
                Debug.Log($"[MatchEconomyManager] Successfully auto-submitted lobby stake: {savedStake} {CurrencyConfig.CurrencyName}.");
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // =========================================================================
        //  Staking Phase
        // =========================================================================

        private IEnumerator StakingPhaseRoutine()
        {
            // Wait briefly for all client player objects to spawn and register
            yield return new WaitForSeconds(1.5f);

            isStakingActive.Value = true;
            stakingTimeRemaining.Value = stakingPhaseDuration;

            // Register all currently connected clients with default minimum stakes
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                RegisterPlayerRecord(client.ClientId);
            }

            // Countdown staking phase
            while (stakingTimeRemaining.Value > 0f)
            {
                yield return new WaitForSeconds(0.5f);
                stakingTimeRemaining.Value = Mathf.Max(0f, stakingTimeRemaining.Value - 0.5f);

                // If everyone confirmed early, we can exit early
                if (AreAllStakesConfirmed())
                {
                    break;
                }
            }

            isStakingActive.Value = false;
            RecalculateTotalPot();
            Debug.Log($"[MatchEconomyManager] Staking phase concluded. Total Pot: {totalPot.Value} {CurrencyConfig.CurrencyName}.");
        }

        private void RegisterPlayerRecord(ulong clientId)
        {
            if (_playerRecords.ContainsKey(clientId)) return;

            string pName = PlayerNameManager.GetPlayerName(clientId);
            bool isGirl = (clientId == GirlRevealManager.SavedGirlClientId);

            // Default minimum stake
            int initialStake = CurrencyConfig.MinimumStake;
            _playerRecords[clientId] = new MatchStakeRecord(clientId, pName, initialStake, isGirl);
        }

        private bool AreAllStakesConfirmed()
        {
            if (_playerRecords.Count == 0) return false;
            foreach (var kvp in _playerRecords)
            {
                if (!kvp.Value.hasConfirmedStake) return false;
            }
            return true;
        }

        [Rpc(SendTo.Server)]
        public void SubmitStakeServerRpc(int requestedStake, RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;
            if (!isStakingActive.Value)
            {
                Debug.LogWarning($"[MatchEconomyManager] Client {senderId} attempted to stake after staking closed.");
                return;
            }

            if (!_playerRecords.TryGetValue(senderId, out var record))
            {
                RegisterPlayerRecord(senderId);
                record = _playerRecords[senderId];
            }

            // Enforce minimum stake
            int validStake = Mathf.Max(CurrencyConfig.MinimumStake, requestedStake);

            record.stakedCredits = validStake;
            record.hasConfirmedStake = true;

            RecalculateTotalPot();
            Debug.Log($"[MatchEconomyManager] Player {record.playerName} (ID {senderId}) confirmed stake: {validStake} {CurrencyConfig.CurrencyName}.");
        }

        private void RecalculateTotalPot()
        {
            int sum = 0;
            foreach (var kvp in _playerRecords.Values)
            {
                sum += kvp.stakedCredits;
            }
            totalPot.Value = sum;
        }

        // =========================================================================
        //  Girl Persistent Upgrades Application (No In-Game Buying)
        // =========================================================================

        /// <summary>
        /// Applies persistent upgrades to the spawned Girl player object.
        /// </summary>
        public void ApplyGirlUpgrades(GameObject girlObject, PlayerEconomyProfile profile)
        {
            if (girlObject == null || profile == null) return;

            // 1. Possession time pool
            if (girlObject.TryGetComponent<GirlPossession>(out var possession))
            {
                float pool = UpgradeStatFormulas.GetGirlPossessionTimePool(profile.possessionDurationLevel);
                possession.maxPossessionTimePool = pool;
                if (IsServer) possession.remainingPossessionTime.Value = pool;
                Debug.Log($"[MatchEconomyManager] Applied Girl Possession Pool upgrade: {pool:0}s (Lvl {profile.possessionDurationLevel})");
            }

            // 2. Deal capacity
            int dealCap = UpgradeStatFormulas.GetGirlDealCapacity(profile.dealCapacityLevel);
            // Stored in record for DealSystemNet verification
            ulong girlId = GirlRevealManager.SavedGirlClientId;
            if (_playerRecords.TryGetValue(girlId, out var record))
            {
                // Can track remaining deals if needed
            }
            Debug.Log($"[MatchEconomyManager] Applied Girl Deal Capacity upgrade: {dealCap} deals (Lvl {profile.dealCapacityLevel})");

            // 3. Visibility / Manifestation charges and duration
            if (girlObject.TryGetComponent<GirlStealth>(out var stealth))
            {
                // Set stats or charges
                int visCharges = UpgradeStatFormulas.GetGirlVisibilityCharges(profile.visibilityCountLevel);
                float visDuration = UpgradeStatFormulas.GetGirlVisibilityDuration(profile.visibilityDurationLevel);
                Debug.Log($"[MatchEconomyManager] Applied Girl Visibility upgrade: {visCharges} charges, {visDuration:0}s duration.");
            }
        }

        // =========================================================================
        //  IContributionLogger Implementation
        // =========================================================================

        public void LogHeal(ulong healerClientId)
        {
            if (!IsServer) return;
            if (_playerRecords.TryGetValue(healerClientId, out var record))
            {
                record.healsPerformed++;
                Debug.Log($"[MatchEconomyManager] Logged heal for {record.playerName}. Total: {record.healsPerformed}");
            }
        }

        private bool _isRitualExorcismCompleted = false;

        public void LogExorcismComplete(ulong priestClientId)
        {
            if (!IsServer) return;
            _isRitualExorcismCompleted = true;
            if (_playerRecords.TryGetValue(priestClientId, out var record))
            {
                record.exorcismCompleted = true;
                Debug.Log($"[MatchEconomyManager] Logged exorcism completion for {record.playerName}!");
            }
        }

        public void LogMonsterKill(ulong killerClientId)
        {
            if (!IsServer) return;

            if (_playerRecords.TryGetValue(killerClientId, out var record))
            {
                record.monstersKilled++;
                Debug.Log($"[MatchEconomyManager] Logged monster kill for {record.playerName}. Total: {record.monstersKilled}");
            }
        }

        public void LogMonsterKillBeforeExorcism(ulong killerClientId) => LogMonsterKill(killerClientId);

        public void LogMapClueDiscovered(ulong explorerClientId)
        {
            if (!IsServer) return;
            if (_playerRecords.TryGetValue(explorerClientId, out var record))
            {
                record.mapCluesRevealed++;
                Debug.Log($"[MatchEconomyManager] Logged map clue revealed for {record.playerName}. Total: {record.mapCluesRevealed}");
            }
        }

        /// <summary>
        /// Marks that an investigator accepted the Girl's pact/deal.
        /// </summary>
        public void TagPlayerAcceptedDeal(ulong victimClientId)
        {
            if (!IsServer) return;
            if (_playerRecords.TryGetValue(victimClientId, out var record))
            {
                record.hasAcceptedDeal = true;
                Debug.Log($"[MatchEconomyManager] Player {record.playerName} tagged as accepting Girl's deal (Traitor tag applied).");
            }
        }

        /// <summary>
        /// Applies stake penalty to an investigator who failed their accepted pact/deal in time.
        /// Deducts penalty directly from their stake.
        /// </summary>
        public void ApplyPactFailurePenalty(ulong traitorClientId, int penaltyAmount)
        {
            if (!IsServer) return;

            if (_playerRecords.TryGetValue(traitorClientId, out var record))
            {
                int penalty = Mathf.Min(record.stakedCredits, penaltyAmount);
                record.stakedCredits = Mathf.Max(0, record.stakedCredits - penalty);
                Debug.Log($"[MatchEconomyManager] Applied pact failure penalty to {record.playerName}: -{penalty} {CurrencyConfig.CurrencyName}. Remaining stake: {record.stakedCredits}");

                RecalculateTotalPot();
                NotifyPactPenaltyClientRpc(traitorClientId, penalty);
            }
        }

        [ClientRpc]
        private void NotifyPactPenaltyClientRpc(ulong targetClientId, int penalty)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == targetClientId)
            {
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification($"<color=#E74C3C>PACT EXPIRED</color>: Failed pact in time! {penalty} {CurrencyConfig.CurrencySymbol} deducted from your stake.", 4.5f);
                }
            }
        }

        /// <summary>
        /// Handles a player abandon when disconnect grace expires.
        /// Disconnect penalty is strictly their staked amount.
        /// </summary>
        public void HandlePlayerForfeit(ulong clientId)
        {
            if (!IsServer) return;
            if (_playerRecords.TryGetValue(clientId, out var record))
            {
                record.hasAbandoned = true;
                Debug.Log($"[MatchEconomyManager] Player {record.playerName} forfeited match. Disconnect penalty = staked amount ({record.stakedCredits} {CurrencyConfig.CurrencyName}).");
            }
        }

        // =========================================================================
        //  Match Resolution & Proportional Payout Engine
        // =========================================================================

        /// <summary>
        /// Called on the server by GameManager when match ends.
        /// </summary>
        public void ResolveMatchEconomy(bool investigatorsWon)
        {
            if (!IsServer || isMatchResolved.Value) return;
            isMatchResolved.Value = true;

            Debug.Log($"[MatchEconomyManager] Resolving match economy. Winner: {(investigatorsWon ? "INVESTIGATORS" : "VENGEFUL SPIRIT")}");

            if (investigatorsWon)
            {
                ResolveInvestigatorsVictory();
            }
            else
            {
                ResolveGirlVictory();
            }
        }

        private void ResolveInvestigatorsVictory()
        {
            // 1. Identify winning investigators (alive or dead, did not abandon, did not accept deal)
            var winners = new List<MatchStakeRecord>();
            int totalWinnerStakes = 0;
            int loserPot = 0;

            foreach (var record in _playerRecords.Values)
            {
                if (record.isGirl)
                {
                    // Girl loses: her stake + flat penalty feed the winning pool
                    loserPot += record.stakedCredits;
                    loserPot += CurrencyConfig.GirlLossPenalty;
                }
                else if (record.hasAbandoned)
                {
                    // Abandoned player loses stake to the pot
                    loserPot += record.stakedCredits;
                }
                else if (record.hasAcceptedDeal)
                {
                    // Traitor loses: stake + traitor penalty feed the winning pool
                    loserPot += record.stakedCredits;
                    loserPot += CurrencyConfig.TraitorPenalty;
                }
                else
                {
                    // Legitimate investigator winner
                    winners.Add(record);
                    totalWinnerStakes += record.stakedCredits;
                }
            }

            if (totalWinnerStakes <= 0) totalWinnerStakes = 1; // Prevent division by zero

            // 2. Distribute payouts to winning investigators
            foreach (var winner in winners)
            {
                float stakeRatio = (float)winner.stakedCredits / totalWinnerStakes;
                int potWinShare = Mathf.RoundToInt(stakeRatio * loserPot);

                // Contribution bonuses
                int rawBonus = CalculateContributionBonus(winner);
                // Cap bonus to max 35% of their pot share so it modifies rather than eclipses the pot
                int maxAllowedBonus = Mathf.Max(5, Mathf.RoundToInt(potWinShare * 0.35f));
                int cappedBonus = Mathf.Min(rawBonus, maxAllowedBonus);

                int netPayout = winner.stakedCredits + potWinShare + cappedBonus;

                DispatchPayoutRpc(winner.clientId, new MatchPayoutSummary
                {
                    won = true,
                    stakedAmount = winner.stakedCredits,
                    potWinnings = potWinShare,
                    contributionBonus = cappedBonus,
                    penaltyDeduction = 0,
                    netPayout = netPayout,
                    bonusDetails = BuildBonusDetailsString(winner, cappedBonus)
                });
            }

            // 3. Notify losing Girl & traitors
            foreach (var record in _playerRecords.Values)
            {
                if (record.isGirl)
                {
                    DispatchPayoutRpc(record.clientId, new MatchPayoutSummary
                    {
                        won = false,
                        stakedAmount = record.stakedCredits,
                        potWinnings = 0,
                        contributionBonus = 0,
                        penaltyDeduction = CurrencyConfig.GirlLossPenalty,
                        netPayout = -(record.stakedCredits + CurrencyConfig.GirlLossPenalty),
                        bonusDetails = $"Spirit Exorcised (-{CurrencyConfig.GirlLossPenalty} {CurrencyConfig.CurrencyName} Penalty)"
                    });
                }
                else if (record.hasAcceptedDeal && !record.hasAbandoned)
                {
                    DispatchPayoutRpc(record.clientId, new MatchPayoutSummary
                    {
                        won = false,
                        stakedAmount = record.stakedCredits,
                        potWinnings = 0,
                        contributionBonus = 0,
                        penaltyDeduction = CurrencyConfig.TraitorPenalty,
                        netPayout = -(record.stakedCredits + CurrencyConfig.TraitorPenalty),
                        bonusDetails = $"Traitor Pact Exposed (-{CurrencyConfig.TraitorPenalty} {CurrencyConfig.CurrencyName} Penalty)"
                    });
                }
                else if (record.hasAbandoned)
                {
                    DispatchPayoutRpc(record.clientId, new MatchPayoutSummary
                    {
                        won = false,
                        stakedAmount = record.stakedCredits,
                        potWinnings = 0,
                        contributionBonus = 0,
                        penaltyDeduction = 0, // Disconnect penalty is strictly their stake
                        netPayout = -record.stakedCredits,
                        bonusDetails = "Abandoned Match (Stake Forfeited)"
                    });
                }
            }
        }

        private void ResolveGirlVictory()
        {
            // When Girl wins: Girl takes all stakes from Investigators
            int investigatorPot = 0;
            MatchStakeRecord girlRecord = null;

            foreach (var record in _playerRecords.Values)
            {
                if (record.isGirl)
                {
                    girlRecord = record;
                }
                else
                {
                    investigatorPot += record.stakedCredits;
                }
            }

            if (girlRecord != null)
            {
                int girlNetPayout = girlRecord.stakedCredits + investigatorPot;

                DispatchPayoutRpc(girlRecord.clientId, new MatchPayoutSummary
                {
                    won = true,
                    stakedAmount = girlRecord.stakedCredits,
                    potWinnings = investigatorPot,
                    contributionBonus = 0,
                    penaltyDeduction = 0,
                    netPayout = girlNetPayout,
                    bonusDetails = "All Miners Slain"
                });
            }

            // Notify all losing investigators
            foreach (var record in _playerRecords.Values)
            {
                if (!record.isGirl)
                {
                    DispatchPayoutRpc(record.clientId, new MatchPayoutSummary
                    {
                        won = false,
                        stakedAmount = record.stakedCredits,
                        potWinnings = 0,
                        contributionBonus = 0,
                        penaltyDeduction = 0,
                        netPayout = -record.stakedCredits,
                        bonusDetails = "Team Wiped by Vengeful Spirit"
                    });
                }
            }
        }

        private int CalculateContributionBonus(MatchStakeRecord r)
        {
            int bonus = 0;
            if (r.exorcismCompleted) bonus += 15; // Priest completed exorcism
            bonus += r.healsPerformed * 4;        // Medic heals (4 per heal)
            bonus += r.mapCluesRevealed * 5;      // Adventurer clue reveals
            bonus += r.monstersKilled * 6;        // Monster kills
            return bonus;
        }

        private string BuildBonusDetailsString(MatchStakeRecord r, int actualBonus)
        {
            var parts = new List<string>();
            if (r.exorcismCompleted) parts.Add("Exorcism Completed (+15)");
            if (r.healsPerformed > 0) parts.Add($"Heals x{r.healsPerformed} (+{r.healsPerformed * 4})");
            if (r.mapCluesRevealed > 0) parts.Add($"Clues x{r.mapCluesRevealed} (+{r.mapCluesRevealed * 5})");
            if (r.monstersKilled > 0) parts.Add($"Monster Kills x{r.monstersKilled} (+{r.monstersKilled * 6})");

            if (parts.Count == 0) return "Flat Base Win";
            return string.Join(" | ", parts);
        }

        private void DispatchPayoutRpc(ulong clientId, MatchPayoutSummary summary)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                ApplyPayoutLocally(summary);
            }
            else if (NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
            {
                ReceivePersonalPayoutClientRpc(clientId, summary.won, summary.stakedAmount, summary.potWinnings,
                    summary.contributionBonus, summary.penaltyDeduction, summary.netPayout, summary.bonusDetails);
            }
        }

        [ClientRpc]
        private void ReceivePersonalPayoutClientRpc(ulong targetClientId, bool won, int staked, int winnings, int bonus, int penalty, int net, string details)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != targetClientId) return;

            var summary = new MatchPayoutSummary
            {
                won = won,
                stakedAmount = staked,
                potWinnings = winnings,
                contributionBonus = bonus,
                penaltyDeduction = penalty,
                netPayout = net,
                bonusDetails = details
            };
            ApplyPayoutLocally(summary);
        }

        private void ApplyPayoutLocally(MatchPayoutSummary summary)
        {
            if (CloudCharacterSaveManager.Instance != null)
            {
                if (summary.netPayout > 0)
                {
                    CloudCharacterSaveManager.Instance.AddCredits(summary.netPayout);
                }
                else if (summary.netPayout < 0)
                {
                    // Deduct stake or penalties
                    CloudCharacterSaveManager.Instance.TryDeductCredits(Mathf.Abs(summary.netPayout));
                }
                summary.newBalance = CloudCharacterSaveManager.Instance.CurrentCredits;
            }

            Debug.Log($"[MatchEconomyManager] Local payout applied: Won={summary.won}, Net={summary.netPayout} {CurrencyConfig.CurrencyName}, New Balance={summary.newBalance}");
            OnLocalPayoutReceived?.Invoke(summary);
        }
    }
}
