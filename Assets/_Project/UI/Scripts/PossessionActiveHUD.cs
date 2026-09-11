using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Renders the active possession HUD overlay on the Girl player's display.
/// Shows target name, remaining time, and a prominent on-screen "RELEASE BODY [E]" button.
/// Appears only while actively possessing an investigator.
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

    [Header("UI Elements")]
    public GameObject hudContainer;
    public TMP_Text victimNameText;
    public TMP_Text timeRemainingText;
    public Button releaseButton;

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

        if (releaseButton != null)
        {
            releaseButton.onClick.AddListener(OnReleaseClicked);
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!_isActive || _activeGirlPossession == null) return;

        // Update remaining time
        if (timeRemainingText != null)
        {
            float rem = _activeGirlPossession.RemainingPool;
            timeRemainingText.text = $"{Mathf.CeilToInt(rem)}s left";
        }

        // Allow cursor to be free when moving towards the top-center release HUD area or holding Alt
        bool isHoveringReleaseArea = false;
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
        if (hudContainer != null)
        {
            RectTransform rt = hudContainer.GetComponent<RectTransform>();
            if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos))
            {
                isHoveringReleaseArea = true;
            }
        }
        
        // Also check if cursor is in the top-center zone (width 440px, top 120px)
        if (!isHoveringReleaseArea)
        {
            if (mousePos.y >= (Screen.height - 120f) && Mathf.Abs(mousePos.x - Screen.width * 0.5f) <= 220f)
            {
                isHoveringReleaseArea = true;
            }
        }

        bool altHeld = (Keyboard.current != null && Keyboard.current.leftAltKey.isPressed) || Input.GetKey(KeyCode.LeftAlt);

        if (isHoveringReleaseArea || altHeld)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (_activeGirlPossession.RemainingPool <= 0.05f)
        {
            OnReleaseClicked();
            return;
        }

        // Hotkey: E or Keyboard E to release possession
        if ((Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) || Input.GetKeyDown(KeyCode.E))
        {
            OnReleaseClicked();
        }
    }

    public void Show(string victimName, GirlPossession girlPossession)
    {
        _activeGirlPossession = girlPossession;
        _isActive = true;

        if (victimNameText != null)
        {
            victimNameText.text = $"<color=#FF4444>|</color> [POSSESSING]: {victimName}";
        }

        if (hudContainer != null)
        {
            hudContainer.SetActive(true);
        }
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        _isActive = false;
        _activeGirlPossession = null;

        if (hudContainer != null)
        {
            hudContainer.SetActive(false);
        }

        // Re-lock cursor when possession ends
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
