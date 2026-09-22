using UnityEngine;
using TMPro;
using Michsky.UI.Heat;

/// <summary>
/// Component attached to an Input Field (or its parent GameObject) to control
/// whether emojis and pictorial symbols are accepted in the text field.
///
/// Features:
/// - Checkbox toggle in the Inspector ("Allow Emojis" / "Accept Emojis")
/// - Prevents typing / insertion of surrogate pairs & emoji code points when unchecked
/// - Strips pasted emojis in real-time
/// - Clean integration with Michsky InputFieldManager and standard TMP_InputField
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Night Crawler/UI/Emoji Input Filter")]
public class EmojiInputFilter : MonoBehaviour
{
    [Header("Emoji Configuration")]
    [Tooltip("Check this box to activate accepting emojis in this text field. Uncheck to disallow emojis.")]
    [SerializeField] private bool allowEmojis = false;

    private TMP_InputField _inputField;
    private InputFieldManager _heatInputField;
    private bool _isSanitizing;

    public bool AllowEmojis
    {
        get => allowEmojis;
        set
        {
            allowEmojis = value;
            if (!allowEmojis && _inputField != null)
            {
                SanitizeCurrentText();
            }
        }
    }

    private void Awake()
    {
        ResolveInputField();
        BindListeners();
    }

    private void OnEnable()
    {
        if (_inputField == null)
        {
            ResolveInputField();
        }
        BindListeners();
    }

    private void OnDisable()
    {
        UnbindListeners();
    }

    private void ResolveInputField()
    {
        if (_inputField != null) return;

        _heatInputField = GetComponent<InputFieldManager>();
        if (_heatInputField != null && _heatInputField.inputText != null)
        {
            _inputField = _heatInputField.inputText;
            return;
        }

        _inputField = GetComponent<TMP_InputField>();
        if (_inputField == null)
        {
            _inputField = GetComponentInChildren<TMP_InputField>(true);
        }
    }

    private void BindListeners()
    {
        if (_inputField == null) return;

        _inputField.onValidateInput = ValidateChar;
        _inputField.onValueChanged.RemoveListener(OnValueChanged);
        _inputField.onValueChanged.AddListener(OnValueChanged);
    }

    private void UnbindListeners()
    {
        if (_inputField == null) return;

        if (_inputField.onValidateInput == ValidateChar)
        {
            _inputField.onValidateInput = null;
        }
        _inputField.onValueChanged.RemoveListener(OnValueChanged);
    }

    private char ValidateChar(string text, int charIndex, char addedChar)
    {
        if (!allowEmojis)
        {
            // Block UTF-16 surrogates (used for modern multi-byte emojis)
            if (char.IsSurrogate(addedChar)) return '\0';

            // Block pictorial symbols / dingbats in BMP
            if (PlayerNameManager.IsEmojiCodePoint((int)addedChar)) return '\0';
        }
        return addedChar;
    }

    private void OnValueChanged(string val)
    {
        if (_isSanitizing || allowEmojis || string.IsNullOrEmpty(val)) return;

        if (PlayerNameManager.ContainsEmoji(val))
        {
            SanitizeCurrentText();
        }
    }

    /// <summary>
    /// Strips any emojis currently residing inside the input field.
    /// </summary>
    public void SanitizeCurrentText()
    {
        if (_inputField == null) return;

        _isSanitizing = true;
        try
        {
            string clean = PlayerNameManager.SanitizePlayerName(_inputField.text, allowEmojis: false);
            if (clean != _inputField.text)
            {
                _inputField.text = clean;
                _inputField.caretPosition = clean.Length;
            }
        }
        finally
        {
            _isSanitizing = false;
        }
    }
}
