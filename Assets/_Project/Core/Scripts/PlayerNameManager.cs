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
    /// Returns true if the player has already saved a non-empty name.
    /// Use this to gate the lobby — require a name before allowing connection.
    /// </summary>
    public static bool HasSavedName()
    {
        return PlayerPrefs.HasKey(PREF_KEY) && !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PREF_KEY));
    }

    /// <summary>
    /// Gets the saved player name. Returns empty string if no name has been set yet.
    /// Check HasSavedName() first if you need to gate on a name being present.
    /// </summary>
    public static string GetPlayerName()
    {
        if (!HasSavedName()) return string.Empty;
        return PlayerPrefs.GetString(PREF_KEY);
    }

    /// <summary>
    /// Gets the synchronized network player name for a specific client ID,
    /// or falls back to local name / default identifier.
    /// </summary>
    public static string GetPlayerName(ulong clientId)
    {
        if (Unity.Netcode.NetworkManager.Singleton != null && 
            Unity.Netcode.NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                var netName = client.PlayerObject.GetComponent<NetworkPlayerName>();
                if (netName != null && !string.IsNullOrEmpty(netName.playerName.Value.ToString()))
                {
                    return netName.playerName.Value.ToString();
                }
            }
        }

        if (Unity.Netcode.NetworkManager.Singleton != null && clientId == Unity.Netcode.NetworkManager.Singleton.LocalClientId)
        {
            string localName = GetPlayerName();
            if (!string.IsNullOrEmpty(localName)) return localName;
        }

        return $"Player {clientId}";
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

    /// <summary>
    /// Clears the saved player name. Useful for testing.
    /// </summary>
    public static void ClearPlayerName()
    {
        PlayerPrefs.DeleteKey(PREF_KEY);
        PlayerPrefs.Save();
    }
}
