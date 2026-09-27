using System.Collections.Generic;
using UnityEngine;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// Attach to monster attack limbs (e.g. Right Hand for Berserker swipe, Jaw/Head for Zombie bite).
    /// Controlled directly by MonsterAI or Animation Events.
    /// Accurately detects when the physical attack connects with an investigator/player.
    /// </summary>
    public class MonsterAttackHitbox : MonoBehaviour
    {
        [Header("Damage Settings")]
        [Tooltip("Base damage dealt by this attack. Can be overridden dynamically by MonsterAI.")]
        public float baseDamage = 35f;

        [Tooltip("Layers representing players/investigators.")]
        public LayerMask targetLayers;

        [Tooltip("Explicit collider to use as trigger hitbox. If null, automatically uses GetComponent<Collider>().")]
        public Collider hitboxCollider;

        [Header("Feedback")]
        public AudioClip hitSound;
        public GameObject hitVfxPrefab;

        private Collider _hitboxCollider;
        private GameObject _ownerRoot;
        private bool _isDamageActive = false;
        private float _currentDamage = 35f;
        private AudioSource _audioSource;
        private readonly HashSet<IDamageReceiver> _alreadyHitThisSwing = new HashSet<IDamageReceiver>();

        public bool IsDamageActive => _isDamageActive;

        private void Awake()
        {
            if (hitboxCollider != null)
            {
                _hitboxCollider = hitboxCollider;
            }
            else
            {
                // Check if this GameObject is a physical ragdoll bone (has Rigidbody or CharacterJoint)
                bool isRagdollBone = GetComponent<CharacterJoint>() != null || (GetComponent<Rigidbody>() != null && transform.parent != null);

                // First, check if a dedicated child trigger already exists
                Collider[] childCols = GetComponentsInChildren<Collider>(true);
                foreach (var c in childCols)
                {
                    if (c.gameObject != gameObject && c.isTrigger)
                    {
                        _hitboxCollider = c;
                        break;
                    }
                }

                if (_hitboxCollider == null)
                {
                    if (isRagdollBone)
                    {
                        // Pro safeguard: Do NOT convert the physical bone collider to a trigger, as it would cause the ragdoll limb to fall through the ground on death!
                        // Dynamically create a dedicated child trigger collider instead.
                        GameObject triggerChild = new GameObject("HitboxTrigger_Auto");
                        triggerChild.transform.SetParent(transform, false);
                        triggerChild.transform.localPosition = Vector3.zero;
                        var sphere = triggerChild.AddComponent<SphereCollider>();
                        sphere.radius = 0.35f;
                        sphere.isTrigger = true;
                        _hitboxCollider = sphere;

                        Debug.LogWarning($"[MonsterAttackHitbox] Auto-created child trigger on '{gameObject.name}' to preserve the ragdoll bone collider!");
                    }
                    else
                    {
                        _hitboxCollider = GetComponent<Collider>();
                    }
                }
            }

            if (_hitboxCollider != null)
            {
                _hitboxCollider.isTrigger = true;
                _hitboxCollider.enabled = false;
            }

            // Find root monster GameObject to prevent friendly fire on self
            var ai = GetComponentInParent<MonsterAI>();
            if (ai != null)
            {
                _ownerRoot = ai.gameObject;
            }
            else
            {
                _ownerRoot = transform.root.gameObject;
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null && hitSound != null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 1.0f;
                _audioSource.playOnAwake = false;
            }

            _currentDamage = baseDamage;
        }

        /// <summary>
        /// Activates the hitbox at the start of the damaging portion of the attack animation.
        /// </summary>
        public void EnableDamage(float customDamage = -1f)
        {
            _isDamageActive = true;
            _alreadyHitThisSwing.Clear();
            _currentDamage = customDamage > 0 ? customDamage : baseDamage;

            if (_hitboxCollider != null)
            {
                _hitboxCollider.enabled = true;
            }
        }

        /// <summary>
        /// Deactivates the hitbox when the attack swing completes.
        /// </summary>
        public void DisableDamage()
        {
            _isDamageActive = false;
            _alreadyHitThisSwing.Clear();

            if (_hitboxCollider != null)
            {
                _hitboxCollider.enabled = false;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_isDamageActive) return;
            if (other == null || other.gameObject == _ownerRoot) return;

            // Ignore hits on other monsters
            if (other.GetComponentInParent<MonsterAI>() != null || other.GetComponentInParent<MonsterController>() != null)
            {
                return;
            }

            // Never damage the Girl (their summoner)
            if (other.GetComponentInParent<GirlStealth>() != null || other.GetComponentInParent<GirlPossession>() != null
                || (GameManager.Instance != null && GameManager.Instance.GirlTransform == other.transform.root))
            {
                return;
            }

            // Check target layer filter if configured
            if (targetLayers.value != 0 && (targetLayers.value & (1 << other.gameObject.layer)) == 0)
            {
                return;
            }

            // Look for IDamageReceiver on the collider or parent
            IDamageReceiver receiver = other.GetComponent<IDamageReceiver>();
            if (receiver == null)
            {
                receiver = other.GetComponentInParent<IDamageReceiver>();
            }

            if (receiver != null)
            {
                if (_alreadyHitThisSwing.Contains(receiver))
                {
                    return; // Avoid multiple hits from the same swipe
                }

                _alreadyHitThisSwing.Add(receiver);

                // Apply damage
                receiver.TakeDamage(_currentDamage, false);
                Debug.Log($"[MonsterAttackHitbox] Hit {other.name} for {_currentDamage} damage!");

                // Play hit reaction on investigator if applicable
                if (other.TryGetComponent<InvestigatorCombatNet>(out var combat) || other.GetComponentInParent<InvestigatorCombatNet>() is { } parentCombat)
                {
                    var c = combat != null ? combat : other.GetComponentInParent<InvestigatorCombatNet>();
                    c?.PlayHitReaction(Random.Range(0, 2));
                }

                // Audio feedback
                if (hitSound != null)
                {
                    if (_audioSource != null)
                    {
                        _audioSource.PlayOneShot(hitSound);
                    }
                    else
                    {
                        AudioSource.PlayClipAtPoint(hitSound, transform.position);
                    }
                }

                // Visual feedback VFX
                if (hitVfxPrefab != null)
                {
                    Vector3 contactPoint = other.ClosestPoint(transform.position);
                    Instantiate(hitVfxPrefab, contactPoint, Quaternion.identity);
                }
            }
        }
    }
}
