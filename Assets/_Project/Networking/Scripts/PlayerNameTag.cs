using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Handles 3D overhead player name tag display.
///
/// Features:
///   • Render through walls & obstacles (ZTest = Always) so player names are always visible!
///   • Crisp, clean White default name tag text — legible in any environment.
///   • Billboards continuously to face the active camera (100% legible at all angles).
///   • Safe transform tracking (works whether attached to prefab root or a child).
///   • Displays synchronized NetworkPlayerName.
///   • Hides tag when Vengeful Spirit is in stealth mode.
/// </summary>
public class PlayerNameTag : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("TextMeshPro 3D text element for the name tag.")]
    public TextMeshPro nameText;

    [Tooltip("Optional 2D UI TextMeshProUGUI if using a World Space Canvas.")]
    public TextMeshProUGUI nameTextUGUI;

    [Header("Settings")]
    [Tooltip("Vertical height offset above character transform.")]
    public float heightOffset = 2.2f;

    [Tooltip("Font size if name text is auto-created.")]
    public float fontSize = 4.5f;

    [Tooltip("Color for Investigator player name tags. Default is crisp White.")]
    public Color nameColor = Color.white;

    [Tooltip("If true, name tags render through walls and 3D environment objects.")]
    public bool showThroughWalls = true;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private NetworkPlayerName _netName;
    private GirlStealth       _stealthComponent;
    private Transform         _camTransform;
    private bool              _isVengefulSpirit = false;
    private Material          _customOverlayMaterial;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        EnsureNameTextExists();
        ApplyThroughWallsShader();
    }

    void Start()
    {
        EnsureNameTextExists();
        ApplyThroughWallsShader();

        _netName          = GetComponentInParent<NetworkPlayerName>();
        _stealthComponent = GetComponentInParent<GirlStealth>();
        _isVengefulSpirit = _stealthComponent != null;

        if (_netName != null)
        {
            _netName.playerName.OnValueChanged += OnNameChanged;
            UpdateNameText(_netName.playerName.Value.ToString());
        }
        else
        {
            UpdateNameText(_isVengefulSpirit ? "VENGEFUL SPIRIT" : "Investigator");
        }
    }

    void OnDestroy()
    {
        if (_netName != null)
        {
            _netName.playerName.OnValueChanged -= OnNameChanged;
        }

        if (_customOverlayMaterial != null)
        {
            Destroy(_customOverlayMaterial);
        }
    }

    void LateUpdate()
    {
        // 1. Resolve camera
        if (_camTransform == null && Camera.main != null)
            _camTransform = Camera.main.transform;

        // 2. Position offset — safely check transform.parent so zero NullReferenceException occurs
        Vector3 basePosition = (transform.parent != null) ? transform.parent.position : transform.position;

        if (nameText != null && nameText.transform != transform)
        {
            nameText.transform.position = basePosition + Vector3.up * heightOffset;
            if (_camTransform != null)
                nameText.transform.rotation = _camTransform.rotation;
        }
        else
        {
            if (transform.parent != null)
                transform.position = basePosition + Vector3.up * heightOffset;

            if (_camTransform != null)
                transform.rotation = _camTransform.rotation;
        }

        // 3. Stealth visibility check: hide name tag if Vengeful Spirit is vanished
        if (_isVengefulSpirit && _stealthComponent != null)
        {
            bool isStealth = _stealthComponent.IsStealthActive.Value;
            bool isOwner   = _stealthComponent.IsOwner;

            SetVisible(isOwner || !isStealth);
        }
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private void ApplyThroughWallsShader()
    {
        if (!showThroughWalls || nameText == null) return;

        // Instantiate material instance to set ZTest = Always (8) without modifying shared asset
        if (nameText.fontMaterial != null)
        {
            if (_customOverlayMaterial == null)
            {
                _customOverlayMaterial = new Material(nameText.fontMaterial);
                nameText.fontMaterial = _customOverlayMaterial;
            }
            // ZTest 8 = Always (Renders through walls and 3D objects)
            _customOverlayMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
    }

    private void EnsureNameTextExists()
    {
        if (nameText != null || nameTextUGUI != null) return;

        // Auto-create TextMeshPro 3D text component on a child object if unassigned
        GameObject textObj = new GameObject("NameTagText");
        textObj.transform.SetParent(transform, false);
        textObj.transform.localPosition = Vector3.up * heightOffset;
        textObj.transform.localRotation = Quaternion.identity;

        nameText = textObj.AddComponent<TextMeshPro>();
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.fontSize = fontSize;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = nameColor;
        nameText.sortingOrder = 100;

        ApplyThroughWallsShader();
    }

    private void OnNameChanged(Unity.Collections.FixedString64Bytes oldVal, Unity.Collections.FixedString64Bytes newVal)
    {
        UpdateNameText(newVal.ToString());
    }

    private void UpdateNameText(string displayName)
    {
        EnsureNameTextExists();

        string formattedName = displayName;
        Color textColor = nameColor; // Crisp White default

        if (_isVengefulSpirit)
        {
            formattedName = $"💀 {displayName} [VENGEFUL SPIRIT] 💀";
            textColor     = new Color(0.85f, 0.2f, 1f); // Glowing magenta/purple
        }

        if (nameText != null)
        {
            nameText.text  = formattedName;
            nameText.color = textColor;
        }

        if (nameTextUGUI != null)
        {
            nameTextUGUI.text  = formattedName;
            nameTextUGUI.color = textColor;
        }

        ApplyThroughWallsShader();
    }

    private void SetVisible(bool visible)
    {
        if (nameText != null) nameText.enabled = visible;
        if (nameTextUGUI != null) nameTextUGUI.enabled = visible;
    }
}
