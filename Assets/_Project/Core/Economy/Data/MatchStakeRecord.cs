using System;

namespace NightCrawler.Economy
{
    /// <summary>
    /// Record tracking an individual player's staking, role status, and in-match contributions.
    /// Used by MatchEconomyManager on the server to resolve pot winnings and bonuses.
    /// </summary>
    [Serializable]
    public class MatchStakeRecord
    {
        public ulong clientId;
        public string playerName = string.Empty;
        public int stakedCredits = 0;
        public bool hasConfirmedStake = false;
        public bool isGirl = false;
        public bool hasAcceptedDeal = false;
        public bool hasAbandoned = false;

        // Contribution metrics for win bonus calculation
        public int healsPerformed = 0;
        public bool exorcismCompleted = false;
        public int monstersKilled = 0;
        public int mapCluesRevealed = 0;

        // Backward-compatible property alias
        public int monstersKilledBeforeExorcism => monstersKilled;

        public MatchStakeRecord(ulong clientId, string playerName, int stakedCredits, bool isGirl)
        {
            this.clientId = clientId;
            this.playerName = playerName;
            this.stakedCredits = stakedCredits;
            this.isGirl = isGirl;
        }
    }

    /// <summary>
    /// Breakdown of a player's earnings and deductions at the end of a match.
    /// Sent via RPC so clients can display their personal financial breakdown.
    /// </summary>
    [Serializable]
    public struct MatchPayoutSummary
    {
        public bool won;
        public int stakedAmount;
        public int potWinnings;
        public int contributionBonus;
        public int penaltyDeduction;
        public int netPayout;
        public int newBalance;
        public string bonusDetails;
    }
}
