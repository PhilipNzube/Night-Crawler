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
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class NetworkManagerSingletonGuard : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoRegisterSceneGuard()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedGuard;
    }

    private static void OnSceneLoadedGuard(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        ResolveNetworkManagerDuplicates();
    }

    void Awake()
    {
        ResolveNetworkManagerDuplicates(GetComponent<NetworkManager>());
    }

    public static void ResolveNetworkManagerDuplicates(NetworkManager currentNetMgr = null)
    {
        var allNetMgrs = FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (allNetMgrs.Length > 1)
        {
            Debug.Log($"[NetworkManagerSingletonGuard] {allNetMgrs.Length} NetworkManager instances detected in memory. Resolving single instance.");

            // 1. Check if one of them is currently active and listening (e.g. running Host/Server/Client session)
            NetworkManager primaryToKeep = null;
            foreach (var netMgr in allNetMgrs)
            {
                if (netMgr != null && netMgr.IsListening)
                {
                    primaryToKeep = netMgr;
                    break;
                }
            }

            // 2. If none is actively listening, keep the existing Singleton reference if set
            if (primaryToKeep == null && NetworkManager.Singleton != null)
            {
                primaryToKeep = NetworkManager.Singleton;
            }

            if (primaryToKeep == null)
            {
                foreach (var netMgr in allNetMgrs)
                {
                    if (netMgr != null && netMgr != currentNetMgr)
                    {
                        primaryToKeep = netMgr;
                        break;
                    }
                }
            }

            if (primaryToKeep == null && allNetMgrs.Length > 0)
                primaryToKeep = allNetMgrs[0];

            // 3. Destroy all other instances that are NOT primaryToKeep
            foreach (var netMgr in allNetMgrs)
            {
                if (netMgr != null && netMgr != primaryToKeep)
                {
                    Debug.Log($"[NetworkManagerSingletonGuard] Destroying duplicate NetworkManager on '{netMgr.gameObject.name}'. Keeping primary on '{primaryToKeep.gameObject.name}'.");
                    netMgr.enabled = false;
                    Destroy(netMgr.gameObject);
                }
            }
        }
    }
}
