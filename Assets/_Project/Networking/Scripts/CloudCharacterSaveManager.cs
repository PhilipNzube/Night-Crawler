using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using NightCrawler.Economy;

/// <summary>
/// Serializable data model representing a player's profile, character selection state,
/// and persistent credit economy / upgrade stats.
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

    // Embedded Persistent Economy Profile (Credits + Persistent Upgrades)
    public PlayerEconomyProfile economy = new PlayerEconomyProfile();
}

/// <summary>
/// SOLID — SRP: Manages saving and loading player profile and persistent economy data.
/// Supports both Unity Gaming Services Cloud Save (when online/authenticated)
/// and automatic local PlayerPrefs fallback (for instant boot and offline/dev use).
/// </summary>
public class CloudCharacterSaveManager : MonoBehaviour
{
    public static CloudCharacterSaveManager Instance { get; private set; }

    private const string LOCAL_SAVE_KEY = "NightCrawler_LocalPlayerProfile";
    private const string CLOUD_PROFILE_KEY = "PlayerProfile";

    public PlayerProfileData CurrentProfile { get; private set; } = new PlayerProfileData();

    public static event Action<PlayerProfileData> OnProfileLoaded;
    public static event Action<int> OnCreditsChanged;

    public int CurrentCredits => CurrentProfile?.economy?.credits ?? 0;

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

        // 1. Instant local load on boot
        LoadFromLocalPlayerPrefs();
    }

    private void Start()
    {
        // 2. Automatically listen to UGS authentication to fetch cloud data seamlessly
        if (AuthenticationService.Instance != null)
        {
            AuthenticationService.Instance.SignedIn += HandleSignedIn;
            if (AuthenticationService.Instance.IsSignedIn)
            {
                _ = LoadProfileAsync();
            }
        }
    }

    private void OnDestroy()
    {
        if (AuthenticationService.Instance != null)
        {
            AuthenticationService.Instance.SignedIn -= HandleSignedIn;
        }
    }

    private void HandleSignedIn()
    {
        Debug.Log("[CloudSaveManager] Authentication completed. Loading latest cloud profile...");
        _ = LoadProfileAsync();
    }

    // =========================================================================
    //  Economy Operations
    // =========================================================================

    /// <summary>
    /// Checks if the player can afford an amount of credits.
    /// </summary>
    public bool CanAfford(int amount)
    {
        if (CurrentProfile == null || CurrentProfile.economy == null) return false;
        return CurrentProfile.economy.credits >= amount;
    }

    /// <summary>
    /// Attempts to deduct credits from the persistent balance and saves.
    /// </summary>
    public bool TryDeductCredits(int amount)
    {
        if (amount <= 0) return true;
        if (!CanAfford(amount)) return false;

        CurrentProfile.economy.credits -= amount;
        OnCreditsChanged?.Invoke(CurrentProfile.economy.credits);
        _ = SaveProfileAsync(CurrentProfile);
        return true;
    }

    /// <summary>
    /// Adds credits (e.g. match winnings, stake return, bonuses) to the persistent balance and saves.
    /// </summary>
    public void AddCredits(int amount)
    {
        if (amount <= 0 || CurrentProfile == null || CurrentProfile.economy == null) return;

        CurrentProfile.economy.credits += amount;
        OnCreditsChanged?.Invoke(CurrentProfile.economy.credits);
        _ = SaveProfileAsync(CurrentProfile);
    }

    /// <summary>
    /// Gets the current upgrade level for any investigator or girl stat.
    /// </summary>
    public int GetUpgradeLevel(UpgradeStatType stat)
    {
        if (CurrentProfile == null || CurrentProfile.economy == null) return 0;
        return CurrentProfile.economy.GetLevel(stat);
    }

    /// <summary>
    /// Purchases the next level for a specific upgrade stat if affordable.
    /// </summary>
    public bool TryPurchaseUpgrade(UpgradeStatType stat)
    {
        if (CurrentProfile == null || CurrentProfile.economy == null) return false;

        int currentLvl = CurrentProfile.economy.GetLevel(stat);
        int cost = UpgradeStatFormulas.CalculateUpgradeCost(stat, currentLvl);

        if (!TryDeductCredits(cost))
        {
            Debug.LogWarning($"[CloudSaveManager] Cannot afford upgrade for {stat}. Required: {cost}, Current: {CurrentProfile.economy.credits}");
            return false;
        }

        CurrentProfile.economy.SetLevel(stat, currentLvl + 1);
        _ = SaveProfileAsync(CurrentProfile);
        Debug.Log($"[CloudSaveManager] Upgraded {stat} to Level {currentLvl + 1} for {cost} Cinders.");
        return true;
    }

    // =========================================================================
    //  Cloud & Local Persistence
    // =========================================================================

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
                Debug.Log($"[CloudSaveManager] Profile synced to UGS Cloud Save for player: {profile.playerName} (Credits: {profile.economy.credits})");
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
                        var loaded = JsonUtility.FromJson<PlayerProfileData>(json);
                        if (loaded != null)
                        {
                            CurrentProfile = loaded;
                            if (CurrentProfile.economy == null) CurrentProfile.economy = new PlayerEconomyProfile();
                            SaveToLocalPlayerPrefs(CurrentProfile); // Update local cache

                            // Synchronize player name if present in cloud
                            if (!string.IsNullOrWhiteSpace(CurrentProfile.playerName))
                            {
                                PlayerNameManager.SetPlayerNameSilently(CurrentProfile.playerName);
                            }

                            OnProfileLoaded?.Invoke(CurrentProfile);
                            OnCreditsChanged?.Invoke(CurrentProfile.economy.credits);
                            Debug.Log($"[CloudSaveManager] Profile loaded from UGS Cloud Save: {CurrentProfile.playerName} | Credits: {CurrentProfile.economy.credits}");
                            return CurrentProfile;
                        }
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
                if (CurrentProfile.economy == null) CurrentProfile.economy = new PlayerEconomyProfile();
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

        if (!string.IsNullOrWhiteSpace(CurrentProfile.playerName))
        {
            PlayerNameManager.SetPlayerNameSilently(CurrentProfile.playerName);
        }

        OnProfileLoaded?.Invoke(CurrentProfile);
        OnCreditsChanged?.Invoke(CurrentProfile.economy.credits);
    }
}
