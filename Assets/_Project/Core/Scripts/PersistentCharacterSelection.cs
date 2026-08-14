using UnityEngine;

/// <summary>
/// SOLID — SRP: Manages persistent storage of the player's selected character index
/// and assigned role (Vengeful Spirit vs Investigator) across scene transitions.
/// </summary>
public static class PersistentCharacterSelection
{
    private const string PREF_KEY_CHAR_INDEX = "NightCrawler_SelectedCharacterIndex";
    private const string PREF_KEY_IS_GIRL     = "NightCrawler_IsVengefulSpirit";

    /// <summary>Gets the saved character index. Returns 0 if none saved yet.</summary>
    public static int GetSelectedCharacterIndex()
    {
        return PlayerPrefs.GetInt(PREF_KEY_CHAR_INDEX, 0);
    }

    /// <summary>Saves the selected character index to PlayerPrefs so it persists across scenes.</summary>
    public static void SetSelectedCharacterIndex(int index)
    {
        PlayerPrefs.SetInt(PREF_KEY_CHAR_INDEX, index);
        PlayerPrefs.Save();
        Debug.Log($"[PersistentCharacterSelection] Saved selected character index: {index}");
    }

    /// <summary>Returns true if the local player was assigned as the Vengeful Spirit (Girl).</summary>
    public static bool IsVengefulSpirit()
    {
        return PlayerPrefs.GetInt(PREF_KEY_IS_GIRL, 0) == 1;
    }

    /// <summary>Saves the player's assigned role (Vengeful Spirit vs Investigator).</summary>
    public static void SetIsVengefulSpirit(bool isGirl)
    {
        PlayerPrefs.SetInt(PREF_KEY_IS_GIRL, isGirl ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[PersistentCharacterSelection] Saved role isVengefulSpirit: {isGirl}");
    }
}
