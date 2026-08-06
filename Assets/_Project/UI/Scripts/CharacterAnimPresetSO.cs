using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SOLID — OCP: Data-driven ScriptableObject asset for character animation configuration.
/// 
/// Allows defining character animation sequences, dance steps, and idle gesture lists
/// as Unity project assets. You can create new presets, add/remove/rename states, and
/// change timing without writing or editing any C# code.
/// 
/// Create via: Assets -> Create -> Night Crawler -> Character Anim Preset
/// </summary>
[CreateAssetMenu(fileName = "NewCharacterAnimPreset", menuName = "Night Crawler/Character Anim Preset")]
public class CharacterAnimPresetSO : ScriptableObject
{
    [Header("Core Idle State")]
    [Tooltip("Exact name of the resting idle state in the Animator Controller.")]
    public string idleStateName = "Idle";

    [Header("Cinematic Intro Sequence")]
    [Tooltip("Sequence of animation steps played when the character preview opens. Add/remove/reorder steps at will.")]
    public List<CharacterAnimationController.AnimSequenceStep> introSequence = new List<CharacterAnimationController.AnimSequenceStep>();

    [Header("Dance Routine Sequence")]
    [Tooltip("Sequence of dance steps played for the girl or showcase screen.")]
    public List<CharacterAnimationController.AnimSequenceStep> danceSequence = new List<CharacterAnimationController.AnimSequenceStep>();

    [Header("Idle Gesture Loop")]
    [Tooltip("List of random gesture states played during linger idle.")]
    public List<string> gestureStateNames = new List<string>();

    [Header("Timing Configuration")]
    [Range(2f, 30f)] public float minGestureDelay = 5f;
    [Range(3f, 60f)] public float maxGestureDelay = 15f;
    [Range(2f, 20f)] public float minDanceRepeatDelay = 5f;
    [Range(3f, 40f)] public float maxDanceRepeatDelay = 12f;
}
