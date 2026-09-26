using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using NightCrawler.Economy;
using Michsky.UI.Heat;

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



            if (missionTimerText != null)
            {
                int mins = Mathf.Max(0, Mathf.FloorToInt(_timeRemaining / 60f));
                int secs = Mathf.Max(0, Mathf.FloorToInt(_timeRemaining % 60f));
                missionTimerText.text = $"{mins:00}:{secs:00}";
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

            if (missionTitleText != null)
            {
                missionTitleText.text = $"<b>PACT OBJECTIVE:</b> {title}";
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
                missionTitleText.text = "<b>PACT FULFILLED!</b>";
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("PACT FULFILLED: The dark forces are pleased. Your stake is safe.", 4f);
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

            if (missionTitleText != null)
            {
                missionTitleText.text = "<b>PACT FAILED — TIME EXPIRED</b>";
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
    }
}
