using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Manages environmental poison / suffocation damage over time.
/// Players slowly lose health over an Inspector-editable lifespan (default 3 minutes = 180s).
/// Hazard Specialist has a 2x lifespan multiplier (default 6 minutes).
/// As health drops, BloodScreenOverlay automatically displays increasing blood.
/// Shows an ominous warning notification at match start informing players of their lifespan.
/// Apparitions / Vengeful Spirit are immune to suffocation.
/// </summary>
public class SuffocationSystemNet : NetworkBehaviour
{
    [Header("Lifespan Settings")]
    [Tooltip("Base lifespan in seconds before player suffocates to death without healing. (Default: 180s = 3 minutes).")]
    public float baseLifespanSeconds = 180f;

    [Tooltip("Multiplier applied if this player is a Hazard Specialist.")]
    public float hazardMultiplier = 2.0f;

    [Tooltip("If true, this character is immune to suffocation (e.g. Vengeful Spirit).")]
    public bool isImmune = false;

    [Header("Warning Settings")]
    public bool showStartWarning = true;
    public float warningDisplayDuration = 6f;

    private HealthSystem _healthSystem;
    private TargetHealth _targetHealth;
    private float _damageInterval = 1.0f;
    private float _effectiveLifespan;
    private bool _isHazardSpecialist = false;

    public float EffectiveLifespan => _effectiveLifespan;
    public bool IsHazardSpecialist => _isHazardSpecialist;

    private void Awake()
    {
        _healthSystem = GetComponent<HealthSystem>();
        _targetHealth = GetComponent<TargetHealth>();
        _effectiveLifespan = baseLifespanSeconds;
    }

    public override void OnNetworkSpawn()
    {
        DetectRoleAndModifiers();

        if (IsServer && !isImmune)
        {
            StartCoroutine(SuffocationDamageRoutine());
        }

        if (IsOwner && showStartWarning && !isImmune)
        {
            StartCoroutine(ShowStartWarningRoutine());
        }
    }

    private void DetectRoleAndModifiers()
    {
        // Check if character is Girl / Vengeful Spirit
        if (GetComponent<GirlPossession>() != null || GetComponent<GirlMovement>() != null)
        {
            isImmune = true;
            return;
        }

        // Check if player is Hazard Specialist
        // We check character name, attached components, or saved character index
        if (gameObject.name.ToLower().Contains("hazard") || gameObject.name.ToLower().Contains("protector"))
        {
            _isHazardSpecialist = true;
        }
        else if (CharacterSelectManager.Instance != null && NetworkManager.Singleton != null)
        {
            int charIndex = CharacterSelectManager.Instance.GetSelectedCharacterIndex(OwnerClientId);
            if (charIndex >= 0 && CharacterSelectManager.Instance.availableCharacters != null 
                && charIndex < CharacterSelectManager.Instance.availableCharacters.Count)
            {
                var charData = CharacterSelectManager.Instance.availableCharacters[charIndex];
                if (charData != null && charData.profession == InvestigatorProfession.HazardSpecialist)
                {
                    _isHazardSpecialist = true;
                }
            }
        }

        _effectiveLifespan = _isHazardSpecialist ? baseLifespanSeconds * hazardMultiplier : baseLifespanSeconds;
    }

    /// <summary>
    /// Inherits the Hazard Specialist's gas mask / respirator filter when looted from their corpse.
    /// Extends lifespan by the hazard multiplier.
    /// </summary>
    public void InheritHazardFilter()
    {
        if (!_isHazardSpecialist)
        {
            _isHazardSpecialist = true;
            _effectiveLifespan *= hazardMultiplier;
            Debug.Log($"[SuffocationSystemNet] Inherited Hazard Specialist gas filter! Lifespan extended to {_effectiveLifespan}s.");
        }
    }

    private IEnumerator SuffocationDamageRoutine()
    {
        // Short delay after match starts before suffocation damage begins
        yield return new WaitForSeconds(3.0f);

        while (IsServer && !isImmune && _healthSystem != null && !_healthSystem.IsDead)
        {
            yield return new WaitForSeconds(_damageInterval);

            if (_effectiveLifespan <= 0f) continue;

            float damagePerSecond = _healthSystem.MaxHealth / _effectiveLifespan;
            float damageThisTick = damagePerSecond * _damageInterval;

            // If remaining health is very low (at or near 0), execute immediate lethal kill
            if (_healthSystem.CurrentHealth <= Mathf.Max(2.5f, damageThisTick * 1.25f))
            {
                _healthSystem.TakeDamage(9999f);
                if (_targetHealth != null) _targetHealth.TakeDamage(9999f);
                break;
            }

            _healthSystem.TakeDamage(damageThisTick);
            if (_targetHealth != null)
            {
                _targetHealth.TakeDamage(damageThisTick);
            }
        }
    }

    private IEnumerator ShowStartWarningRoutine()
    {
        yield return new WaitForSeconds(1.5f);

        int minutes = Mathf.FloorToInt(_effectiveLifespan / 60f);
        int seconds = Mathf.FloorToInt(_effectiveLifespan % 60f);
        string timeStr = $"{minutes}m {seconds:00}s";

        string message = _isHazardSpecialist
            ? $"[HAZARD FILTER ACTIVE] Toxic mine air detected. Reinforced respirator lifespan: {timeStr}."
            : $"[WARNING: TOXIC ATMOSPHERE] Lethal mine air detected! Estimated survival: {timeStr} without medical treatment.";

        Debug.LogWarning($"[SuffocationSystemNet] {message}");

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowHazardWarning(message, warningDisplayDuration);
        }
    }
}
