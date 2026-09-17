namespace NightCrawler.Economy
{
    /// <summary>
    /// Distinguishes role-native weapons (e.g. Miner's pickaxe) from deal-granted weapons.
    /// Deal-granted weapons are restricted: cannot damage monsters, only investigators / ritual targets.
    /// Miner's axe is always usable on monsters even if they later take a deal.
    /// </summary>
    public enum WeaponOrigin
    {
        Native,         // Role-native weapon (e.g. Miner's axe). Full capabilities.
        DealGranted     // Granted via Girl pact. Can only damage investigators, not monsters.
    }

    /// <summary>
    /// Contract to query origin and properties of a weapon during combat resolution.
    /// </summary>
    public interface IWeaponOriginProvider
    {
        WeaponOrigin Origin { get; }
        bool IsDealWeapon { get; }
    }
}
