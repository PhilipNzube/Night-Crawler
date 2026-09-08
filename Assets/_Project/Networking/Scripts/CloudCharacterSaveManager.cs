using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;

/// <summary>
/// Serializable data model representing a player's profile and character selection state.
/// </summary>
[System.Serializable]
public class PlayerProfileData
{
    public string playerName = "Investigator";
    public int selectedCharacterIndex = 0;
    public InvestigatorProfession profession = InvestigatorProfession.MineWorker;
    public int playerLevel = 1;
    public int matchesSurvived = 0;
    public string lastSavedUtc = string.Empty;
}

/// <summary>
/// SOLID — SRP: Manages saving and loading player profile and character data.
/// Supports both Unity Gaming Services Cloud Save (when online/authenticated)
/// and automatic local PlayerPrefs fallback (for offline/dev use).
/// </summary>
public class CloudCharacterSaveManager : MonoBehaviour
{
    public static CloudCharacterSaveManager Instance { get; private set; }

    private const string LOCAL_SAVE_KEY = "NightCrawler_LocalPlayerProfile";
    private const string CLOUD_PROFILE_KEY = "PlayerProfile";

    public PlayerProfileData CurrentProfile { get; private set; } = new PlayerProfileData();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);

        // Load cached local profile immediately on boot
        LoadFromLocalPlayerPrefs();
    }

    /// <summary>
    /// Saves the current profile to Cloud Save if authenticated, and always updates local PlayerPrefs.
    /// </summary>
    public async Task<bool> SaveProfileAsync(PlayerProfileData profile)
    {
        if (profile == null) return false;

        profile.lastSavedUtc = DateTime.UtcNow.ToString("o");
        CurrentProfile = profile;

        // 1. Always save to local PlayerPrefs as an instant offline fallback
        SaveToLocalPlayerPrefs(profile);

        // 2. If authenticated with UGS, persist to Cloud Save
        if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
        {
            try
            {
                string json = JsonUtility.ToJson(profile);
                var data = new Dictionary<string, object>
                {
                    { CLOUD_PROFILE_KEY, json }
                };

                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
                Debug.Log($"[CloudSaveManager] Profile synced to UGS Cloud Save for player: {profile.playerName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CloudSaveManager] Cloud Save failed (relying on local save): {ex.Message}");
                return false;
            }
        }
        else
        {
            Debug.Log("[CloudSaveManager] Player not signed into UGS. Profile saved to local storage only.");
            return true;
        }
    }

    /// <summary>
    /// Loads the player profile. Attempts Cloud Save first if authenticated, otherwise uses local PlayerPrefs.
    /// </summary>
    public async Task<PlayerProfileData> LoadProfileAsync()
    {
        // 1. If signed into UGS, attempt to fetch from Cloud Save
        if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
        {
            try
            {
                var keys = new HashSet<string> { CLOUD_PROFILE_KEY };
                var results = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

                if (results.TryGetValue(CLOUD_PROFILE_KEY, out var item))
                {
                    string json = item.Value.GetAs<string>();
                    if (!string.IsNullOrEmpty(json))
                    {
                        CurrentProfile = JsonUtility.FromJson<PlayerProfileData>(json);
                        SaveToLocalPlayerPrefs(CurrentProfile); // Update local cache
                        Debug.Log($"[CloudSaveManager] Profile loaded from UGS Cloud Save: {CurrentProfile.playerName}");
                        return CurrentProfile;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CloudSaveManager] Failed to load from Cloud Save (falling back to local): {ex.Message}");
            }
        }

        // 2. Fallback to local PlayerPrefs
        LoadFromLocalPlayerPrefs();
        return CurrentProfile;
    }

    private void SaveToLocalPlayerPrefs(PlayerProfileData profile)
    {
        try
        {
            string json = JsonUtility.ToJson(profile);
            PlayerPrefs.SetString(LOCAL_SAVE_KEY, json);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CloudSaveManager] Failed to save profile locally: {ex.Message}");
        }
    }

    private void LoadFromLocalPlayerPrefs()
    {
        if (PlayerPrefs.HasKey(LOCAL_SAVE_KEY))
        {
            try
            {
                string json = PlayerPrefs.GetString(LOCAL_SAVE_KEY);
                CurrentProfile = JsonUtility.FromJson<PlayerProfileData>(json) ?? new PlayerProfileData();
            }
            catch
            {
                CurrentProfile = new PlayerProfileData();
            }
        }
        else
        {
            CurrentProfile = new PlayerProfileData();
            if (PlayerNameManager.HasSavedName())
            {
                CurrentProfile.playerName = PlayerNameManager.GetPlayerName();
            }
        }
    }
}
