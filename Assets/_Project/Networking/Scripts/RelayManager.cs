using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

/// <summary>
/// SOLID — SRP: Manages Unity Gaming Services (UGS) initialization, anonymous authentication,
/// and Relay allocation / transport binding for Netcode for GameObjects.
/// </summary>
public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    [Header("Relay Settings")]
    [Tooltip("Protocol connection type. 'dtls' is encrypted UDP (recommended). 'udp' is unencrypted. 'wss' for WebGL.")]
    public string connectionType = "dtls";

    /// <summary>The current active Join Code if hosting, or joined code if client.</summary>
    public string CurrentJoinCode { get; private set; } = string.Empty;

    /// <summary>True if UGS has been initialized and signed in.</summary>
    public bool IsAuthenticated => AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

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
    }

    /// <summary>
    /// Initializes Unity Services and performs Anonymous Sign-In if not already completed.
    /// </summary>
    public async Task<bool> EnsureInitializedAndSignedInAsync()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
                Debug.Log("[RelayManager] Unity Gaming Services initialized.");
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[RelayManager] Signed in anonymously. Player ID: {AuthenticationService.Instance.PlayerId}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RelayManager] Failed to initialize/authenticate UGS: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Allocates a Relay session for the host, retrieves the Join Code, binds to UnityTransport, and starts hosting.
    /// </summary>
    /// <param name="maxConnections">Maximum connected peers (excluding host).</param>
    /// <returns>The generated Join Code, or null if failed.</returns>
    public async Task<string> StartRelayHostAsync(int maxConnections)
    {
        bool ready = await EnsureInitializedAndSignedInAsync();
        if (!ready)
        {
            Debug.LogError("[RelayManager] Cannot start Relay host: Authentication failed.");
            return null;
        }

        try
        {
            // 1. Allocate relay slot on closest Relay server
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            // 2. Retrieve the short human-readable join code
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            CurrentJoinCode = joinCode;

            // 3. Configure UnityTransport to route through Relay
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[RelayManager] No UnityTransport component found on NetworkManager!");
                return null;
            }

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, connectionType);
            transport.SetRelayServerData(relayServerData);

            // 4. Start Netcode Host
            bool started = NetworkManager.Singleton.StartHost();
            if (started)
            {
                Debug.Log($"[RelayManager] Relay Host started successfully. Join Code: {joinCode}");
                return joinCode;
            }
            else
            {
                Debug.LogError("[RelayManager] NetworkManager failed to StartHost.");
                return null;
            }
        }
        catch (RelayServiceException rex)
        {
            Debug.LogError($"[RelayManager] RelayServiceException while hosting: {rex.Message} (Reason: {rex.Reason})");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RelayManager] Exception while hosting via Relay: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Joins a Relay session using a Join Code, binds to UnityTransport, and starts the client.
    /// </summary>
    /// <param name="joinCode">The 6-character Join Code provided by the host.</param>
    /// <returns>True if join succeeded and client started.</returns>
    public async Task<bool> StartRelayClientAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogError("[RelayManager] Cannot join: Join code is empty.");
            return false;
        }

        bool ready = await EnsureInitializedAndSignedInAsync();
        if (!ready)
        {
            Debug.LogError("[RelayManager] Cannot join Relay client: Authentication failed.");
            return false;
        }

        try
        {
            string cleanCode = joinCode.Trim().ToUpper();

            // 1. Join the allocation using the join code
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(cleanCode);
            CurrentJoinCode = cleanCode;

            // 2. Configure UnityTransport
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[RelayManager] No UnityTransport component found on NetworkManager!");
                return false;
            }

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, connectionType);
            transport.SetRelayServerData(relayServerData);

            // 3. Start Netcode Client
            bool started = NetworkManager.Singleton.StartClient();
            if (started)
            {
                Debug.Log($"[RelayManager] Relay Client joined successfully with code: {cleanCode}");
                return true;
            }
            else
            {
                Debug.LogError("[RelayManager] NetworkManager failed to StartClient.");
                return false;
            }
        }
        catch (RelayServiceException rex)
        {
            Debug.LogError($"[RelayManager] RelayServiceException while joining: {rex.Message} (Reason: {rex.Reason})");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RelayManager] Exception while joining via Relay: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reverts UnityTransport back to direct IP / LAN connection (e.g. 127.0.0.1 : 7777).
    /// Used for local development and offline testing.
    /// </summary>
    public void ConfigureLocalTransport(string ipAddress = "127.0.0.1", ushort port = 7777)
    {
        if (NetworkManager.Singleton == null) return;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(ipAddress, port);
            CurrentJoinCode = string.Empty;
            Debug.Log($"[RelayManager] Transport configured for Local LAN: {ipAddress}:{port}");
        }
    }
}
