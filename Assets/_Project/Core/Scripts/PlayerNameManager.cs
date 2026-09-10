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
        return SanitizePlayerName(PlayerPrefs.GetString(PREF_KEY));
    }

    /// <summary>
    /// Gets the synchronized network player name for a specific client ID,
    /// or falls back to local name / default identifier.
    /// </summary>
    public static string GetPlayerName(ulong clientId)
    {
        string registered = GirlRevealManager.GetRegisteredPlayerName(clientId);
        if (!string.IsNullOrEmpty(registered) && !registered.StartsWith("Player "))
        {
            return registered;
        }

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
    /// Checks whether the given string contains any emojis, surrogates, or pictorial symbols.
    /// </summary>
    public static bool ContainsEmoji(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogate(text[i])) return true;

            int cp = char.ConvertToUtf32(text, i);
            if (IsEmojiCodePoint(cp)) return true;

            if (char.IsSurrogatePair(text, i))
            {
                i++; // Skip low surrogate
            }
        }
        return false;
    }

    /// <summary>
    /// Removes all emojis, surrogate characters, and pictorial symbols from the string.
    /// </summary>
    public static string SanitizePlayerName(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogate(text[i]))
            {
                if (char.IsSurrogatePair(text, i)) i++;
                continue;
            }

            int cp = char.ConvertToUtf32(text, i);
            if (IsEmojiCodePoint(cp)) continue;

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static bool IsEmojiCodePoint(int cp)
    {
        // Unicode emoji & symbol blocks
        if (cp >= 0x1F600 && cp <= 0x1F64F) return true; // Emoticons
        if (cp >= 0x1F300 && cp <= 0x1F5FF) return true; // Misc Symbols and Pictographs
        if (cp >= 0x1F680 && cp <= 0x1F6FF) return true; // Transport and Map
        if (cp >= 0x1F700 && cp <= 0x1F77F) return true; // Alchemical Symbols
        if (cp >= 0x1F780 && cp <= 0x1F7FF) return true; // Geometric Shapes Extended
        if (cp >= 0x1F800 && cp <= 0x1F8FF) return true; // Supplemental Arrows-C
        if (cp >= 0x1F900 && cp <= 0x1F9FF) return true; // Supplemental Symbols and Pictographs
        if (cp >= 0x1FA00 && cp <= 0x1FA6F) return true; // Chess Symbols
        if (cp >= 0x1FA70 && cp <= 0x1FAFF) return true; // Symbols and Pictographs Extended-A
        if (cp >= 0x2600 && cp <= 0x26FF) return true;   // Misc Symbols (e.g. ☠, ⛏, ⚠, ⚡)
        if (cp >= 0x2700 && cp <= 0x27BF) return true;   // Dingbats (e.g. ✂, ✈, ✉, ✌)
        if (cp >= 0xFE00 && cp <= 0xFE0F) return true;   // Variation Selectors
        if (cp == 0x200D) return true;                   // Zero Width Joiner
        if (cp >= 0xD800 && cp <= 0xDFFF) return true;   // Surrogates

        return false;
    }

    /// <summary>
    /// Saves a new player name to PlayerPrefs, sanitizing any emojis or excessive characters.
    /// </summary>
    public static void SetPlayerName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        string sanitized = SanitizePlayerName(newName).Trim();
        if (string.IsNullOrWhiteSpace(sanitized)) return;

        if (sanitized.Length > 18) sanitized = sanitized.Substring(0, 18);

        PlayerPrefs.SetString(PREF_KEY, sanitized);
        PlayerPrefs.Save();

        OnNameChanged?.Invoke(sanitized);
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
