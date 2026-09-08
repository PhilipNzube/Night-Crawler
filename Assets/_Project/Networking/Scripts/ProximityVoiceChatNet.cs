using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Real-time 3D spatial proximity voice communication over Netcode for GameObjects.
/// Captures microphone audio on the local player and streams compressed PCM packets to players within
/// a configurable 3D acoustic radius. Heard only in true 3D spatial sound from the speaker's location.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class ProximityVoiceChatNet : NetworkBehaviour
{
    [Header("Proximity Voice Settings")]
    [Tooltip("Maximum distance in meters where player voice can be heard.")]
    public float voiceRadius = 20.0f;

    [Tooltip("Minimum distance for full volume before attenuation.")]
    public float minDistance = 2.0f;

    [Tooltip("Sample rate for microphone recording (16000Hz standard for voice clarity and low bandwidth).")]
    public int sampleRate = 16000;

    [Tooltip("Push to talk key (e.g. V). If pushToTalk is false, mic transmits continuously.")]
    public Key pushToTalkKey = Key.V;

    [Tooltip("If true, requires holding down pushToTalkKey to speak.")]
    public bool pushToTalk = false;

    private AudioSource _3dAudioSource;
    private AudioClip _micClip;
    private string _micDeviceName;
    private int _lastSamplePos = 0;
    private bool _isTransmitting = false;

    private const int CHUNK_SIZE = 800; // 50ms at 16kHz
    private readonly float[] _sampleBuffer = new float[CHUNK_SIZE];

    public bool IsTransmitting => _isTransmitting;

    private void Awake()
    {
        _3dAudioSource = GetComponent<AudioSource>();
        _3dAudioSource.spatialBlend = 1.0f; // 100% 3D spatialized
        _3dAudioSource.rolloffMode = AudioRolloffMode.Linear;
        _3dAudioSource.minDistance = minDistance;
        _3dAudioSource.maxDistance = voiceRadius;
        _3dAudioSource.playOnAwake = false;
        _3dAudioSource.loop = false;
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Owner does not hear their own voice spatialized from their head
            _3dAudioSource.mute = true;
            StartMicrophoneCapture();
        }
        else
        {
            _3dAudioSource.mute = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            StopMicrophoneCapture();
        }
    }

    private void StartMicrophoneCapture()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[ProximityVoiceChatNet] No microphone input devices found on system.");
            return;
        }

        _micDeviceName = Microphone.devices[0];
        _micClip = Microphone.Start(_micDeviceName, loop: true, lengthSec: 10, sampleRate);
        _lastSamplePos = 0;
    }

    private void StopMicrophoneCapture()
    {
        if (!string.IsNullOrEmpty(_micDeviceName) && Microphone.IsRecording(_micDeviceName))
        {
            Microphone.End(_micDeviceName);
        }
    }

    private void Update()
    {
        if (!IsOwner || _micClip == null) return;

        bool isMicKeyHeld = (Keyboard.current != null && Keyboard.current[pushToTalkKey].isPressed);
        bool shouldTransmit = !pushToTalk || isMicKeyHeld;

        if (shouldTransmit)
        {
            ProcessAndSendMicData();
            _isTransmitting = true;
        }
        else
        {
            _isTransmitting = false;
        }
    }

    private void ProcessAndSendMicData()
    {
        int currentPos = Microphone.GetPosition(_micDeviceName);
        if (currentPos < 0) return;

        int samplesAvailable = (currentPos - _lastSamplePos + _micClip.samples) % _micClip.samples;

        while (samplesAvailable >= CHUNK_SIZE)
        {
            _micClip.GetData(_sampleBuffer, _lastSamplePos);
            _lastSamplePos = (_lastSamplePos + CHUNK_SIZE) % _micClip.samples;
            samplesAvailable -= CHUNK_SIZE;

            // Compress float samples [-1..1] into signed 8-bit bytes to minimize network bandwidth
            byte[] compressed = CompressAudioChunk(_sampleBuffer);

            SendVoiceChunkServerRpc(compressed);
        }
    }

    private byte[] CompressAudioChunk(float[] samples)
    {
        byte[] bytes = new byte[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            // Normalize float [-1, 1] to byte [0..255]
            int val = Mathf.RoundToInt((samples[i] + 1f) * 127.5f);
            bytes[i] = (byte)Mathf.Clamp(val, 0, 255);
        }
        return bytes;
    }

    private float[] DecompressAudioChunk(byte[] bytes)
    {
        float[] samples = new float[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            samples[i] = (bytes[i] / 127.5f) - 1.0f;
        }
        return samples;
    }

    [Rpc(SendTo.Server)]
    private void SendVoiceChunkServerRpc(byte[] voiceData)
    {
        Vector3 speakerPosition = transform.position;
        ulong senderId = OwnerClientId;

        // Relay voice only to clients within voiceRadius
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == senderId) continue;
            if (client.PlayerObject == null) continue;

            float dist = Vector3.Distance(speakerPosition, client.PlayerObject.transform.position);
            if (dist <= voiceRadius)
            {
                PlayVoiceChunkClientRpc(voiceData, RpcTarget.Single(client.ClientId, RpcTargetUse.Temp));
            }
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void PlayVoiceChunkClientRpc(byte[] voiceData, RpcParams rpcParams = default)
    {
        if (IsOwner) return;

        float[] samples = DecompressAudioChunk(voiceData);
        AudioClip clip = AudioClip.Create("VoiceStream", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);

        if (_3dAudioSource != null)
        {
            _3dAudioSource.PlayOneShot(clip);
        }
    }
}
