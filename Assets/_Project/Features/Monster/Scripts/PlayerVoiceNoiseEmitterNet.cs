using UnityEngine;
using Unity.Netcode;
using NightCrawler.Monsters;

/// <summary>
/// Monitors the local player's microphone loudness in real-time.
/// When the player speaks or talks too loudly, emits acoustic noise events into the cave.
/// Nearby monsters hear the voice via MonsterPerception and stalk toward the player's position.
/// Supports both native Unity Microphone sampling and Vivox voice chat.
/// </summary>
public class PlayerVoiceNoiseEmitterNet : NetworkBehaviour
{
    [Header("Microphone Detection")]
    [Tooltip("Enable microphone volume monitoring to alert monsters.")]
    public bool enableVoiceDetection = true;

    [Tooltip("Minimum RMS volume to consider speech (whispering). Below this is treated as background silence.")]
    [Range(0.005f, 0.1f)]
    public float whisperThreshold = 0.02f;

    [Tooltip("RMS volume for normal conversational speech.")]
    [Range(0.02f, 0.2f)]
    public float talkThreshold = 0.05f;

    [Tooltip("RMS volume for loud talking, shouting, or screaming in fear.")]
    [Range(0.1f, 0.5f)]
    public float shoutThreshold = 0.18f;

    [Header("Acoustic Noise Radii (Metres)")]
    [Tooltip("Noise propagation radius when whispering.")]
    public float whisperNoiseRadius = 4.0f;

    [Tooltip("Noise propagation radius when speaking normally.")]
    public float talkNoiseRadius = 14.0f;

    [Tooltip("Noise propagation radius when talking loudly or screaming.")]
    public float shoutNoiseRadius = 28.0f;

    [Header("Emission Cadence")]
    [Tooltip("Minimum interval between consecutive noise pulses to prevent network flooding.")]
    public float noisePulseInterval = 0.45f;

    [Header("Debug & Calibration")]
    [Tooltip("Shows current real-time microphone volume in the Inspector.")]
    [SerializeField] private float currentMicVolume = 0f;

    [Tooltip("Log speech loudness triggers to the Unity console.")]
    public bool logVoiceEvents = false;

    // Internal Mic Recording State
    private AudioClip _micClip;
    private string _micDeviceName;
    private const int SampleRate = 16000;
    private const int SampleWindow = 256;
    private readonly float[] _audioSamples = new float[SampleWindow];
    private float _nextPulseTimer = 0f;
    private bool _isMicInitialized = false;

    public float CurrentMicVolume => currentMicVolume;

    public override void OnNetworkSpawn()
    {
        // Only the local player reads their physical microphone hardware
        if (!IsOwner) return;

        if (enableVoiceDetection)
        {
            InitializeMicrophone();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            StopMicrophone();
        }
    }

    private void InitializeMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[PlayerVoiceNoiseEmitterNet] No microphone hardware detected.");
            return;
        }

        _micDeviceName = Microphone.devices[0];
        _micClip = Microphone.Start(_micDeviceName, true, 1, SampleRate);
        _isMicInitialized = true;
    }

    private void StopMicrophone()
    {
        if (_isMicInitialized && !string.IsNullOrEmpty(_micDeviceName))
        {
            if (Microphone.IsRecording(_micDeviceName))
            {
                Microphone.End(_micDeviceName);
            }
            _isMicInitialized = false;
        }
    }

    private void Update()
    {
        if (!IsOwner || !enableVoiceDetection || !_isMicInitialized) return;

        // The Wraith cannot speak, and possessed victims have their vocal cords suppressed
        if (IsPlayerMuted())
        {
            currentMicVolume = 0f;
            return;
        }

        currentMicVolume = CalculateCurrentRmsVolume();

        if (_nextPulseTimer > 0f)
        {
            _nextPulseTimer -= Time.deltaTime;
        }

        // Check if voice loudness exceeds acoustic thresholds
        if (currentMicVolume >= whisperThreshold && _nextPulseTimer <= 0f)
        {
            float noiseRadius;
            string volumeLevel;

            if (currentMicVolume >= shoutThreshold)
            {
                noiseRadius = shoutNoiseRadius;
                volumeLevel = "LOUD TALKING / SCREAM";
            }
            else if (currentMicVolume >= talkThreshold)
            {
                noiseRadius = talkNoiseRadius;
                volumeLevel = "NORMAL TALKING";
            }
            else
            {
                noiseRadius = whisperNoiseRadius;
                volumeLevel = "WHISPERING";
            }

            _nextPulseTimer = noisePulseInterval;

            if (logVoiceEvents)
            {
                Debug.Log($"🎙️ [Voice Noise] {volumeLevel} (RMS: {currentMicVolume:F3}) -> Emitting {noiseRadius}m acoustic radius!");
            }

            // Transmit noise event to the server
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                EmitVoiceNoiseServerRpc(transform.position, noiseRadius);
            }
            else
            {
                // Local / offline fallback
                MonsterPerception.EmitNoise(transform.position, noiseRadius);
            }
        }
    }

    private float CalculateCurrentRmsVolume()
    {
        if (_micClip == null) return 0f;

        int micPosition = Microphone.GetPosition(_micDeviceName) - SampleWindow;
        if (micPosition < 0) return 0f;

        _micClip.GetData(_audioSamples, micPosition);

        float sum = 0f;
        for (int i = 0; i < SampleWindow; i++)
        {
            sum += _audioSamples[i] * _audioSamples[i];
        }

        return Mathf.Sqrt(sum / SampleWindow);
    }

    private bool IsPlayerMuted()
    {
        // 1. Wraith / Girl player is completely silent in the physical realm
        if (MonsterAI.IsGirl(transform.root != null ? transform.root : transform)) return true;

        // 2. Possessed players are muted
        if (TryGetComponent<PlayerPossessableNet>(out var pNet) && pNet.isPossessed.Value) return true;

        return false;
    }

    [ServerRpc]
    private void EmitVoiceNoiseServerRpc(Vector3 playerPosition, float noiseRadius)
    {
        // Broadcasts noise to all monsters on the authoritative server
        MonsterPerception.EmitNoise(playerPosition, noiseRadius);
    }
}
