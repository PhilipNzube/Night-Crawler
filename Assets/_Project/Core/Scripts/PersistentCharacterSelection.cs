using UnityEngine;

/// <summary>
/// SOLID — SRP: Manages persistent storage of the player's selected character index
/// across scene transitions (e.g. from Lobby/Select scene into Squad display or GameScene).
/// </summary>
public static class PersistentCharacterSelection
{
    private const string PREF_KEY_CHAR_INDEX = "NightCrawler_SelectedCharacterIndex";

    /// <summary>
    /// Gets the saved character index. Returns 0 if none saved yet.
    /// </summary>
    public static int GetSelectedCharacterIndex()
    {
        return PlayerPrefs.GetInt(PREF_KEY_CHAR_INDEX, 0);
    }

    /// <summary>
    /// Saves the selected character index to PlayerPrefs so it persists across scenes.
    /// </summary>
    public static void SetSelectedCharacterIndex(int index)
    {
        PlayerPrefs.SetInt(PREF_KEY_CHAR_INDEX, index);
        PlayerPrefs.Save();
        Debug.Log($"[PersistentCharacterSelection] Saved selected character index: {index}");
    }
}
