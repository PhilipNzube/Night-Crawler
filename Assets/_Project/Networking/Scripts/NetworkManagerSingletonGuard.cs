using UnityEngine;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Prevents duplicate NetworkManager instances from spawning when
/// transitioning back to the lobby or reloading scenes.
///
/// Setup:
/// Attach this component to the GameObject that holds your NetworkManager component.
/// If a NetworkManager instance already exists in memory (from DontDestroyOnLoad),
/// this script cleanly destroys the new duplicate instance immediately in Awake().
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class NetworkManagerSingletonGuard : MonoBehaviour
{
    void Awake()
    {
        var currentNetMgr = GetComponent<NetworkManager>();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton != currentNetMgr)
        {
            Debug.Log($"[NetworkManagerSingletonGuard] Duplicate NetworkManager detected on '{gameObject.name}' during scene load. Destroying duplicate instance.");
            
            // Disable component first so Netcode doesn't throw initialization warnings
            currentNetMgr.enabled = false;
            Destroy(gameObject);
        }
    }
}
