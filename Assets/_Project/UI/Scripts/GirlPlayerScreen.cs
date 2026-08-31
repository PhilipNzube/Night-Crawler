using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// SOLID — SRP: The exclusive cinematic screen shown only to the player chosen
///              as the Vengeful Spirit / "Girl".
/// </summary>
public class GirlPlayerScreen : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — Environment
    // -------------------------------------------------------------------------
    [Header("Environment")]
    [Tooltip("Root panel of this screen. Enabled only for the girl player after reveal.")]
    public GameObject girlScreenPanel;

    [Tooltip("Camera that looks at the girl's character model on this screen.")]
    public Camera girlScreenCamera;

    // -------------------------------------------------------------------------
    //  Inspector — Character Model
    // -------------------------------------------------------------------------
    [Header("Character Model")]
    [Tooltip("Transform pivot in the scene where the girl's model is spawned.")]
    public Transform modelPivot;

    [Tooltip("Direct prefab override for the girl model. Used if GameManager.girlPrefab is null.")]
    public GameObject girlPrefabOverride;

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

    [Tooltip("Seconds after Show() before the READY button appears.")]
    public float readyButtonDelay = 3.5f;

    [Header("Player Status Panel (Live Investigator Status)")]
    [Tooltip("Optional: root panel to show all players' ready status on the girl screen.")]
    public GameObject playerStatusPanel;
    [Tooltip("Vertical container inside playerStatusPanel for per-player rows.")]
    public Transform playerStatusContainer;
    [Tooltip("Prefab for a single row: must have 2 TMP_Text children — [0]=name, [1]=status.")]
    public GameObject playerStatusRowPrefab;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private GameObject                   _modelInstance;
    private CharacterAnimationController _animController;
    private Coroutine                    _readyDelayCoroutine;
    private bool                         _readySent = false;
    private readonly List<GameObject>    _statusRows = new List<GameObject>();

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (girlScreenPanel == null)
            girlScreenPanel = gameObject;

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyPressed);
        }
    }

    void OnEnable()
    {
        Show();
    }

    void OnDisable()
    {
        Hide();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    public void Show()
    {
        _readySent = false;
        PersistentCharacterSelection.SetIsVengefulSpirit(true);

        // Ensure CharacterSelectUI and white room are disabled so nothing overlaps the girl screen
        CharacterSelectUI selectUI = FindFirstObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
        if (selectUI != null && selectUI.gameObject.activeSelf)
            selectUI.gameObject.SetActive(false);

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();

        SetScreenVisible(true);
        PopulateTexts();
        SpawnGirlModel();

        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.GirlScreen);

        if (_readyDelayCoroutine != null) StopCoroutine(_readyDelayCoroutine);
        _readyDelayCoroutine = StartCoroutine(EnableReadyButtonAfterDelay());

        // Subscribe to live ready-state updates so investigators' status is visible here too
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.OnReadyStatesUpdated += HandleReadyStatesUpdated;
    }

    public void Hide()
    {
        SetScreenVisible(false);
        DestroyModel();

        if (_readyDelayCoroutine != null)
        {
            StopCoroutine(_readyDelayCoroutine);
            _readyDelayCoroutine = null;
        }

        // Unsubscribe
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
    }

    // =========================================================================
    //  Private
    // =========================================================================

    private void PopulateTexts()
    {
        if (roleTitleText != null)
            roleTitleText.text = "VENGEFUL SPIRIT";

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

        // 1. Resolve Pivot
        Transform pivot = modelPivot;
        if (pivot == null)
        {
            Transform childPivot = transform.Find("ModelPivot");
            pivot = childPivot != null ? childPivot : transform;
            Debug.Log($"[GirlPlayerScreen] modelPivot unassigned — using fallback: {pivot.name}");
        }

        // 2. Resolve Prefab
        GameObject prefab = girlPrefabOverride;
        if (prefab == null && GameManager.Instance != null)
            prefab = GameManager.Instance.girlPrefab;

        if (prefab == null)
        {
            Debug.LogWarning("[GirlPlayerScreen] CRITICAL: No Girl Prefab assigned! " +
                             "Assign 'girlPrefabOverride' on GirlPlayerScreen or 'girlPrefab' on GameManager.");
            return;
        }

        Debug.Log($"[GirlPlayerScreen] Spawning Girl Model: {prefab.name} at pivot {pivot.name}");
        _modelInstance = Instantiate(prefab, pivot.position, pivot.rotation, pivot);

        // Disable gameplay components — only keep CharacterAnimationController
        foreach (MonoBehaviour mb in _modelInstance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (mb is CharacterAnimationController) continue;
            mb.enabled = false;
        }

        // Ensure CharacterAnimationController is present and set to Girl
        _animController = _modelInstance.GetComponent<CharacterAnimationController>();
        if (_animController == null)
            _animController = _modelInstance.AddComponent<CharacterAnimationController>();

        _animController.characterType = CharacterAnimationController.CharacterType.Girl;
        _animController.PlayDanceLoop();
    }

    private IEnumerator EnableReadyButtonAfterDelay()
    {
        if (readyButton != null) readyButton.gameObject.SetActive(false);
        yield return new WaitForSecondsRealtime(readyButtonDelay);
        if (readyButton != null)
        {
            readyButton.gameObject.SetActive(true);
            readyButton.interactable = true;
        }
        _readyDelayCoroutine = null;
    }

    private void OnReadyPressed()
    {
        if (_readySent) return;
        _readySent = true;

        if (readyButton != null) readyButton.interactable = false;

        if (waitingText != null)
            waitingText.text = "Ready! Waiting for the investigators to finish...";

        // Primary path: PlayerReadyTracker (broadcasts status to all clients)
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.ReportGirlReadyServerRpc();

        // Legacy fallback: GirlRevealManager (keeps old scene-load logic in sync)
        if (GirlRevealManager.Instance != null)
            GirlRevealManager.Instance.ReportGirlReadyServerRpc();
        else
            Debug.LogWarning("[GirlPlayerScreen] GirlRevealManager.Instance is null — ready signal not sent.");
    }

    private void HandleReadyStatesUpdated(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        if (playerStatusContainer == null || playerStatusRowPrefab == null) return;

        foreach (var row in _statusRows)
            if (row != null) Destroy(row);
        _statusRows.Clear();

        foreach (var kvp in snapshot)
        {
            GameObject row = Instantiate(playerStatusRowPrefab, playerStatusContainer);
            _statusRows.Add(row);
            var texts = row.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            if (texts.Length >= 1) texts[0].text = kvp.Value.name;
            if (texts.Length >= 2) texts[1].text = kvp.Value.ready ? "✓ Ready" : "Selecting...";
        }

        if (playerStatusPanel != null)
            playerStatusPanel.SetActive(_statusRows.Count > 0);
    }

    private void SetScreenVisible(bool visible)
    {
        if (girlScreenPanel != null && girlScreenPanel != gameObject)
            girlScreenPanel.SetActive(visible);

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
