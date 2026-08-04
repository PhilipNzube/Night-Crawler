using UnityEngine;
using System;

/// <summary>
/// SOLID — SRP: Manages local player profile name persistence (PlayerPrefs).
/// </summary>
public static class PlayerNameManager
{
    private const string PREF_KEY = "NightCrawler_PlayerName";
    public static event Action<string> OnNameChanged;

    /// <summary>
    /// Gets the saved player name, or generates a default if none exists.
    /// </summary>
    public static string GetPlayerName()
    {
        if (!PlayerPrefs.HasKey(PREF_KEY) || string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PREF_KEY)))
        {
            string defaultName = "Investigator_" + UnityEngine.Random.Range(100, 999);
            PlayerPrefs.SetString(PREF_KEY, defaultName);
            PlayerPrefs.Save();
        }
        return PlayerPrefs.GetString(PREF_KEY);
    }

    /// <summary>
    /// Saves a new player name to PlayerPrefs.
    /// </summary>
    public static void SetPlayerName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        string trimmed = newName.Trim();
        if (trimmed.Length > 18) trimmed = trimmed.Substring(0, 18);

        PlayerPrefs.SetString(PREF_KEY, trimmed);
        PlayerPrefs.Save();

        OnNameChanged?.Invoke(trimmed);
    }
}
