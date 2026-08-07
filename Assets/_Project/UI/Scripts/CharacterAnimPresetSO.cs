using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SOLID — OCP: Data-driven ScriptableObject asset for character animation configuration.
/// 
/// Allows defining character animation sequences, dance steps, idle gesture lists,
/// root motion settings, and random dance rotation behaviour without editing C# code.
/// 
/// Create via: Assets -> Create -> Night Crawler -> Character Anim Preset
/// </summary>
[CreateAssetMenu(fileName = "NewCharacterAnimPreset", menuName = "Night Crawler/Character Anim Preset")]
public class CharacterAnimPresetSO : ScriptableObject
{
    [Header("Root Motion Settings")]
    [Tooltip("If true, Animator.applyRootMotion is enabled on preview models using this preset.")]
    public bool enableRootMotionInPreview = true;

    [Header("Core Idle State")]
    [Tooltip("Exact name of the resting idle state in the Animator Controller.")]
    public string idleStateName = "Idle";

    [Header("Cinematic Intro Sequence")]
    [Tooltip("Sequence of animation steps played when the character preview opens.")]
    public List<CharacterAnimationController.AnimSequenceStep> introSequence = new List<CharacterAnimationController.AnimSequenceStep>();

    [Header("Dance Routine Sequence")]
    [Tooltip("Sequence of dance steps played for the girl or showcase screen.")]
    public List<CharacterAnimationController.AnimSequenceStep> danceSequence = new List<CharacterAnimationController.AnimSequenceStep>();

    [Header("Dance Random Rotation")]
    [Tooltip("If true, character smoothly turns to random facing angles while dancing.")]
    public bool enableRandomRotationOnDance = true;
    public float minTurnInterval = 1.5f;
    public float maxTurnInterval = 4.0f;
    public float maxTurnAngle = 70f;

    [Header("Idle Gesture Loop")]
    [Tooltip("List of random gesture states played during linger idle.")]
    public List<string> gestureStateNames = new List<string>();

    [Header("Timing Configuration")]
    [Range(2f, 30f)] public float minGestureDelay = 5f;
    [Range(3f, 60f)] public float maxGestureDelay = 15f;
    [Range(2f, 20f)] public float minDanceRepeatDelay = 5f;
    [Range(3f, 40f)] public float maxDanceRepeatDelay = 12f;
}
