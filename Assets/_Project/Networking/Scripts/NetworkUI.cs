using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SOLID — SRP: Audio diagnostic helper only.
///
/// All lobby/connection UI logic has been moved to LobbyUI.cs.
/// This script retains only the engine audio test functionality used during development.
///
/// NOTE: You may safely delete this script once audio is confirmed working.
/// </summary>
public class NetworkUI : MonoBehaviour
{
    [Header("Audio Diagnostic")]
    [Tooltip("Assign a test AudioClip here to verify engine audio is working.")]
    public AudioClip testSound;

    private AudioSource _diagSource;

    private void Start()
    {
        _diagSource             = gameObject.AddComponent<AudioSource>();
        _diagSource.playOnAwake = false;
        _diagSource.spatialBlend = 0f; // 2D
        _diagSource.volume      = 1f;
    }

    /// <summary>
    /// Call from a UI button (or Inspector context menu) to play the test sound.
    /// </summary>
    [ContextMenu("Play Test Sound")]
    public void PlayTestSound()
    {
        if (testSound != null)
            _diagSource.PlayOneShot(testSound);
        else
            Debug.LogWarning("[NetworkUI] No 'Test Sound' assigned in the Inspector.");
    }
}