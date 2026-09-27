using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Renders the active possession HUD overlay on the Girl player's display.
/// Shows target victim name, formatted remaining time (00:00), and dynamic exit prompt
/// that adapts between keyboard [E] and official Heat UI controller icons.
/// </summary>
public class PossessionActiveHUD : MonoBehaviour
{
    private static PossessionActiveHUD _instance;
    public static PossessionActiveHUD Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<PossessionActiveHUD>(FindObjectsInactive.Include);
                if (_instance != null && !_instance.gameObject.activeInHierarchy)
                {
                    _instance.gameObject.SetActive(true);
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Core UI Elements")]
    public GameObject hudContainer;
    [Tooltip("Text displaying the possessed victim's name.")]
    public TMP_Text victimNameText;
    [Tooltip("Text displaying the formatted timer (00:00).")]
    public TMP_Text timeRemainingText;
    [Tooltip("Legacy/Fallback single-line text hint displaying the exit prompt.")]
    public TMP_Text exitPromptText;

    [Header("New Dynamic Exit Prompt (Hierarchy)")]
    [Tooltip("Root Transform of the new ExitPromptText UI object.")]
    public RectTransform newExitPromptText;
    [Tooltip("Child container for gamepad controller icon (Icon Parent).")]
    public GameObject iconParent;
    [Tooltip("Image component displaying the controller button sprite (Icon).")]
    public Image exitGamepadIcon;
    [Tooltip("Child container for keyboard key display (Text Parent).")]
    public GameObject textParent;
    [Tooltip("Text component displaying the keyboard key inside Text Parent (e.g. 'E').")]
    public TMP_Text exitKeyText;

    private GirlPossession _activeGirlPossession;
    private bool _isActive = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureReferences();
        Hide();
    }

    private void OnEnable()
    {
        KeybindingManager.OnBindingsChanged += UpdatePromptDisplay;
        KeybindingManager.OnDeviceTypeChanged += OnDeviceTypeChanged;
    }

    private void OnDisable()
    {
        KeybindingManager.OnBindingsChanged -= UpdatePromptDisplay;
        KeybindingManager.OnDeviceTypeChanged -= OnDeviceTypeChanged;
    }

    private void OnDeviceTypeChanged(bool isGamepad)
    {
        UpdatePromptDisplay();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        EnsureReferences();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EnsureReferences();
        }
    }
#endif

    public void EnsureReferences()
    {
        if (hudContainer == null) hudContainer = gameObject;

        // Victim name reference
        if (victimNameText == null)
        {
            var pName = transform.Find("VictimName/PlayerName");
            if (pName != null) victimNameText = pName.GetComponent<TMP_Text>();
        }

        // Timer reference
        if (timeRemainingText == null)
        {
            var tName = transform.Find("TimerText/Time");
            if (tName != null) timeRemainingText = tName.GetComponent<TMP_Text>();
        }

        // Old prompt reference
        if (exitPromptText == null)
        {
            var oldPrompt = transform.Find("ExitPromptText_Old/NotificationText") ?? transform.Find("ExitPromptText/NotificationText");
            if (oldPrompt != null) exitPromptText = oldPrompt.GetComponent<TMP_Text>();
        }

        // New prompt reference
        if (newExitPromptText == null)
        {
            var prompt = transform.Find("ExitPromptText");
            if (prompt != null) newExitPromptText = prompt.GetComponent<RectTransform>();
        }

        if (newExitPromptText != null)
        {
            if (iconParent == null)
            {
                var ip = newExitPromptText.Find("Icon Parent");
                if (ip != null) iconParent = ip.gameObject;
            }

            if (exitGamepadIcon == null && iconParent != null)
            {
                exitGamepadIcon = iconParent.GetComponentInChildren<Image>(true);
            }

            if (textParent == null)
            {
                var tp = newExitPromptText.Find("Text Parent");
                if (tp != null) textParent = tp.gameObject;
            }

            if (exitKeyText == null && textParent != null)
            {
                var txt = textParent.transform.Find("Text");
                if (txt != null) exitKeyText = txt.GetComponent<TMP_Text>();
                else exitKeyText = textParent.GetComponentInChildren<TMP_Text>(true);
            }
        }
    }

    private void Update()
    {
        if (!_isActive || _activeGirlPossession == null) return;

        // Format remaining time as 00:00 (MM:SS)
        float rem = Mathf.Max(0f, _activeGirlPossession.RemainingPool);
        int minutes = Mathf.FloorToInt(rem / 60f);
        int seconds = Mathf.FloorToInt(rem % 60f);
        if (timeRemainingText != null)
        {
            timeRemainingText.text = $"{minutes:00}:{seconds:00}";
        }

        // Handle user exit action
        if (KeybindingManager.IsActionTriggered("ExitPossession"))
        {
            OnReleaseClicked();
            return;
        }

        if (_activeGirlPossession.RemainingPool <= 0.05f)
        {
            OnReleaseClicked();
            return;
        }
    }

    public void Show(string victimName, GirlPossession girlPossession)
    {
        EnsureReferences();
        _activeGirlPossession = girlPossession;
        _isActive = true;

        if (victimNameText != null)
        {
            victimNameText.text = !string.IsNullOrEmpty(victimName) ? victimName.ToUpper() : "UNKNOWN";
        }

        UpdatePromptDisplay();

        if (hudContainer != null)
        {
            hudContainer.SetActive(true);
        }
        gameObject.SetActive(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void UpdatePromptDisplay()
    {
        EnsureReferences();

        bool isGamepad = KeybindingManager.IsGamepadActive();

        if (isGamepad)
        {
            if (iconParent != null) iconParent.SetActive(true);
            if (textParent != null) textParent.SetActive(false);

            if (exitGamepadIcon != null)
            {
                Sprite sprite = KeybindingManager.GetGamepadSprite("ExitPossession");
                if (sprite != null)
                {
                    exitGamepadIcon.sprite = sprite;
                    exitGamepadIcon.enabled = true;
                }
            }
        }
        else
        {
            if (iconParent != null) iconParent.SetActive(false);
            if (textParent != null) textParent.SetActive(true);

            if (exitKeyText != null)
            {
                exitKeyText.text = KeybindingManager.GetBoundKeyString("ExitPossession", "E");
            }
        }

        if (exitPromptText != null)
        {
            exitPromptText.text = $"{KeybindingManager.GetActionPrompt("ExitPossession", "Press")} to exit body";
        }
    }

    public void Hide()
    {
        _isActive = false;
        _activeGirlPossession = null;

        if (hudContainer != null)
        {
            hudContainer.SetActive(false);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnReleaseClicked()
    {
        if (_activeGirlPossession != null)
        {
            _activeGirlPossession.RequestRelease();
        }
        Hide();
    }
}
