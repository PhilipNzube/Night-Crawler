using System.Collections;
using UnityEngine;
using TMPro;

namespace Michsky.UI.Heat
{
    /// <summary>
    /// Heat UI-style animation component for rhythmic pulsing of TextMeshProUGUI elements.
    /// Works similarly to Heat UI's ImagePulse, allowing scale, alpha, and color pulsing.
    /// Can be manually attached to any text element and assigned in the Inspector.
    /// </summary>
    [AddComponentMenu("Heat UI/Animation/Text Pulse")]
    [RequireComponent(typeof(TMP_Text))]
    public class TextPulse : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The TextMeshProUGUI component to animate. Defaults to self if not assigned.")]
        public TMP_Text targetText;

        [Header("Scale Pulsing")]
        [Tooltip("Whether to pulse the transform scale.")]
        public bool pulseScale = true;
        [Range(0.5f, 2.0f)] public float minScale = 0.96f;
        [Range(0.5f, 2.0f)] public float maxScale = 1.08f;

        [Header("Alpha Pulsing")]
        [Tooltip("Whether to pulse the text alpha transparency.")]
        public bool pulseAlpha = false;
        [Range(0f, 1f)] public float minAlpha = 0.4f;
        [Range(0f, 1f)] public float maxAlpha = 1.0f;

        [Header("Animation Settings")]
        [Tooltip("Speed multiplier for the pulse cycle.")]
        [Range(0.2f, 25f)] public float pulseSpeed = 4.0f;

        [Tooltip("Custom easing curve for the pulse rhythm.")]
        public AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Playback")]
        public bool playOnEnable = true;

        private Vector3 _baseScale;
        private Color _baseColor;
        private Coroutine _pulseRoutine;
        private bool _isPlaying;

        private void Awake()
        {
            if (targetText == null)
            {
                targetText = GetComponent<TMP_Text>();
            }

            if (targetText != null)
            {
                _baseScale = targetText.transform.localScale;
                _baseColor = targetText.color;
            }
            else
            {
                _baseScale = transform.localScale;
            }
        }

        private void OnEnable()
        {
            if (playOnEnable)
            {
                StartPulse();
            }
        }

        private void OnDisable()
        {
            StopPulse();
            ResetVisuals();
        }

        public void StartPulse()
        {
            if (targetText == null) targetText = GetComponent<TMP_Text>();
            if (targetText == null) return;

            StopPulse();
            _isPlaying = true;
            _pulseRoutine = StartCoroutine(PulseLoop());
        }

        public void StopPulse()
        {
            _isPlaying = false;
            if (_pulseRoutine != null)
            {
                StopCoroutine(_pulseRoutine);
                _pulseRoutine = null;
            }
        }

        public void ResetVisuals()
        {
            if (targetText != null)
            {
                if (pulseScale) targetText.transform.localScale = _baseScale;
                if (pulseAlpha) targetText.color = _baseColor;
            }
        }

        /// <summary>
        /// Dynamically adjust the pulse speed (e.g. accelerating tension when losing a QTE).
        /// </summary>
        public void SetSpeed(float newSpeed)
        {
            pulseSpeed = Mathf.Max(0.1f, newSpeed);
        }

        /// <summary>
        /// Dynamically override the text color while maintaining pulsing alpha/scale.
        /// </summary>
        public void SetColor(Color newColor)
        {
            _baseColor = newColor;
            if (targetText != null && !pulseAlpha)
            {
                targetText.color = newColor;
            }
        }

        /// <summary>
        /// Punch scale effect on mash/hit.
        /// </summary>
        public void TriggerPunch(float punchMultiplier = 1.25f, float duration = 0.12f)
        {
            StartCoroutine(PunchRoutine(punchMultiplier, duration));
        }

        private IEnumerator PunchRoutine(float punchMultiplier, float duration)
        {
            if (targetText == null) yield break;

            Vector3 startScale = _baseScale * punchMultiplier;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float ratio = t / duration;
                targetText.transform.localScale = Vector3.Lerp(startScale, _baseScale, ratio);
                yield return null;
            }
        }

        private IEnumerator PulseLoop()
        {
            float timer = 0f;

            while (_isPlaying && targetText != null)
            {
                timer += Time.deltaTime * pulseSpeed;
                float eval = pulseCurve.Evaluate(Mathf.PingPong(timer, 1f));

                if (pulseScale)
                {
                    float currentScale = Mathf.Lerp(minScale, maxScale, eval);
                    targetText.transform.localScale = _baseScale * currentScale;
                }

                if (pulseAlpha)
                {
                    float currentAlpha = Mathf.Lerp(minAlpha, maxAlpha, eval);
                    targetText.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, currentAlpha);
                }

                yield return null;
            }
        }
    }
}
