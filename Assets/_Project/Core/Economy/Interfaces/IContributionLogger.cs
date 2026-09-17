namespace NightCrawler.Economy
{
    /// <summary>
    /// SOLID — Interface Segregation: Allows abilities and systems (Medic vials, Priest exorcism,
    /// Miner combat, Adventurer map) to log contributions without tightly coupling to MatchEconomyManager.
    /// </summary>
    public interface IContributionLogger
    {
        void LogHeal(ulong healerClientId);
        void LogExorcismComplete(ulong priestClientId);
        void LogMonsterKill(ulong killerClientId);
        void LogMapClueDiscovered(ulong explorerClientId);
    }
}
