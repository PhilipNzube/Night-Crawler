using UnityEngine;

/// <summary>
/// Connects any AudioSource in the game to the centralized GameSettingsManager audio channels
/// (Master, Music, SFX, UI, Environment/Ambient).
/// Ensures volume updates dynamically whenever audio settings sliders change.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioChannelBinding : MonoBehaviour
{
    public enum Channel
    {
        SFX,
        UI,
        Music,
        EnvironmentAmbient
    }

    [Tooltip("The audio setting channel that controls this AudioSource.")]
    public Channel channel = Channel.SFX;

    [Tooltip("Base volume of this AudioSource when channel volume is 1.0.")]
    [Range(0f, 1f)]
    public float baseVolume = 1.0f;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (baseVolume <= 0f && _audioSource != null && _audioSource.volume > 0f)
        {
            baseVolume = _audioSource.volume;
        }
    }

    private void Start()
    {
        ApplyVolume();
    }

    private void OnEnable()
    {
        ApplyVolume();
        GameSettingsManager.OnSFXVolumeChanged += HandleSFXChanged;
        GameSettingsManager.OnUIVolumeChanged += HandleUIChanged;
        GameSettingsManager.OnAmbientVolumeChanged += HandleAmbientChanged;
        GameSettingsManager.OnSettingsChanged += HandleSettingsChanged;
    }

    private void OnDisable()
    {
        GameSettingsManager.OnSFXVolumeChanged -= HandleSFXChanged;
        GameSettingsManager.OnUIVolumeChanged -= HandleUIChanged;
        GameSettingsManager.OnAmbientVolumeChanged -= HandleAmbientChanged;
        GameSettingsManager.OnSettingsChanged -= HandleSettingsChanged;
    }

    private void HandleSFXChanged(float vol) { if (channel == Channel.SFX) ApplyVolume(); }
    private void HandleUIChanged(float vol) { if (channel == Channel.UI) ApplyVolume(); }
    private void HandleAmbientChanged(float vol) { if (channel == Channel.EnvironmentAmbient) ApplyVolume(); }
    private void HandleSettingsChanged() { ApplyVolume(); }

    public void ApplyVolume()
    {
        if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) return;

        float channelVol = 1.0f;
        switch (channel)
        {
            case Channel.SFX:
                channelVol = GameSettingsManager.SFXVolume;
                break;
            case Channel.UI:
                channelVol = GameSettingsManager.UIVolumeVal;
                break;
            case Channel.Music:
                channelVol = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.musicVolume : 1.0f;
                break;
            case Channel.EnvironmentAmbient:
                channelVol = GameSettingsManager.AmbientVolume * GameSettingsManager.SFXVolume;
                break;
        }

        _audioSource.volume = Mathf.Clamp01(baseVolume * channelVol);
    }
}
