using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Michsky.UI.Heat;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// Overhead WorldSpace Billboard Health Bar for Monsters.
    /// Supports Michsky Heat UI ProgressBar, standard Sliders, and smooth fade-in upon receiving damage.
    /// </summary>
    public class MonsterHealthBar : MonoBehaviour
    {
        [Header("UI Component Bindings")]
        [Tooltip("Heat UI ProgressBar component (optional).")]
        public ProgressBar heatProgressBar;

        [Tooltip("Standard Unity UI Slider (optional fallback).")]
        public Slider unitySlider;

        [Tooltip("Standard UI Image with Filled type (optional fallback).")]
        public Image fillImage;

        [Tooltip("CanvasGroup controlling fade-in and visibility.")]
        public CanvasGroup canvasGroup;

        [Tooltip("Optional TextMeshPro label for monster name.")]
        public TextMeshProUGUI monsterNameText;

        [Header("Display Settings")]
        [Tooltip("Hide the health bar initially until the monster takes its first hit.")]
        public bool hideUntilDamaged = true;

        [Tooltip("Duration in seconds the bar stays visible after being damaged. Set to 0 or negative to stay visible permanently once hit.")]
        public float visibleDurationAfterDamage = 5.0f;

        [Tooltip("Speed of smooth health bar lerping.")]
        public float smoothFillSpeed = 8.0f;

        [Tooltip("Height offset above root if repositioning dynamically.")]
        public float overheadOffset = 2.4f;

        private TargetHealth _targetHealth;
        private MonsterAI _monsterAI;
        private float _currentDisplayedHealth = 100f;
        private float _maxHealth = 100f;
        private float _hideTimer = 0f;
        private bool _isBarVisible = false;
        private Camera _mainCamera;

        private void Awake()
        {
            // Auto-locate parent health / monster references
            _targetHealth = GetComponentInParent<TargetHealth>();
            _monsterAI = GetComponentInParent<MonsterAI>();

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            // Auto-locate Heat UI ProgressBar if not assigned
            if (heatProgressBar == null)
            {
                heatProgressBar = GetComponentInChildren<ProgressBar>();
            }

            // Auto-locate Slider if not assigned
            if (heatProgressBar == null && unitySlider == null)
            {
                unitySlider = GetComponentInChildren<Slider>();
            }

            if (monsterNameText == null)
            {
                monsterNameText = GetComponentInChildren<TextMeshProUGUI>();
            }

            // Initial visibility state
            if (hideUntilDamaged)
            {
                SetBarAlpha(0f);
                _isBarVisible = false;
            }
            else
            {
                SetBarAlpha(1f);
                _isBarVisible = true;
            }
        }

        private void Start()
        {
            InitializeValues();
        }

        private void OnEnable()
        {
            if (_targetHealth != null)
            {
                _targetHealth.currentHealth.OnValueChanged += HandleTargetHealthChanged;
            }
        }

        private void OnDisable()
        {
            if (_targetHealth != null)
            {
                _targetHealth.currentHealth.OnValueChanged -= HandleTargetHealthChanged;
            }
        }

        private void InitializeValues()
        {
            if (_targetHealth != null)
            {
                _maxHealth = _targetHealth.MaxHealth > 0 ? _targetHealth.MaxHealth : 100f;
                _currentDisplayedHealth = _targetHealth.CurrentHealth;
            }
            else if (_monsterAI != null)
            {
                _maxHealth = _monsterAI.stats != null ? _monsterAI.stats.maxHealth : 100f;
                _currentDisplayedHealth = _maxHealth;
            }

            if (heatProgressBar != null)
            {
                heatProgressBar.minValue = 0f;
                heatProgressBar.maxValue = _maxHealth;
                heatProgressBar.SetValue(_currentDisplayedHealth);
            }

            if (unitySlider != null)
            {
                unitySlider.minValue = 0f;
                unitySlider.maxValue = _maxHealth;
                unitySlider.value = _currentDisplayedHealth;
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = Mathf.Clamp01(_currentDisplayedHealth / _maxHealth);
            }

            if (monsterNameText != null && string.IsNullOrEmpty(monsterNameText.text))
            {
                Transform root = transform.root;
                monsterNameText.text = root != null ? root.name.Replace("(Clone)", "").Trim() : "Monster";
            }
        }

        private void HandleTargetHealthChanged(float previous, float current)
        {
            if (current < previous)
            {
                // Monster took damage -> show health bar
                OnDamaged(current, _targetHealth.MaxHealth);
            }
            else
            {
                UpdateHealthDirect(current, _targetHealth.MaxHealth);
            }
        }

        /// <summary>
        /// Explicit trigger called by MonsterAI or combat systems when monster takes damage.
        /// </summary>
        public void OnDamaged(float newHealth, float maxHp)
        {
            _maxHealth = maxHp > 0 ? maxHp : _maxHealth;
            _hideTimer = visibleDurationAfterDamage;
            _isBarVisible = true;
            UpdateHealthDirect(newHealth, _maxHealth);
        }

        public void UpdateHealthDirect(float currentHp, float maxHp)
        {
            _maxHealth = maxHp > 0 ? maxHp : _maxHealth;
            _currentDisplayedHealth = currentHp;

            if (_currentDisplayedHealth <= 0f)
            {
                _isBarVisible = false;
                SetBarAlpha(0f);
            }
        }

        private void LateUpdate()
        {
            // 1. Smooth Billboarding towards camera
            if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera != null)
            {
                transform.rotation = _mainCamera.transform.rotation;
            }

            // 2. Smooth fill animation
            float targetRatio = _maxHealth > 0 ? Mathf.Clamp01(_currentDisplayedHealth / _maxHealth) : 0f;

            if (heatProgressBar != null)
            {
                if (Mathf.Abs(heatProgressBar.currentValue - _currentDisplayedHealth) > 0.1f)
                {
                    float lerpedVal = Mathf.Lerp(heatProgressBar.currentValue, _currentDisplayedHealth, Time.deltaTime * smoothFillSpeed);
                    heatProgressBar.SetValue(lerpedVal);
                }
            }

            if (unitySlider != null)
            {
                if (Mathf.Abs(unitySlider.value - _currentDisplayedHealth) > 0.1f)
                {
                    unitySlider.value = Mathf.Lerp(unitySlider.value, _currentDisplayedHealth, Time.deltaTime * smoothFillSpeed);
                }
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = Mathf.Lerp(fillImage.fillAmount, targetRatio, Time.deltaTime * smoothFillSpeed);
            }

            // 3. Fade Timer logic
            if (hideUntilDamaged && _isBarVisible && visibleDurationAfterDamage > 0f)
            {
                _hideTimer -= Time.deltaTime;
                if (_hideTimer <= 0f)
                {
                    _isBarVisible = false;
                }
            }

            // 4. Smooth CanvasGroup fade
            float targetAlpha = _isBarVisible && _currentDisplayedHealth > 0f ? 1f : 0f;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.deltaTime * 3.5f);
            }
        }

        private void SetBarAlpha(float alpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }
        }
    }
}
