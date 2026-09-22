using UnityEngine;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Manages player profile name validation, sanitization (with full Emoji support),
/// and persistence (PlayerPrefs + CloudCharacterSaveManager sync).
///
/// Industry-standard game filtering:
/// - Allows emojis and unicode symbols (e.g. ⛏, 💀, 🔥, ⚡, 👻)
/// - Strips invisible/zero-width exploit characters and directional override spoofers
/// - Strips control characters and collapses whitespace
/// - Enforces length bounds (2 - 18 characters)
/// - Filters harmful profanity and slurs
/// </summary>
public static class PlayerNameManager
{
    private const string PREF_KEY = "NightCrawler_PlayerName";
    public static event Action<string> OnNameChanged;

    // Zero-width & invisible spoofing characters to block
    private static readonly HashSet<char> InvisibleChars = new HashSet<char>
    {
        '\u200B', // Zero width space
        '\u200C', // Zero width non-joiner
        '\u200E', // Left-to-right mark
        '\u200F', // Right-to-left mark
        '\u202A', // LTR embedding
        '\u202B', // RTL embedding
        '\u202C', // Pop directional formatting
        '\u202D', // LTR override
        '\u202E', // RTL override
        '\u2060', // Word joiner
        '\u2066', // LTR isolate
        '\u2067', // RTL isolate
        '\u2068', // First strong isolate
        '\u2069', // Pop directional isolate
        '\uFEFF', // Zero width no-break space (BOM)
        '\u00AD'  // Soft hyphen
    };

    // Standard profanity / hate word filter list (case-insensitive)
    private static readonly string[] ProhibitedWords = new string[]
    {
        "nigger", "nigga", "faggot", "fag", "kike", "spic", "chink", "cunt",
        "hitler", "nazi", "retard", "rape", "pedophile", "childporn"
    };

    /// <summary>
    /// Returns true if the player has already saved a non-empty, valid name.
    /// </summary>
    public static bool HasSavedName()
    {
        return PlayerPrefs.HasKey(PREF_KEY) && !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PREF_KEY));
    }

    /// <summary>
    /// Gets the saved player name. Returns empty string if no name has been set yet.
    /// </summary>
    public static string GetPlayerName()
    {
        if (!HasSavedName()) return string.Empty;
        return SanitizePlayerName(PlayerPrefs.GetString(PREF_KEY));
    }

    /// <summary>
    /// Gets the saved player name with an option to filter emojis.
    /// </summary>
    public static string GetPlayerName(bool allowEmojis)
    {
        if (!HasSavedName()) return string.Empty;
        return SanitizePlayerName(PlayerPrefs.GetString(PREF_KEY), allowEmojis);
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
    /// Validates a player name for length, visible content, and offensive terms.
    /// Returns true if valid, or false with a user-friendly error explanation.
    /// </summary>
    public static bool ValidatePlayerName(string rawName, out string sanitizedName, out string errorMessage, bool allowEmojis = true)
    {
        sanitizedName = SanitizePlayerName(rawName, allowEmojis);

        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            errorMessage = "Player name cannot be blank.";
            return false;
        }

        if (!allowEmojis && ContainsEmoji(rawName))
        {
            errorMessage = "Emojis are not allowed in player names.";
            return false;
        }

        if (sanitizedName.Length < 2)
        {
            errorMessage = "Player name must be at least 2 characters.";
            return false;
        }

        if (sanitizedName.Length > 18)
        {
            errorMessage = "Player name cannot exceed 18 characters.";
            return false;
        }

        // Profanity check
        string lower = sanitizedName.ToLowerInvariant();
        foreach (var word in ProhibitedWords)
        {
            if (lower.Contains(word))
            {
                errorMessage = "Player name contains prohibited language.";
                return false;
            }
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Checks whether the given string contains any emojis, surrogates, or pictorial symbols.
    /// (Maintained for backward compatibility; emojis are now fully supported and allowed!)
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
                i++;
            }
        }
        return false;
    }

    /// <summary>
    /// Sanitizes player name:
    /// - Strips invisible / zero-width characters and directional overrides
    /// - Strips ASCII control characters (\0..\x1F, \x7F..\x9F)
    /// - Normalizes consecutive spaces to a single space
    /// - Trims leading and trailing whitespace
    /// - Allows or strips emojis based on allowEmojis parameter
    /// </summary>
    public static string SanitizePlayerName(string text, bool allowEmojis = true)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Strip invisible / zero-width exploit characters
            if (InvisibleChars.Contains(c)) continue;

            // Strip control characters (tabs, newlines, null bytes, backspaces)
            if (char.IsControl(c)) continue;

            // Surrogate pairs (standard for modern emojis like 👻, ⛏, etc.)
            if (char.IsSurrogate(c))
            {
                if (char.IsSurrogatePair(text, i))
                {
                    int cp = char.ConvertToUtf32(text, i);
                    if (!allowEmojis && (IsEmojiCodePoint(cp) || true))
                    {
                        // Without emoji permission, strip the surrogate pair
                        i++; // skip second half of pair
                        continue;
                    }

                    if (allowEmojis)
                    {
                        sb.Append(text[i]);
                        sb.Append(text[i + 1]);
                    }
                    i++; // skip second half of pair
                    continue;
                }
                else
                {
                    // Orphaned single surrogate without pair -> discard
                    continue;
                }
            }

            int singleCp = (int)c;
            if (!allowEmojis && IsEmojiCodePoint(singleCp))
            {
                continue;
            }

            sb.Append(c);
        }

        // Collapse multiple whitespace characters into single space
        string cleaned = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();

        if (cleaned.Length > 18)
        {
            cleaned = cleaned.Substring(0, 18).Trim();
        }

        return cleaned;
    }

    /// <summary>
    /// Strips all emojis, surrogates, and pictorial symbols from the string.
    /// </summary>
    public static string StripEmojis(string text)
    {
        return SanitizePlayerName(text, allowEmojis: false);
    }

    public static bool IsEmojiCodePoint(int cp)
    {
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
    /// Saves a validated player name to PlayerPrefs and synchronizes with CloudCharacterSaveManager.
    /// </summary>
    public static void SetPlayerName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        string sanitized = SanitizePlayerName(newName);
        if (string.IsNullOrWhiteSpace(sanitized)) return;

        PlayerPrefs.SetString(PREF_KEY, sanitized);
        PlayerPrefs.Save();

        // Sync to CloudCharacterSaveManager profile
        if (CloudCharacterSaveManager.Instance != null && CloudCharacterSaveManager.Instance.CurrentProfile != null)
        {
            CloudCharacterSaveManager.Instance.CurrentProfile.playerName = sanitized;
            _ = CloudCharacterSaveManager.Instance.SaveProfileAsync(CloudCharacterSaveManager.Instance.CurrentProfile);
        }

        OnNameChanged?.Invoke(sanitized);
    }

    /// <summary>
    /// Sets the local player name without triggering another cloud save (used during incoming cloud loads).
    /// </summary>
    public static void SetPlayerNameSilently(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        string sanitized = SanitizePlayerName(name);
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
