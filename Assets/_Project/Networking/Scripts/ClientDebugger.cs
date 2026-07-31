using Unity.Netcode;
using UnityEngine;

public class ClientDebugger : MonoBehaviour
{
    void Start()
    {
        Debug.Log("[CLIENT DEBUG] Script started");

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[CLIENT DEBUG] No NetworkManager!");
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnected;
    }

    void OnConnected(ulong clientId)
    {
        Debug.Log($"[CLIENT DEBUG] CONNECTED as {clientId}");
    }

    void OnDisconnected(ulong clientId)
    {
        Debug.Log($"[CLIENT DEBUG] DISCONNECTED {clientId}");
    }
}