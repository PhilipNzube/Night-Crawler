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
    private bool _isLocalPlayerVoiceInstance = false;
    private bool _girlMuteNotified = false;
    private bool _wasPossessedLastFrame = false;

    public bool IsChannelJoined => _isChannelJoined;

    public override void OnNetworkSpawn()
    {
        if (Camera.main != null)
        {
            _listenerCamera = Camera.main.transform;
        }

        if (IsOwner)
        {
            _isLocalPlayerVoiceInstance = true;
            ConnectToVivox3D();

            if (IsGirlPlayer())
            {
                StartCoroutine(NotifyGirlMutedRoutine());
            }
        }
    }

    private System.Collections.IEnumerator NotifyGirlMutedRoutine()
    {
        // Wait briefly for HUD & NotificationManager to fully initialize
        yield return new WaitForSeconds(1.5f);
        if (!_girlMuteNotified)
        {
            _girlMuteNotified = true;
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(
                    "MICROPHONE MUTED",
                    "Vengeful Spirits cannot speak in the physical realm. Your microphone is disabled.",
                    new Color(0.9f, 0.2f, 0.2f, 1f),
                    6.0f
                );
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_isLocalPlayerVoiceInstance)
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
            string friendly = GetFriendlyVoiceErrorMessage(errorMessage);
            NotificationManager.Instance.ShowNotification("VOICE CHAT", friendly, 4.5f);
        }
    }

    private string GetFriendlyVoiceErrorMessage(string technicalError)
    {
        if (string.IsNullOrWhiteSpace(technicalError)) return "Voice service is currently offline.";
        string lower = technicalError.ToLowerInvariant();
        if (lower.Contains("internet") || lower.Contains("notreachable") || lower.Contains("network"))
            return "Offline. Please check your internet connection.";
        if (lower.Contains("mic") || lower.Contains("microphone") || lower.Contains("device") || lower.Contains("audio"))
            return "Microphone or audio device not detected.";
        if (lower.Contains("permission") || lower.Contains("denied"))
            return "Microphone access was denied in system settings.";
        if (lower.Contains("timeout") || lower.Contains("timed out"))
            return "Voice chat connection timed out. Retrying in background...";
        if (lower.Contains("auth") || lower.Contains("signin") || lower.Contains("sign-in") || lower.Contains("login"))
            return "Voice login failed. Playing in silent mode.";
        if (lower.Contains("channel") || lower.Contains("room") || lower.Contains("join"))
            return "Unable to connect to proximity voice room.";
        return "Voice service is temporarily unavailable.";
    }

    // =========================================================================
    //  Per-Frame 3D Position & Push-To-Talk
    // =========================================================================

    private void Update()
    {
        if (!_isLocalPlayerVoiceInstance || !_isChannelJoined || VivoxService.Instance == null || PauseManager.IsGamePaused) return;

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

        bool isGirl = IsGirlPlayer();
        bool isPossessed = IsLocalPlayerPossessed();

        // Check if voice key was attempted this frame
        bool voiceKeyPressed = KeybindingManager.IsActionTriggered("VoiceChat")
            || (KeybindingManager.Instance == null && Keyboard.current != null && pushToTalkKey != Key.None && Keyboard.current[pushToTalkKey].wasPressedThisFrame);

        // CASE 1: The Girl player is ALWAYS muted
        if (isGirl)
        {
            if (!VivoxService.Instance.IsInputDeviceMuted)
            {
                VivoxService.Instance.MuteInputDevice();
            }

            if (voiceKeyPressed && NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(
                    "MICROPHONE MUTED",
                    "Vengeful Spirits cannot transmit voice to investigators.",
                    new Color(0.9f, 0.2f, 0.2f, 1f),
                    3.0f
                );
            }
            return;
        }

        // CASE 2: Investigator is possessed by the Girl -> Mute microphone!
        if (isPossessed)
        {
            _wasPossessedLastFrame = true;
            if (!VivoxService.Instance.IsInputDeviceMuted)
            {
                VivoxService.Instance.MuteInputDevice();
            }

            if (voiceKeyPressed && NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(
                    "MICROPHONE MUTED",
                    "You are currently possessed. Your vocal cords are suppressed.",
                    new Color(0.9f, 0.2f, 0.2f, 1f),
                    3.0f
                );
            }
            return;
        }

        // CASE 3: Possession just ended -> restore normal mic mute state
        if (_wasPossessedLastFrame)
        {
            _wasPossessedLastFrame = false;
            ApplyMicMuteState();
        }

        // CASE 4: Normal human investigator Push-To-Talk / Open Mic
        if (pushToTalk)
        {
            bool isHeld = KeybindingManager.IsActionHeld("VoiceChat")
                || (KeybindingManager.Instance == null && Keyboard.current != null && pushToTalkKey != Key.None && Keyboard.current[pushToTalkKey].isPressed);
            if (isHeld && VivoxService.Instance.IsInputDeviceMuted)
            {
                VivoxService.Instance.UnmuteInputDevice();
            }
            else if (!isHeld && !VivoxService.Instance.IsInputDeviceMuted)
            {
                VivoxService.Instance.MuteInputDevice();
            }
        }
        else
        {
            if (VivoxService.Instance.IsInputDeviceMuted)
            {
                VivoxService.Instance.UnmuteInputDevice();
            }
        }
    }

    private void ApplyMicMuteState()
    {
        if (VivoxService.Instance == null) return;

        if (IsGirlPlayer() || IsLocalPlayerPossessed())
        {
            VivoxService.Instance.MuteInputDevice();
            return;
        }

        if (pushToTalk)
        {
            VivoxService.Instance.MuteInputDevice();
        }
        else
        {
            VivoxService.Instance.UnmuteInputDevice();
        }
    }

    private bool IsGirlPlayer()
    {
        if (PersistentCharacterSelection.IsVengefulSpirit()) return true;
        if (GetComponent<GirlPossession>() != null || GetComponent<GirlMovement>() != null || GetComponent<GirlStealth>() != null) return true;
        if (gameObject.name.ToLower().Contains("girl") || gameObject.name.ToLower().Contains("demon") || gameObject.name.ToLower().Contains("spirit")) return true;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localObj.GetComponent<GirlPossession>() != null || localObj.GetComponent<GirlMovement>() != null) return true;
        }
        return false;
    }

    private bool IsLocalPlayerPossessed()
    {
        if (TryGetComponent<PlayerPossessableNet>(out var pNet) && pNet.isPossessed.Value)
        {
            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            if (pNet.originalOwnerClientId.Value == localId) return true;
        }
        return PlayerPossessableNet.IsLocalPlayerPossessed();
    }
}
