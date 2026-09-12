using UnityEngine;

/// <summary>
/// SOLID — SRP: Attached to the default Standing Idle state in the Animator Controller.
/// Periodically triggers 'IdleFidget' to play 'Standing Idle Looking Ver. 1',
/// making investigators look around alertly every few seconds instead of repeating a static loop.
/// Decoupled from character controller logic.
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

    private float _timer;
    private int _triggerHash;
    private int _indexHash;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        _timer = Random.Range(minIdleTime, maxIdleTime);
        _triggerHash = Animator.StringToHash(fidgetTriggerName);
        _indexHash = Animator.StringToHash(fidgetIndexParamName);
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            _timer = Random.Range(minIdleTime, maxIdleTime);

            if (totalFidgetVariations > 1)
            {
                int randomFidget = Random.Range(0, totalFidgetVariations);
                animator.SetInteger(_indexHash, randomFidget);
            }

            animator.SetTrigger(_triggerHash);
        }
    }
}
