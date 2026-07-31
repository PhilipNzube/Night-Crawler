using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class HauntedAtmosphereNet : NetworkBehaviour
{
    [Header("Panic Settings")]
    public float panicRadius = 8f;
    public AudioClip breathingClip;
    private AudioSource _panicSource;

    [Header("Gaze Detection")]
    public float tauntCheckRadius = 15f;
    private float _nextCheckTime;

    void Awake()
    {
        // Setup local breathing source (Spatialized 3D so Demon can hear)
        _panicSource = gameObject.AddComponent<AudioSource>();
        _panicSource.clip = breathingClip;
        _panicSource.loop = true;
        _panicSource.spatialBlend = 1f;
        _panicSource.minDistance = 1f;
        _panicSource.maxDistance = 10f;
        _panicSource.playOnAwake = false;
    }

    void Update()
    {
        if (!IsOwner) return;

        // Optimization: Don't check every single frame
        if (Time.time < _nextCheckTime) return;
        _nextCheckTime = Time.time + 0.2f;

        HandlePanicAndGaze();
    }

    private void HandlePanicAndGaze()
    {
        bool demonIsNear = false;
        Vector3 nearestDemonPos = transform.position + Vector3.forward * 100f;
        float minDist = Mathf.Infinity;
        
        // --- OPTIMIZED SEARCH: Use GameManager's cached registry ---
        if (GameManager.Instance == null) return;
        
        foreach (var stealth in GameManager.Instance.GetAllDemons())
        {
            if (stealth == null) continue;
            
            float dist = Vector3.Distance(transform.position, stealth.transform.position);
            // Use tauntCheckRadius or a sensible default if it's 0 in Inspector
            float checkDist = tauntCheckRadius > 0 ? tauntCheckRadius : 15f; 
            
            if (dist < checkDist && stealth.IsStealthActive.Value)
            {
                demonIsNear = true;
                CheckDemonGaze(stealth);
            }
            
            if (dist < minDist && stealth.IsStealthActive.Value)
            {
                minDist = dist;
                nearestDemonPos = stealth.transform.position;
            }
        }

        // 2. Manage Breathing
        if (demonIsNear && Vector3.Distance(transform.position, nearestDemonPos) < panicRadius)
        {
            if (!_panicSource.isPlaying) _panicSource.Play();
        }
        else
        {
            if (_panicSource.isPlaying) _panicSource.Stop();
        }
    }

    private void CheckDemonGaze(GirlStealth demon)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 viewportPos = cam.WorldToViewportPoint(demon.transform.position);
        
        // CHECK: Is the Demon NOT on the screen?
        bool inFront = viewportPos.z > 0;
        bool onScreen = viewportPos.x > 0 && viewportPos.x < 1 && viewportPos.y > 0 && viewportPos.y < 1;

        if (!inFront || !onScreen)
        {
            // Survivor isn't looking! Trigger the taunt (Laugh, Whistle, or Whisper)
            // We now use the Global Handshake via GameManager for 100% reliability
            int tauntType = Random.Range(0, 3); // 0 = Laugh, 1 = Whistle, 2 = Whisper
            
            GameManager gm = GameManager.Singleton;
            if (gm != null)
            {
                Debug.Log($"[HAUNTED] Triggering Global Taunt via GameManager (Type: {tauntType})");
                gm.BroadcastTauntServerRpc(demon.NetworkObjectId, tauntType);
            }
        }
    }
}
