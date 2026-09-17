using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using NightCrawler.Economy;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — SRP: Active Pact/Deal Mission Tracker HUD for the Investigator who accepted a deal.
    /// Displays the objective, live countdown timer, and failure penalty warning.
    /// If the timer expires, automatically applies the stake penalty through MatchEconomyManager.
    /// Includes runtime procedural UI generation fallback.
    /// </summary>
    public class ActiveDealMissionHUD : MonoBehaviour
    {
        public static ActiveDealMissionHUD Instance { get; private set; }

        [Header("UI Controls")]
        public GameObject missionPanel;
        public TextMeshProUGUI missionTitleText;
        public TextMeshProUGUI missionTimerText;
        public TextMeshProUGUI penaltyWarningText;
        public Slider timeProgressBar;

        [Header("Audio Feedback")]
        public AudioClip urgentTickSound;
        public AudioClip failureLaughSound;

        private float _timeRemaining = 0f;
        private float _totalDuration = 0f;
        private int _penaltyAmount = 15;
        private bool _isMissionActive = false;
        private string _activeMissionTitle = string.Empty;
        private AudioSource _audioSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 0f;
            }

            EnsureRuntimeUI();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!_isMissionActive || PauseManager.IsGamePaused) return;

            _timeRemaining -= Time.deltaTime;

            if (timeProgressBar != null && _totalDuration > 0f)
            {
                timeProgressBar.value = Mathf.Clamp01(_timeRemaining / _totalDuration);
            }

            if (missionTimerText != null)
            {
                int mins = Mathf.Max(0, Mathf.FloorToInt(_timeRemaining / 60f));
                int secs = Mathf.Max(0, Mathf.FloorToInt(_timeRemaining % 60f));
                string color = _timeRemaining <= 25f ? "#E74C3C" : (_timeRemaining <= 50f ? "#F39C12" : "#2ECC71");
                missionTimerText.text = $"Time Left: <color={color}><b>{mins:00}:{secs:00}</b></color>";
            }

            if (_timeRemaining <= 0f)
            {
                FailMission();
            }
        }

        public void StartMission(string title, string terms, int durationSeconds, int penaltyAmount)
        {
            _activeMissionTitle = title;
            _totalDuration = Mathf.Max(30f, durationSeconds);
            _timeRemaining = _totalDuration;
            _penaltyAmount = penaltyAmount;
            _isMissionActive = true;

            EnsureRuntimeUI();

            if (missionTitleText != null)
            {
                missionTitleText.text = $"<b>PACT OBJECTIVE:</b> {title}";
            }

            if (penaltyWarningText != null)
            {
                penaltyWarningText.text = $"<size=11><color=#E74C3C>Penalty on Expiry: -{penaltyAmount} {CurrencyConfig.CurrencySymbol} (from Stake)</color></size>";
            }

            SetVisible(true);
            Debug.Log($"[ActiveDealMissionHUD] Started pact mission '{title}' with {durationSeconds}s timer and {penaltyAmount} penalty.");
        }

        public void CompleteMission()
        {
            if (!_isMissionActive) return;
            _isMissionActive = false;

            if (missionTitleText != null)
            {
                missionTitleText.text = "<color=#2ECC71><b>PACT FULFILLED!</b></color>";
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("<color=#2ECC71>PACT FULFILLED</color>: The dark forces are pleased. Your stake is safe.", 4f);
            }

            StartCoroutine(HideAfterDelay(3.5f));
        }

        public void FailMission()
        {
            if (!_isMissionActive) return;
            _isMissionActive = false;

            Debug.LogWarning($"[ActiveDealMissionHUD] Pact timer expired for '{_activeMissionTitle}'! Applying {_penaltyAmount} penalty.");

            // Deduct penalty from stake
            if (MatchEconomyManager.Instance != null && NetworkManager.Singleton != null)
            {
                MatchEconomyManager.Instance.ApplyPactFailurePenalty(NetworkManager.Singleton.LocalClientId, _penaltyAmount);
            }

            if (failureLaughSound != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(failureLaughSound);
            }

            if (missionTitleText != null)
            {
                missionTitleText.text = "<color=#E74C3C><b>PACT FAILED — TIME EXPIRED</b></color>";
            }

            if (penaltyWarningText != null)
            {
                penaltyWarningText.text = $"<color=#E74C3C>-{_penaltyAmount} {CurrencyConfig.CurrencySymbol} deducted from your stake!</color>";
            }

            StartCoroutine(HideAfterDelay(4.5f));
        }

        private IEnumerator HideAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (missionPanel != null)
            {
                missionPanel.SetActive(visible);
            }
        }

        private void EnsureRuntimeUI()
        {
            if (missionPanel != null) return;

            // Search HUDCanvas
            Canvas canvas = null;
            var hudCanvasObj = GameObject.Find("HUDCanvas");
            if (hudCanvasObj != null) canvas = hudCanvasObj.GetComponent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();

            if (canvas == null) return;

            // Create procedural Mission HUD panel
            missionPanel = new GameObject("ActiveDealMissionPanel", typeof(RectTransform), typeof(Image));
            missionPanel.transform.SetParent(canvas.transform, false);

            var rect = missionPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.75f);
            rect.anchorMax = new Vector2(0f, 0.75f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(20f, -20f);
            rect.sizeDelta = new Vector2(280f, 85f);

            var img = missionPanel.GetComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.10f, 0.92f);

            var vGroup = missionPanel.AddComponent<VerticalLayoutGroup>();
            vGroup.padding = new RectOffset(12, 12, 8, 8);
            vGroup.spacing = 4;
            vGroup.childControlWidth = true;
            vGroup.childControlHeight = false;

            // Title
            var titleObj = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObj.transform.SetParent(missionPanel.transform, false);
            missionTitleText = titleObj.GetComponent<TextMeshProUGUI>();
            missionTitleText.fontSize = 12;
            missionTitleText.color = Color.white;
            missionTitleText.richText = true;

            // Timer
            var timerObj = new GameObject("Timer", typeof(RectTransform), typeof(TextMeshProUGUI));
            timerObj.transform.SetParent(missionPanel.transform, false);
            missionTimerText = timerObj.GetComponent<TextMeshProUGUI>();
            missionTimerText.fontSize = 13;
            missionTimerText.color = new Color(0.2f, 0.8f, 0.4f);
            missionTimerText.richText = true;

            // Penalty Text
            var penObj = new GameObject("Penalty", typeof(RectTransform), typeof(TextMeshProUGUI));
            penObj.transform.SetParent(missionPanel.transform, false);
            penaltyWarningText = penObj.GetComponent<TextMeshProUGUI>();
            penaltyWarningText.fontSize = 10;
            penaltyWarningText.color = new Color(0.9f, 0.3f, 0.3f);
            penaltyWarningText.richText = true;
        }
    }
}
