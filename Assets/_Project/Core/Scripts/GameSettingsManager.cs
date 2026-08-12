using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SOLID — SRP: Owns the runtime state, application, and PlayerPrefs persistence
/// of all PC Game Settings (Audio, Video/Display, Graphics Quality, Controls).
/// 
/// Automatically loads and applies saved settings on game startup.
/// </summary>
public class GameSettingsManager : MonoBehaviour
{
    // =========================================================================
    //  Singleton & Global Access
    // =========================================================================
    public static GameSettingsManager Instance { get; private set; }

    // Events
    public static event Action OnSettingsChanged;
    public static event Action<float> OnSFXVolumeChanged;

    // =========================================================================
    //  PlayerPrefs Keys
    // =========================================================================
    private const string PREF_MASTER_VOL    = "NC_Setting_MasterVolume";
    private const string PREF_MUSIC_VOL     = "NC_Setting_MusicVolume";
    private const string PREF_SFX_VOL       = "NC_Setting_SFXVolume";
    
    private const string PREF_RES_WIDTH     = "NC_Setting_ResWidth";
    private const string PREF_RES_HEIGHT    = "NC_Setting_ResHeight";
    private const string PREF_RES_REFRESH   = "NC_Setting_ResRefresh";
    private const string PREF_DISPLAY_MODE  = "NC_Setting_DisplayMode";
    private const string PREF_VSYNC         = "NC_Setting_VSync";
    private const string PREF_TARGET_FPS    = "NC_Setting_TargetFPS";

    private const string PREF_QUALITY_LEVEL = "NC_Setting_QualityLevel";
    private const string PREF_SHADOW_QUAL   = "NC_Setting_ShadowQuality";
    private const string PREF_AA_LEVEL      = "NC_Setting_AntiAliasing";
    private const string PREF_TEXTURE_QUAL  = "NC_Setting_TextureQuality";
    private const string PREF_ANISOTROPIC   = "NC_Setting_Anisotropic";

    private const string PREF_SENSITIVITY   = "NC_Setting_Sensitivity";
    private const string PREF_INVERT_Y      = "NC_Setting_InvertY";

    // =========================================================================
    //  Public Settings State
    // =========================================================================
    [Header("Audio Settings")]
    public float masterVolume = 1.0f;
    public float musicVolume  = 0.8f;
    public float sfxVolume    = 1.0f;

    [Header("Video / Display Settings")]
    public int resolutionWidth  = 1920;
    public int resolutionHeight = 1080;
    public int refreshRate      = 60;
    public int displayMode      = 0; // 0 = Fullscreen Windowed, 1 = Exclusive Fullscreen, 2 = Windowed
    public int vSync            = 1; // 0 = Off, 1 = On
    public int targetFPS        = -1; // -1 = Unlimited, 30, 60, 120, 144, 240

    [Header("Graphics Quality Settings")]
    public int qualityLevel         = 2; // Medium/High default depending on system
    public int shadowQuality        = 2; // 0 = Disabled, 1 = Hard Only, 2 = All
    public int antiAliasing         = 4; // 0 = Off, 2 = 2x, 4 = 4x, 8 = 8x
    public int textureQuality        = 0; // 0 = Full, 1 = Half, 2 = Quarter, 3 = Eighth
    public int anisotropicFiltering = 2; // 0 = Disabled, 1 = Enable, 2 = ForceEnable

    [Header("Controls Settings")]
    public float mouseSensitivity = 1.0f;
    public bool  invertYAxis      = false;

    // Static Accessors for convenience
    public static float MouseSens  => Instance != null ? Instance.mouseSensitivity : 1.0f;
    public static bool  InvertY    => Instance != null ? Instance.invertYAxis : false;
    public static float SFXVolume  => Instance != null ? Instance.sfxVolume : 1.0f;

    // =========================================================================
    //  Unity Lifecycle & Initialization
    // =========================================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        if (Instance == null)
        {
            GameObject managerObj = new GameObject("[GameSettingsManager]");
            managerObj.AddComponent<GameSettingsManager>();
            DontDestroyOnLoad(managerObj);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSettings();
        ApplySettings();
    }

    // =========================================================================
    //  Load & Save
    // =========================================================================
    public void LoadSettings()
    {
        // Audio
        masterVolume = PlayerPrefs.GetFloat(PREF_MASTER_VOL, 1.0f);
        musicVolume  = PlayerPrefs.GetFloat(PREF_MUSIC_VOL, 0.8f);
        sfxVolume    = PlayerPrefs.GetFloat(PREF_SFX_VOL, 1.0f);

        // Display defaults
        Resolution defaultRes = Screen.currentResolution;
        resolutionWidth  = PlayerPrefs.GetInt(PREF_RES_WIDTH, defaultRes.width);
        resolutionHeight = PlayerPrefs.GetInt(PREF_RES_HEIGHT, defaultRes.height);
        refreshRate      = PlayerPrefs.GetInt(PREF_RES_REFRESH, (int)defaultRes.refreshRateRatio.value > 0 ? (int)defaultRes.refreshRateRatio.value : 60);
        displayMode      = PlayerPrefs.GetInt(PREF_DISPLAY_MODE, 0);
        vSync            = PlayerPrefs.GetInt(PREF_VSYNC, 1);
        targetFPS        = PlayerPrefs.GetInt(PREF_TARGET_FPS, -1);

        // Graphics
        int maxQuality = QualitySettings.names.Length - 1;
        qualityLevel         = Mathf.Clamp(PlayerPrefs.GetInt(PREF_QUALITY_LEVEL, Mathf.Min(2, maxQuality)), 0, maxQuality);
        shadowQuality        = PlayerPrefs.GetInt(PREF_SHADOW_QUAL, 2);
        antiAliasing         = PlayerPrefs.GetInt(PREF_AA_LEVEL, 4);
        textureQuality        = PlayerPrefs.GetInt(PREF_TEXTURE_QUAL, 0);
        anisotropicFiltering = PlayerPrefs.GetInt(PREF_ANISOTROPIC, 2);

        // Controls
        mouseSensitivity = PlayerPrefs.GetFloat(PREF_SENSITIVITY, 1.0f);
        invertYAxis      = PlayerPrefs.GetInt(PREF_INVERT_Y, 0) == 1;
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(PREF_MASTER_VOL, masterVolume);
        PlayerPrefs.SetFloat(PREF_MUSIC_VOL, musicVolume);
        PlayerPrefs.SetFloat(PREF_SFX_VOL, sfxVolume);

        PlayerPrefs.SetInt(PREF_RES_WIDTH, resolutionWidth);
        PlayerPrefs.SetInt(PREF_RES_HEIGHT, resolutionHeight);
        PlayerPrefs.SetInt(PREF_RES_REFRESH, refreshRate);
        PlayerPrefs.SetInt(PREF_DISPLAY_MODE, displayMode);
        PlayerPrefs.SetInt(PREF_VSYNC, vSync);
        PlayerPrefs.SetInt(PREF_TARGET_FPS, targetFPS);

        PlayerPrefs.SetInt(PREF_QUALITY_LEVEL, qualityLevel);
        PlayerPrefs.SetInt(PREF_SHADOW_QUAL, shadowQuality);
        PlayerPrefs.SetInt(PREF_AA_LEVEL, antiAliasing);
        PlayerPrefs.SetInt(PREF_TEXTURE_QUAL, textureQuality);
        PlayerPrefs.SetInt(PREF_ANISOTROPIC, anisotropicFiltering);

        PlayerPrefs.SetFloat(PREF_SENSITIVITY, mouseSensitivity);
        PlayerPrefs.SetInt(PREF_INVERT_Y, invertYAxis ? 1 : 0);

        PlayerPrefs.Save();
        Debug.Log("[GameSettingsManager] Settings successfully saved to PlayerPrefs.");
    }

    // =========================================================================
    //  Apply Settings to Unity Engine
    // =========================================================================
    public void ApplySettings()
    {
        // 1. Audio
        AudioListener.volume = Mathf.Clamp01(masterVolume);
        if (GameMusicManager.Instance != null)
        {
            GameMusicManager.Instance.bgMaxVolume = Mathf.Clamp01(musicVolume);
        }
        OnSFXVolumeChanged?.Invoke(sfxVolume);

        // 2. Video / Display Mode & Resolution
        FullScreenMode windowMode = FullScreenMode.FullScreenWindow;
        switch (displayMode)
        {
            case 0: windowMode = FullScreenMode.FullScreenWindow; break;
            case 1: windowMode = FullScreenMode.ExclusiveFullScreen; break;
            case 2: windowMode = FullScreenMode.Windowed; break;
        }

        RefreshRate rr = new RefreshRate { numerator = (uint)Mathf.Max(30, refreshRate), denominator = 1 };
        Screen.SetResolution(resolutionWidth, resolutionHeight, windowMode, rr);

        QualitySettings.vSyncCount = Mathf.Clamp(vSync, 0, 1);
        Application.targetFrameRate = targetFPS;

        // 3. Graphics Quality
        if (qualityLevel >= 0 && qualityLevel < QualitySettings.names.Length)
        {
            QualitySettings.SetQualityLevel(qualityLevel, applyExpensiveChanges: true);
        }

        switch (shadowQuality)
        {
            case 0: QualitySettings.shadows = ShadowQuality.Disable; break;
            case 1: QualitySettings.shadows = ShadowQuality.HardOnly; break;
            case 2: QualitySettings.shadows = ShadowQuality.All; break;
        }

        QualitySettings.antiAliasing = antiAliasing;
        QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(textureQuality, 0, 3);

        switch (anisotropicFiltering)
        {
            case 0: QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable; break;
            case 1: QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable; break;
            case 2: QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable; break;
        }

        OnSettingsChanged?.Invoke();
        Debug.Log($"[GameSettingsManager] Applied settings: Res={resolutionWidth}x{resolutionHeight}@{refreshRate}Hz, Mode={windowMode}, Quality={QualitySettings.names[qualityLevel]}, VSync={vSync}");
    }

    // =========================================================================
    //  Defaults Reset
    // =========================================================================
    public void ResetToDefaults()
    {
        masterVolume = 1.0f;
        musicVolume  = 0.8f;
        sfxVolume    = 1.0f;

        Resolution currentRes = Screen.currentResolution;
        resolutionWidth  = currentRes.width;
        resolutionHeight = currentRes.height;
        refreshRate      = (int)currentRes.refreshRateRatio.value > 0 ? (int)currentRes.refreshRateRatio.value : 60;
        displayMode      = 0;
        vSync            = 1;
        targetFPS        = -1;

        qualityLevel         = Mathf.Min(2, QualitySettings.names.Length - 1);
        shadowQuality        = 2;
        antiAliasing         = 4;
        textureQuality        = 0;
        anisotropicFiltering = 2;

        mouseSensitivity = 1.0f;
        invertYAxis      = false;

        SaveSettings();
        ApplySettings();
    }
}
