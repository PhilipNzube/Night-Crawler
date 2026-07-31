using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    public AudioClip testSound;
    private AudioSource _diagSource;

    private void Start()
    {
        _diagSource = gameObject.AddComponent<AudioSource>();
        _diagSource.playOnAwake = false;
        _diagSource.spatialBlend = 0f; // 2D Sound
        _diagSource.volume = 1f;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 250, 250));
        GUILayout.BeginVertical("box");

        // --- MASTER AUDIO TEST ---
        if (GUILayout.Button("TEST ENGINE AUDIO (2D)"))
        {
            if (testSound != null) _diagSource.PlayOneShot(testSound);
            else Debug.LogWarning("[AUDIO-DIAG] No 'Test Sound' assigned to NetworkUI in Inspector!");
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Start Host"))
                NetworkManager.Singleton.StartHost();

            if (GUILayout.Button("Start Client"))
                NetworkManager.Singleton.StartClient();
        }
        else
        {
            GUILayout.Label($"Status: {(NetworkManager.Singleton.IsConnectedClient ? "Connected" : "Connecting")}");

            if (NetworkManager.Singleton.IsServer)
            {
                GameManager gm = FindObjectOfType<GameManager>();
                if (gm != null)
                {
                    if (!gm.HasGameStarted)
                    {
                        GUILayout.Label($"Players in Lobby: {gm.CurrentPlayerCount}");

                        if (gm.CurrentPlayerCount >= gm.minPlayers)
                        {
                            if (GUILayout.Button("START MATCH!"))
                            {
                                gm.StartGame();
                            }
                        }
                        else
                        {
                            GUI.enabled = false;
                            GUILayout.Button($"WAITING FOR {gm.minPlayers} PLAYERS...");
                            GUI.enabled = true;
                        }
                    }
                }
                else 
                {
                    Debug.LogError("[UI] Could not find GameManager in scene!");
                }
            }

            if (GUILayout.Button("Disconnect"))
                NetworkManager.Singleton.Shutdown();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}