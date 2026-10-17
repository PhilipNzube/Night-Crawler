using System.Collections;
using UnityEngine;
using NightCrawler.Monsters;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// Executes melee combat, attack cooldowns, audio grunts, and hitbox damage activation
    /// for monster NPCs based on the EnemyNPC_AI EnemyAttack architecture.
    /// Follows the Single Responsibility Principle (SRP).
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterCombat : MonoBehaviour
    {
        [Header("Combat Settings")]
        [Tooltip("Effective striking range to initiate attack.")]
        public float attackRange = 2.2f;

        [Tooltip("Damage dealt per strike to investigators.")]
        public float attackDamage = 35f;

        [Tooltip("Cooldown period between successive attacks.")]
        public float attackCooldown = 1.8f;

        [Header("Components & Hitbox")]
        [Tooltip("Physical limb hitbox (Jaw for Zombie, Right Hand for Berserker).")]
        public MonsterAttackHitbox attackHitbox;

        [Tooltip("Audio clip played during the attack swing.")]
        public AudioClip attackSoundClip;

        [Tooltip("If true, MonsterCombat will play attackSoundClip directly with PlayOneShot. If false, MonsterAI manages synchronized 3D attack audio across the network.")]
        public bool autoPlayAudioInSequence = false;

        [Tooltip("AudioSource for spatial attack audio.")]
        public AudioSource audioSource;

        [Tooltip("Animator driving combat animations.")]
        public Animator animator;

        private float _cooldownTimer = 0f;
        private Coroutine _attackRoutine;

        public bool IsOnCooldown => _cooldownTimer > 0f;
        public float CooldownRemaining => _cooldownTimer;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (attackHitbox == null) attackHitbox = GetComponentInChildren<MonsterAttackHitbox>(true);
        }

        private void Update()
        {
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
            }
        }

        /// <summary>
        /// Validates if an attack can be executed against the target right now.
        /// </summary>
        public bool CanAttack(Transform target)
        {
            if (target == null || _cooldownTimer > 0f) return false;

            float dist = Vector3.Distance(transform.position, target.position);
            return dist <= attackRange;
        }

        /// <summary>
        /// Initiates the attack animation, vocalization, and synchronized hitbox activation.
        /// </summary>
        public void StartAttack(string animationTrigger, System.Action onComplete = null)
        {
            if (_attackRoutine != null)
            {
                StopCoroutine(_attackRoutine);
            }
            _attackRoutine = StartCoroutine(AttackSequence(animationTrigger, onComplete));
        }

        private IEnumerator AttackSequence(string animationTrigger, System.Action onComplete)
        {
            _cooldownTimer = attackCooldown;

            // 1. Trigger Animation
            if (animator != null && !string.IsNullOrEmpty(animationTrigger))
            {
                animator.SetTrigger(animationTrigger);
            }

            // 2. Attack swing sound is silenced per design (physical impact audio handled on hitbox contact)

            // 3. Anticipation window (strike wind-up)
            yield return new WaitForSeconds(0.25f);

            // 4. Activate Hitbox for the damaging strike window
            if (attackHitbox != null)
            {
                attackHitbox.EnableDamage(attackDamage);
            }
            else
            {
                // Fallback sphere overlap damage check if no physical hitbox is wired (from EnemyAttack.cs)
                PerformDirectOverlapDamage();
            }

            // 5. Active damage window duration
            yield return new WaitForSeconds(0.60f);

            // 6. Disable Hitbox
            if (attackHitbox != null)
            {
                attackHitbox.DisableDamage();
            }

            // 7. Follow-through recovery
            yield return new WaitForSeconds(0.35f);

            _attackRoutine = null;
            onComplete?.Invoke();
        }

        private void PerformDirectOverlapDamage()
        {
            Vector3 center = transform.position + transform.forward * (attackRange * 0.75f) + Vector3.up * 1.0f;
            Collider[] hits = Physics.OverlapSphere(center, attackRange * 0.6f);

            foreach (var hit in hits)
            {
                if (hit == null || hit.gameObject == gameObject) continue;
                if (hit.GetComponentInParent<MonsterAI>() != null || MonsterAI.IsGirl(hit.transform.root != null ? hit.transform.root : hit.transform)) continue;

                IDamageReceiver receiver = hit.GetComponent<IDamageReceiver>();
                if (receiver == null) receiver = hit.GetComponentInParent<IDamageReceiver>();

                if (receiver != null)
                {
                    receiver.TakeDamage(attackDamage, false);
                    break; // Hit one target per swing
                }
            }
        }

        /// <summary>
        /// Cancels any active attack sequence and ensures the hitbox is disabled.
        /// </summary>
        public void CancelAttack()
        {
            if (_attackRoutine != null)
            {
                StopCoroutine(_attackRoutine);
                _attackRoutine = null;
            }

            if (attackHitbox != null)
            {
                attackHitbox.DisableDamage();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
