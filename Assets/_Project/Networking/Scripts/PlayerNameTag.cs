using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Handles the 3D overhead player name tag display.
///
/// Features:
///   • Billboards to face the local camera.
///   • Displays synchronized NetworkPlayerName.
///   • Special identification design for the Vengeful Spirit (Demon Girl).
///   • Hides tag when Vengeful Spirit is in stealth invisibility.
/// </summary>
public class PlayerNameTag : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("TextMeshPro 3D text or UI text element for the name tag.")]
    public TextMeshPro nameText;

    [Tooltip("Optional 2D UI TextMeshProUGUI if using a World Space Canvas.")]
    public TextMeshProUGUI nameTextUGUI;

    [Header("Settings")]
    [Tooltip("Vertical height offset above character transform.")]
    public float heightOffset = 2.2f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private NetworkPlayerName _netName;
    private GirlStealth       _stealthComponent;
    private Transform         _camTransform;
    private bool              _isVengefulSpirit = false;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Start()
    {
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
            UpdateNameText(_isVengefulSpirit ? "💀 VENGEFUL SPIRIT 💀" : "Investigator");
        }
    }

    void OnDestroy()
    {
        if (_netName != null)
        {
            _netName.playerName.OnValueChanged -= OnNameChanged;
        }
    }

    void LateUpdate()
    {
        // Billboard facing main camera
        if (_camTransform == null && Camera.main != null)
            _camTransform = Camera.main.transform;

        if (_camTransform != null)
        {
            transform.position = transform.parent.position + Vector3.up * heightOffset;
            transform.rotation = Quaternion.LookRotation(transform.position - _camTransform.position);
        }

        // Stealth visibility check: hide name tag if Vengeful Spirit is vanished
        if (_isVengefulSpirit && _stealthComponent != null)
        {
            bool isStealth = _stealthComponent.IsStealthActive.Value;
            bool isOwner   = _stealthComponent.IsOwner;

            // Only hide tag from other players when in stealth
            SetVisible(isOwner || !isStealth);
        }
    }

    // =========================================================================
    //  Helpers
    // =========================================================================
    private void OnNameChanged(Unity.Collections.FixedString64Bytes oldVal, Unity.Collections.FixedString64Bytes newVal)
    {
        UpdateNameText(newVal.ToString());
    }

    private void UpdateNameText(string displayName)
    {
        string formattedName = displayName;
        Color textColor = new Color(0.2f, 0.8f, 1f); // Investigator cyan

        if (_isVengefulSpirit)
        {
            formattedName = $"💀 {displayName} [VENGEFUL SPIRIT] 💀";
            textColor     = new Color(0.85f, 0.2f, 1f); // Vengeful Spirit glowing magenta/purple
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
    }

    private void SetVisible(bool visible)
    {
        if (nameText != null) nameText.enabled = visible;
        if (nameTextUGUI != null) nameTextUGUI.enabled = visible;
    }
}
