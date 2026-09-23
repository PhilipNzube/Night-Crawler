using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SOLID — SRP: Attached to the default Standing Idle / Locomotion states in the Animator Controller.
/// Periodically triggers 'IdleFidget' to play 'Standing Idle Looking Ver. 1',
/// making investigators look around alertly every few seconds instead of repeating a static loop.
///
/// RULES:
/// 1. Never plays on the Girl character (Vengeful Spirit).
/// 2. Never plays while characters are moving (normally or with weapons) or in any non-idle state (jumping, falling, attacking).
/// </summary>
public class RandomIdleFidgetSMB : StateMachineBehaviour
{
    [Header("Timing")]
    [Tooltip("Minimum time (seconds) in base idle before playing the looking around animation.")]
    public float minIdleTime = 6f;

    [Tooltip("Maximum time (seconds) in base idle before playing the looking around animation.")]
    public float maxIdleTime = 12f;

    [Header("Multiple Variations")]
    [Tooltip("Number of distinct idle fidget variations available (e.g., 1 = only Look Around, 3 = Look, Check Weapon, Stretch).")]
    public int totalFidgetVariations = 1;

    [Header("Animator Parameters")]
    [Tooltip("Trigger parameter name in the Animator Controller.")]
    public string fidgetTriggerName = "IdleFidget";

    [Tooltip("Integer parameter name for selecting which fidget to play (0, 1, 2...). Ignored if totalFidgetVariations <= 1.")]
    public string fidgetIndexParamName = "FidgetIndex";

    [Header("Idle Detection")]
    [Tooltip("Movement speed threshold above which the character is considered moving/not idle.")]
    public float speedThreshold = 0.1f;

    // Per-animator state tracking (prevents multi-instance conflicts in multiplayer)
    private static readonly Dictionary<int, float> _timers = new Dictionary<int, float>();
    private static readonly HashSet<int> _girlAnimatorIds = new HashSet<int>();
    private static readonly HashSet<int> _investigatorAnimatorIds = new HashSet<int>();

    private int _triggerHash;
    private int _indexHash;
    private int _speedHash;
    private int _motionSpeedHash;
    private int _groundedHash;
    private int _jumpHash;
    private int _freeFallHash;
    private int _comboStepHash;

    private bool _hasSpeedParam;
    private bool _hasMotionSpeedParam;
    private bool _hasGroundedParam;
    private bool _hasJumpParam;
    private bool _hasFreeFallParam;
    private bool _hasComboStepParam;
    private bool _paramsInitialized;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (animator == null) return;
        InitParameters(animator);
        int animId = animator.GetInstanceID();

        if (IsGirl(animator, animId))
        {
            animator.ResetTrigger(_triggerHash);
            return;
        }

        _timers[animId] = Random.Range(minIdleTime, maxIdleTime);
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (animator == null) return;
        int animId = animator.GetInstanceID();
        _timers[animId] = Random.Range(minIdleTime, maxIdleTime);
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (animator == null) return;
        int animId = animator.GetInstanceID();

        // 1. RULE: The Girl character must NEVER play the idleFidget animation
        if (IsGirl(animator, animId))
        {
            animator.ResetTrigger(_triggerHash);
            return;
        }

        // 2. RULE: While moving (normally or with weapons) or in any non-idle state, do NOT play fidget
        if (!IsCharacterIdle(animator))
        {
            // Reset the countdown timer so the fidget countdown only begins when the character is standing completely idle
            _timers[animId] = Random.Range(minIdleTime, maxIdleTime);

            // Clear any lingering trigger
            animator.ResetTrigger(_triggerHash);
            return;
        }

        // 3. Count down only while continuously in the Idle state
        if (!_timers.TryGetValue(animId, out float t))
        {
            t = Random.Range(minIdleTime, maxIdleTime);
        }

        t -= Time.deltaTime;
        if (t <= 0f)
        {
            t = Random.Range(minIdleTime, maxIdleTime);

            // Re-verify that the character is still strictly idle right now before triggering
            if (IsCharacterIdle(animator))
            {
                if (totalFidgetVariations > 1)
                {
                    int randomFidget = Random.Range(0, totalFidgetVariations);
                    animator.SetInteger(_indexHash, randomFidget);
                }

                animator.SetTrigger(_triggerHash);
            }
        }

        _timers[animId] = t;
    }

    private void InitParameters(Animator animator)
    {
        if (_paramsInitialized) return;
        _paramsInitialized = true;

        _triggerHash = Animator.StringToHash(fidgetTriggerName);
        _indexHash = Animator.StringToHash(fidgetIndexParamName);
        _speedHash = Animator.StringToHash("Speed");
        _motionSpeedHash = Animator.StringToHash("MotionSpeed");
        _groundedHash = Animator.StringToHash("Grounded");
        _jumpHash = Animator.StringToHash("Jump");
        _freeFallHash = Animator.StringToHash("FreeFall");
        _comboStepHash = Animator.StringToHash("ComboStep");

        if (animator != null)
        {
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == _speedHash) _hasSpeedParam = true;
                else if (p.nameHash == _motionSpeedHash) _hasMotionSpeedParam = true;
                else if (p.nameHash == _groundedHash) _hasGroundedParam = true;
                else if (p.nameHash == _jumpHash) _hasJumpParam = true;
                else if (p.nameHash == _freeFallHash) _hasFreeFallParam = true;
                else if (p.nameHash == _comboStepHash) _hasComboStepParam = true;
            }
        }
    }

    private bool IsCharacterIdle(Animator animator)
    {
        if (animator == null) return false;
        InitParameters(animator);

        // A. Movement Speed: If character is moving at all (normal or armed), not idle
        if (_hasSpeedParam && animator.GetFloat(_speedHash) > speedThreshold)
            return false;

        // B. Motion Speed / Input magnitude: If movement input is being applied, not idle
        if (_hasMotionSpeedParam && animator.GetFloat(_motionSpeedHash) > speedThreshold)
            return false;

        // C. Physical velocity: If CharacterController is moving, not idle
        var cc = animator.GetComponentInParent<CharacterController>();
        if (cc != null && cc.velocity.sqrMagnitude > (speedThreshold * speedThreshold))
            return false;

        // D. Airborne / Jumping / Free Fall: Must be grounded and not jumping/falling
        if (_hasGroundedParam && !animator.GetBool(_groundedHash))
            return false;
        if (_hasJumpParam && animator.GetBool(_jumpHash))
            return false;
        if (_hasFreeFallParam && animator.GetBool(_freeFallHash))
            return false;

        // E. Attacks / Combos: Cannot fidget while attacking
        if (_hasComboStepParam && animator.GetInteger(_comboStepHash) > 0)
            return false;

        return true;
    }

    private static bool IsGirl(Animator animator, int animId)
    {
        if (_girlAnimatorIds.Contains(animId)) return true;
        if (_investigatorAnimatorIds.Contains(animId)) return false;

        bool isGirl = CheckIfGirlHierarchy(animator);
        if (isGirl)
        {
            _girlAnimatorIds.Add(animId);
        }
        else
        {
            _investigatorAnimatorIds.Add(animId);
        }

        return isGirl;
    }

    private static bool CheckIfGirlHierarchy(Animator animator)
    {
        if (animator == null) return false;

        var root = animator.transform.root;
        if (root != null)
        {
            if (root.GetComponentInChildren<GirlMovement>(true) != null) return true;
            if (root.GetComponentInChildren<GirlPossession>(true) != null) return true;
            if (root.GetComponentInChildren<GirlMaterialController>(true) != null) return true;
            if (root.GetComponentInChildren<GirlAttackNet>(true) != null) return true;

            string rootName = root.gameObject.name;
            if (rootName.IndexOf("girl", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                rootName.IndexOf("demon", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                rootName.IndexOf("vengeful", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        string objName = animator.gameObject.name;
        if (objName.IndexOf("girl", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            objName.IndexOf("demon", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            objName.IndexOf("vengeful", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }
}
