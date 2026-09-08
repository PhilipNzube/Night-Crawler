using System;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;

/// <summary>
/// SOLID — SRP: Pure Unity Vivox 3D Positional Proximity Voice Chat.
/// Vivox handles all microphone capture, network transmission, 3D spatialization,
/// and playback natively through dedicated voice servers.
/// </summary>
public class ProximityVoiceChatNet : NetworkBehaviour
{
    [Header("3D Acoustic Settings")]
    [Tooltip("Maximum distance in meters where player voice can be heard.")]
    public float voiceRadius = 25.0f;

    [Tooltip("Minimum distance in meters for full volume before attenuation starts.")]
    public float minDistance = 2.0f;

    [Header("Microphone Controls")]
    [Tooltip("Push to talk key (e.g. V). If pushToTalk is false, operates as Open Mic.")]
    public Key pushToTalkKey = Key.V;

    [Tooltip("If true, requires holding down pushToTalkKey to speak. If false, mic is always transmitting (Open Mic).")]
    public bool pushToTalk = false;

    // Vivox State
    private string _currentChannelName = string.Empty;
    private bool _isChannelJoined = false;
    private Transform _listenerCamera;

    public bool IsChannelJoined => _isChannelJoined;

    public override void OnNetworkSpawn()
    {
        if (Camera.main != null)
        {
            _listenerCamera = Camera.main.transform;
        }

        if (IsOwner)
        {
            ConnectToVivox3D();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            LeaveVivox3D();
        }
    }

    // =========================================================================
    //  Vivox 3D Connection & Channel Management
    // =========================================================================

    private async void ConnectToVivox3D()
    {
        // 1. Check internet reachability
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            ReportVoiceError("No internet connection detected. Voice chat offline.");
            return;
        }

        try
        {
            // 2. Ensure Unity Services are initialized
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            // 3. Ensure player is authenticated
            if (AuthenticationService.Instance != null && !AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            if (VivoxService.Instance == null)
            {
                ReportVoiceError("VivoxService not found. Verify Vivox package and dashboard setup.");
                return;
            }

            // 4. Initialize Vivox Service if not ready
            if (VivoxService.Instance.InitializationState != VivoxInitializationState.Initialized)
            {
                await VivoxService.Instance.InitializeAsync();
            }

            // 5. Log in to Vivox
            if (!VivoxService.Instance.IsLoggedIn)
            {
                await VivoxService.Instance.LoginAsync();
            }

            // 6. Build channel name tied to match / Relay Join Code
            string lobbyCode = (RelayManager.Instance != null && !string.IsNullOrEmpty(RelayManager.Instance.CurrentJoinCode))
                ? RelayManager.Instance.CurrentJoinCode
                : "NightCrawlerRoom";

            _currentChannelName = $"NC_3DPos_{lobbyCode}";

            // 7. Configure 3D Channel properties
            var properties = new Channel3DProperties(
                audibleDistance: Mathf.RoundToInt(voiceRadius),
                conversationalDistance: Mathf.RoundToInt(minDistance),
                audioFadeIntensityByDistanceaudio: 1.0f,
                audioFadeModel: AudioFadeModel.LinearByDistance);

            await VivoxService.Instance.JoinPositionalChannelAsync(
                _currentChannelName,
                ChatCapability.AudioOnly,
                properties);

            _isChannelJoined = true;
            Debug.Log($"[Vivox] Successfully joined 3D Positional Voice room: '{_currentChannelName}'!");

            // 8. Apply Push-to-Talk or Open-Mic state
            ApplyMicMuteState();
        }
        catch (Exception ex)
        {
            ReportVoiceError($"Failed to connect to Vivox voice server: {ex.Message}");
        }
    }

    private async void LeaveVivox3D()
    {
        if (VivoxService.Instance != null && _isChannelJoined && !string.IsNullOrEmpty(_currentChannelName))
        {
            try
            {
                _isChannelJoined = false;
                await VivoxService.Instance.LeaveChannelAsync(_currentChannelName);
                Debug.Log($"[Vivox] Left voice room: '{_currentChannelName}'");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Vivox] Error leaving voice room: {ex.Message}");
            }
        }
    }

    private void ReportVoiceError(string errorMessage)
    {
        _isChannelJoined = false;
        Debug.LogError($"[VivoxProximityVoice] {errorMessage}");

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"[VOICE ERROR] {errorMessage}", 5f);
        }
    }

    // =========================================================================
    //  Per-Frame 3D Position & Push-To-Talk
    // =========================================================================

    private void Update()
    {
        if (!IsOwner || !_isChannelJoined || VivoxService.Instance == null) return;

        if (_listenerCamera == null && Camera.main != null)
        {
            _listenerCamera = Camera.main.transform;
        }

        // 1. Update 3D coordinates for Vivox spatial engine
        Transform listener = _listenerCamera != null ? _listenerCamera : transform;
        VivoxService.Instance.Set3DPosition(
            speakerPos: transform.position,
            listenerPos: listener.position,
            listenerAtOrient: listener.forward,
            listenerUpOrient: listener.up,
            channelName: _currentChannelName
        );

        // 2. Handle Push-To-Talk input
        if (pushToTalk)
        {
            bool isHeld = (Keyboard.current != null && Keyboard.current[pushToTalkKey].isPressed);
            if (isHeld && VivoxService.Instance.IsInputDeviceMuted)
            {
                VivoxService.Instance.UnmuteInputDevice();
            }
            else if (!isHeld && !VivoxService.Instance.IsInputDeviceMuted)
            {
                VivoxService.Instance.MuteInputDevice();
            }
        }
    }

    private void ApplyMicMuteState()
    {
        if (VivoxService.Instance == null) return;

        if (pushToTalk)
        {
            VivoxService.Instance.MuteInputDevice();
        }
        else
        {
            VivoxService.Instance.UnmuteInputDevice();
        }
    }
}
