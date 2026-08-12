using UnityEngine;
using Unity.Netcode.Components;

/// <summary>
/// SOLID — SRP: Reusable Character Animation System wrapping Unity Animator and NetworkAnimator.
/// Can be attached to any 3D character prefab (Explorer, Girl, Monster, Mannequin showcase).
/// </summary>
public class CharacterAnimationSystem : MonoBehaviour
{
    [Header("Animator References")]
    public Animator animator;
    public NetworkAnimator networkAnimator;

    [Header("Animation Presets (Optional)")]
    public CharacterAnimPresetSO animPreset;

    // Hashes
    private static readonly int SpeedHash       = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedHash    = Animator.StringToHash("Grounded");
    private static readonly int JumpHash        = Animator.StringToHash("Jump");
    private static readonly int AttackHash      = Animator.StringToHash("Attack");
    private static readonly int GestureHash     = Animator.StringToHash("Gesture");

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();
    }

    public void SetLocomotion(float speed, float motionSpeed = 1.0f)
    {
        if (animator == null) return;
        animator.SetFloat(SpeedHash, speed);
        animator.SetFloat(MotionSpeedHash, motionSpeed);
    }

    public void SetGrounded(bool isGrounded)
    {
        if (animator == null) return;
        animator.SetBool(GroundedHash, isGrounded);
    }

    public void TriggerJump()
    {
        TriggerAnimator(JumpHash);
    }

    public void TriggerAttack()
    {
        TriggerAnimator(AttackHash);
    }

    public void TriggerGesture(int gestureIndex = 1)
    {
        if (animator == null) return;
        animator.SetInteger(GestureHash, gestureIndex);
        TriggerAnimator(GestureHash);
    }

    private void TriggerAnimator(int paramHash)
    {
        if (networkAnimator != null && networkAnimator.IsSpawned)
        {
            networkAnimator.SetTrigger(paramHash);
        }
        else if (animator != null)
        {
            animator.SetTrigger(paramHash);
        }
    }
}
