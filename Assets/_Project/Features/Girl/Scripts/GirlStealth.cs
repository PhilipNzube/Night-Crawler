using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using System.Collections;

public class GirlStealth : NetworkBehaviour
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;

    public NetworkVariable<bool> IsStealthActive = new NetworkVariable<bool>(false, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    [Header("Audio")]
    public AudioClip laughClip;
    public AudioClip whistleClip;
    public AudioClip whisperClip;
    private AudioSource _tauntSource;
    private float _tauntCooldown;

    private GirlMaterialController _matCtrl;
    private float _cooldownTimer;
    private CinemachineCamera _vcam;
    private Coroutine _fovCoroutine;
    private int _originalLayer;

    void Awake()
    {
        _matCtrl = GetComponent<GirlMaterialController>();
        _originalLayer = gameObject.layer;
        
        // Setup the Taunt AudioSource
        _tauntSource = gameObject.AddComponent<AudioSource>();
        _tauntSource.playOnAwake = false;
        _tauntSource.spatialBlend = 1f; // 3D Sound
        _tauntSource.minDistance = 2f;
        _tauntSource.maxDistance = 50f; // Increased for better map presence
        _tauntSource.volume = 1f;
    }

    public override void OnNetworkSpawn()
    {
        IsStealthActive.OnValueChanged += (oldVal, newVal) => ApplyStealthVisuals(newVal);
        ApplyStealthVisuals(IsStealthActive.Value); // Initial sync

        // --- LOUD & CLEAR TAUNTS ---
        if (_tauntSource != null)
        {
            _tauntSource.spatialBlend = IsOwner ? 0.2f : 1.0f;
            _tauntSource.maxDistance = 75f;
            _tauntSource.rolloffMode = AudioRolloffMode.Linear;
            Debug.Log($"[AUDIO] Taunt spatial blend initialized: {_tauntSource.spatialBlend} (Owner: {IsOwner})");
        }
    }

    void Update()
    {
        // --- COOLDOWN TICK (Runs for Everyone/Server) ---
        if (_tauntCooldown > 0) _tauntCooldown -= Time.deltaTime;

        if (!IsOwner) return;

        if (_cooldownTimer > 0) _cooldownTimer -= Time.deltaTime;

        if (Keyboard.current.qKey.wasPressedThisFrame && _cooldownTimer <= 0)
        {
            IsStealthActive.Value = !IsStealthActive.Value;
            if (!IsStealthActive.Value && stats != null) _cooldownTimer = stats.invisCooldown;
        }
    }

    private void ApplyStealthVisuals(bool isActive)
    {
        if (_matCtrl == null || stats == null) return;

        // 1. Owner sees stealthAlpha, Explorers see 0 (100% invisible)
        float targetAlpha = 1f;
        if (isActive)
        {
            targetAlpha = IsOwner ? stats.stealthAlpha : 0f;
        }
        
        _matCtrl.RequestAlpha(targetAlpha, 0.4f);
        
        // 2. Only the Girl sees the outline feedback
        _matCtrl.ToggleOutline(IsOwner && isActive);

        // 3. Predator Perspective (Owner Only)
        if (IsOwner)
        {
            ApplyPredatorVision(isActive);
        }

        // 4. Server-side tag change for AI
        if (IsServer) gameObject.tag = isActive ? "Untagged" : "Player";
    }

    private void ApplyPredatorVision(bool isActive)
    {
        // 1. Map Invisibility: Change layer so we don't show on radar
        gameObject.layer = isActive ? 1 : _originalLayer;

        // 2. FOV Widen: Smoothly transition the camera
        if (_vcam == null) 
        {
            _vcam = GetComponentInChildren<CinemachineCamera>();
            if (_vcam == null) _vcam = FindObjectOfType<CinemachineCamera>();
        }

        if (_vcam != null)
        {
            if (_fovCoroutine != null) StopCoroutine(_fovCoroutine);
            float targetFOV = isActive ? 85f : 60f;
            _fovCoroutine = StartCoroutine(LerpFOV(targetFOV, 0.5f));
        }
    }

    private IEnumerator LerpFOV(float targetFOV, float duration)
    {
        float startFOV = _vcam.Lens.FieldOfView;
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var lens = _vcam.Lens;
            lens.FieldOfView = Mathf.Lerp(startFOV, targetFOV, elapsed / duration);
            _vcam.Lens = lens;
            yield return null;
        }
        var finalLens = _vcam.Lens;
        finalLens.FieldOfView = targetFOV;
        _vcam.Lens = finalLens;
    }

    // Called by GameManager to verify if this Demon can taunt
    public bool CanTaunt()
    {
        return _tauntCooldown <= 0;
    }

    // Called by GameManager when a taunt is successfully broadcast
    public void ResetTauntCooldown()
    {
        _tauntCooldown = 4f;
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayTauntServerRpc(int type)
    {
        // Server tracks the cooldown for this specific Demon
        if (_tauntCooldown <= 0)
        {
            PlayTauntClientRpc(type);
            _tauntCooldown = 4f; 
        }
    }

    [ClientRpc]
    private void PlayTauntClientRpc(int type)
    {
        PlayLocalTaunt(type);
    }

    // This can also be called by GameManager's Global Handshake
    public void PlayLocalTaunt(int type)
    {
        Debug.Log($"[AUDIO] Playing Taunt locally (Type: {type})");
        if (_tauntSource == null) return;

        AudioClip clip = null;
        if (type == 0) clip = laughClip;
        else if (type == 1) clip = whistleClip;
        else if (type == 2) clip = whisperClip;

        if (clip != null)
        {
            Debug.Log($"[AUDIO] Playing Clip: {clip.name} | Vol: {_tauntSource.volume} | MaxDist: {_tauntSource.maxDistance}");
            _tauntSource.PlayOneShot(clip);
        }
    }
}