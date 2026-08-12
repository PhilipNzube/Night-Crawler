using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine.InputSystem;

/// <summary>
/// GirlAttackNet — Combat abilities have been disabled for the Girl character.
/// This component now strictly acts as a dummy/disabled handler so any residual animation events do not trigger damage.
/// </summary>
public class GirlAttackNet : NetworkBehaviour
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;

    [Header("References")]
    public Transform leftHandTransform;
    public Transform rightHandTransform;

    void Awake()
    {
        // Auto find hands for visual compatibility if needed
        AutoFindHands();
    }

    private void OnValidate()
    {
        AutoFindHands();
    }

    private void AutoFindHands()
    {
        if (leftHandTransform == null) leftHandTransform = FindDeepChild(transform, "ik_hand_l");
        if (rightHandTransform == null) rightHandTransform = FindDeepChild(transform, "ik_hand_r");
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    void Update()
    {
        // Combat attack ability removed from Girl character. Left click attack input is ignored.
    }

    // Animation Event Hook — Does nothing as combat ability is removed.
    public void OnAttackSwipe(int handIndex)
    {
        // Disabled combat ability — no VFX spawn or damage checks executed.
    }
}
