using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// SOLID — SRP: The exclusive cinematic screen shown only to the player chosen
///              as the Vengeful Spirit / "Girl".
///
/// This player is separated from the investigator flow entirely.
/// While investigators choose characters and view the squad lineup, the girl
/// player sees this screen with her character dancing and a READY button.
/// 
/// The game scene only loads once BOTH this player presses READY and all
/// investigators complete the squad countdown (tracked by GirlRevealManager).
///
/// ─── SETUP ────────────────────────────────────────────────────────────────────
///  1. Create a full-screen Canvas panel (girlScreenPanel).
///  2. Add a Camera dedicated to this screen (girlScreenCamera).
///  3. Place a Transform pivot in the scene at the desired model position (modelPivot).
///  4. Wire all fields below.
///  5. Keep girlScreenPanel and girlScreenCamera disabled initially.
///  6. The girlFlow root GameObject (in GirlRevealManager) should reference this
///     object's root — enabling it will call OnEnable which calls Show().
/// </summary>
public class GirlPlayerScreen : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — Environment
    // -------------------------------------------------------------------------
    [Header("Environment")]
    [Tooltip("Root panel of this screen. Enabled only for the girl player after reveal.")]
    public GameObject girlScreenPanel;

    [Tooltip("Camera that looks at the girl's character model on this screen. " +
             "Enabled when screen is active; disabled otherwise.")]
    public Camera girlScreenCamera;

    // -------------------------------------------------------------------------
    //  Inspector — Character Model
    // -------------------------------------------------------------------------
    [Header("Character Model")]
    [Tooltip("Transform pivot in the scene where the girl's model is spawned.")]
    public Transform modelPivot;

    // -------------------------------------------------------------------------
    //  Inspector — UI Text
    // -------------------------------------------------------------------------
    [Header("UI — Text")]
    [Tooltip("Large title label. Displays the role name.")]
    public TextMeshProUGUI roleTitleText;

    [Tooltip("Flavour / lore text panel describing the girl's role.")]
    public TextMeshProUGUI flavourText;

    [Tooltip("Status line that changes after READY is pressed.")]
    public TextMeshProUGUI waitingText;

    // -------------------------------------------------------------------------
    //  Inspector — READY Button
    // -------------------------------------------------------------------------
    [Header("UI — Ready Button")]
    [Tooltip("Button the girl presses when she is ready for the game to start.")]
    public Button readyButton;

    [Tooltip("Seconds after Show() before the READY button appears. " +
             "Prevents accidental instant presses.")]
    public float readyButtonDelay = 3.5f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private GameObject                  _modelInstance;
    private CharacterAnimationController _animController;
    private Coroutine                   _readyDelayCoroutine;
    private bool                        _readySent = false;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        SetScreenVisible(false);

        if (readyButton != null)
        {
            readyButton.onClick.AddListener(OnReadyPressed);
            readyButton.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        // Called when GirlRevealManager activates the girlFlow root
        Show();
    }

    void OnDisable()
    {
        Hide();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>Activates the girl screen, spawns her model with dance animation.</summary>
    public void Show()
    {
        _readySent = false;
        SetScreenVisible(true);
        PopulateTexts();
        SpawnGirlModel();

        // Switch to the girl's dedicated Cinemachine camera
        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.GirlScreen);

        if (_readyDelayCoroutine != null) StopCoroutine(_readyDelayCoroutine);
        _readyDelayCoroutine = StartCoroutine(EnableReadyButtonAfterDelay());
    }

    /// <summary>Hides the screen and cleans up the spawned model.</summary>
    public void Hide()
    {
        SetScreenVisible(false);
        DestroyModel();

        if (_readyDelayCoroutine != null)
        {
            StopCoroutine(_readyDelayCoroutine);
            _readyDelayCoroutine = null;
        }
    }

    // =========================================================================
    //  Private
    // =========================================================================

    private void PopulateTexts()
    {
        if (roleTitleText != null)
            roleTitleText.text = "✦  VENGEFUL SPIRIT  ✦";

        if (flavourText != null)
            flavourText.text =
                "The mine took everything from you.\n\n" +
                "Seep into the shadows.\n" +
                "Manipulate the lights. Whisper lies.\n" +
                "Turn the investigators against each other.\n\n" +
                "Make them suffer.";

        if (waitingText != null)
            waitingText.text = "The investigators are assembling their squad...";
    }

    private void SpawnGirlModel()
    {
        DestroyModel();
        if (modelPivot == null) return;

        GameObject prefab = GameManager.Instance != null ? GameManager.Instance.girlPrefab : null;
        if (prefab == null)
        {
            Debug.LogWarning("[GirlPlayerScreen] No girl prefab found on GameManager. " +
                             "Assign girlPrefab in the Inspector.");
            return;
        }

        _modelInstance = Instantiate(prefab, modelPivot.position, modelPivot.rotation, modelPivot);

        // Disable gameplay components — only keep CharacterAnimationController
        foreach (MonoBehaviour mb in _modelInstance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is CharacterAnimationController) continue;
            mb.enabled = false;
        }

        // Ensure CharacterAnimationController is present
        _animController = _modelInstance.GetComponent<CharacterAnimationController>();
        if (_animController == null)
            _animController = _modelInstance.AddComponent<CharacterAnimationController>();

        _animController.characterType = CharacterAnimationController.CharacterType.Girl;
        // Dance plays once on entry, then she idles, then dances again naturally
        _animController.PlayDanceLoop();
    }

    private IEnumerator EnableReadyButtonAfterDelay()
    {
        if (readyButton != null) readyButton.gameObject.SetActive(false);
        yield return new WaitForSecondsRealtime(readyButtonDelay);
        if (readyButton != null) readyButton.gameObject.SetActive(true);
        _readyDelayCoroutine = null;
    }

    private void OnReadyPressed()
    {
        if (_readySent) return;
        _readySent = true;

        if (readyButton != null) readyButton.interactable = false;

        if (waitingText != null)
            waitingText.text = "Ready! Waiting for the investigators to finish...";

        // Signal the server that the girl player is ready
        if (GirlRevealManager.Instance != null)
            GirlRevealManager.Instance.ReportGirlReadyServerRpc();
        else
            Debug.LogWarning("[GirlPlayerScreen] GirlRevealManager.Instance is null — ready signal not sent.");
    }

    private void SetScreenVisible(bool visible)
    {
        if (girlScreenPanel != null)
        {
            girlScreenPanel.SetActive(visible);
        }
        else if (visible)
        {
            Debug.LogWarning("[GirlPlayerScreen] WARNING: 'girlScreenPanel' is NOT assigned in the Inspector! " +
                             "Drag the UI panel for the Girl Player Screen into this field.");
        }

        if (girlScreenCamera != null)
            girlScreenCamera.enabled = visible;
    }

    private void DestroyModel()
    {
        if (_modelInstance != null)
        {
            Destroy(_modelInstance);
            _modelInstance  = null;
            _animController = null;
        }
    }
}
