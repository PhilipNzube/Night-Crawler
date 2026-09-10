using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Cursed Priest Exorcism Ability.
/// Forces the Vengeful Spirit (Girl) out of a possessed teammate and drains
/// her possession time pool by an editable penalty amount.
/// </summary>
public class PriestExorcismNet : NetworkBehaviour
{
    [Header("Exorcism Settings")]
    [Tooltip("Effective range of the exorcism rite in meters.")]
    public float exorcismRange = 8.0f;

    [Tooltip("Cooldown between exorcism casts in seconds.")]
    public float cooldownSeconds = 12.0f;

    [Tooltip("Key used by the Priest to cast Exorcism.")]
    public Key castKey = Key.R;

    [Header("Visuals & Audio")]
    public AudioClip exorcismCastSound;
    public ParticleSystem exorcismVFXPrefab;

    private float _cooldownTimer = 0f;
    private AudioSource _audioSource;
    private Transform _cameraTransform;

    [Tooltip("If true, this character can cast Exorcism. Automatically true for Priest; unlocked for others when looting Priest corpse.")]
    public bool isUnlocked = false;

    public float CooldownRemaining => _cooldownTimer;
    public bool CanCast => isUnlocked && _cooldownTimer <= 0f;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 1f;
        }

        if (Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }

        // Auto-unlock for Priest character
        if (gameObject.name.ToLower().Contains("priest"))
        {
            isUnlocked = true;
        }
    }

    /// <summary>
    /// Unlocks the Exorcism rite on this character when looting the Cursed Priest's corpse.
    /// </summary>
    public void InheritExorcismAbility()
    {
        isUnlocked = true;
        enabled = true;
        Debug.Log("[PriestExorcismNet] Inherited Holy Relic! Exorcism rite unlocked ([R] key).");
    }

    private void Update()
    {
        if (!IsOwner || !isUnlocked || PauseManager.IsGamePaused) return;

        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
        }

        if (Keyboard.current != null && Keyboard.current[castKey].wasPressedThisFrame && CanCast)
        {
            TryCastExorcism();
        }
    }

    private void TryCastExorcism()
    {
        if (_cameraTransform == null && Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }

        Transform cam = _cameraTransform != null ? _cameraTransform : transform;
        Ray ray = new Ray(cam.position, cam.forward);

        // Raycast or check radius for possessed teammates
        PlayerPossessableNet targetPossessed = null;

        if (Physics.Raycast(ray, out RaycastHit hit, exorcismRange))
        {
            targetPossessed = hit.collider.GetComponentInParent<PlayerPossessableNet>();
        }

        // Fallback: check sphere if raycast didn't hit direct collider
        if (targetPossessed == null || !targetPossessed.IsPossessed)
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, exorcismRange);
            foreach (var col in colliders)
            {
                if (col.gameObject == gameObject) continue;
                var poss = col.GetComponentInParent<PlayerPossessableNet>();
                if (poss != null && poss.IsPossessed)
                {
                    targetPossessed = poss;
                    break;
                }
            }
        }

        if (targetPossessed != null && targetPossessed.IsPossessed)
        {
            _cooldownTimer = cooldownSeconds;
            NetworkObject targetNetObj = targetPossessed.GetComponent<NetworkObject>();
            if (targetNetObj != null)
            {
                CastExorcismServerRpc(targetNetObj.NetworkObjectId);
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification("Casting Exorcism Rite!", 2.5f);
                }
            }
        }
        else
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("No possessed entity detected nearby.", 2f);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void CastExorcismServerRpc(ulong targetNetObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetObjectId, out var targetObj))
        {
            var targetPossessable = targetObj.GetComponent<PlayerPossessableNet>();
            if (targetPossessable != null && targetPossessable.IsPossessed)
            {
                targetPossessable.ForceExorcise();
                NotifyExorcismSucceededClientRpc(targetObj.transform.position);
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyExorcismSucceededClientRpc(Vector3 effectPosition)
    {
        if (_audioSource != null && exorcismCastSound != null)
        {
            _audioSource.PlayOneShot(exorcismCastSound);
        }

        if (exorcismVFXPrefab != null)
        {
            Instantiate(exorcismVFXPrefab, effectPosition + Vector3.up, Quaternion.identity);
        }
    }
}
