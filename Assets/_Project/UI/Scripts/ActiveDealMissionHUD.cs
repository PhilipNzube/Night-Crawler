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
        public CanvasGroup canvasGroup;
        public TextMeshProUGUI missionTitleText;
        public TextMeshProUGUI missionTimerText;

        private float _timeRemaining = 0f;
        private float _totalDuration = 0f;
        private int _penaltyAmount = 15;
        private int _rewardAmount = 30;
        private bool _isMissionActive = false;
        private string _activeMissionTitle = string.Empty;
        private AudioSource _audioSource;

        public bool IsMissionActive => _isMissionActive;
        public bool IsLootMission => !string.IsNullOrEmpty(_activeMissionTitle) && _activeMissionTitle.ToLower().Contains("loot");
        public bool IsKillMission => !string.IsNullOrEmpty(_activeMissionTitle) && _activeMissionTitle.ToLower().Contains("kill");

        private void Awake()
        {
            // If mistakenly placed on TimeBankText, remove it immediately
            if (gameObject.name == "TimeBankText")
            {
                Destroy(this);
                return;
            }

            if (Instance != null && Instance != this)
            {
                SetVisible(false);
                Destroy(this);
                return;
            }
            Instance = this;

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null && missionPanel != null && missionPanel != gameObject)
                {
                    canvasGroup = missionPanel.GetComponent<CanvasGroup>();
                }
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 0f;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
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

        public void StartMission(string title, string terms, int durationSeconds, int penaltyAmount, int rewardAmount = 30)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            _activeMissionTitle = title;
            _totalDuration = Mathf.Max(30f, durationSeconds);
            _timeRemaining = _totalDuration;
            _penaltyAmount = penaltyAmount;
            _rewardAmount = rewardAmount;
            _isMissionActive = true;

            if (missionTitleText != null)
            {
                missionTitleText.text = $"<b>PACT OBJECTIVE:</b> {title}";
            }

            SetVisible(true);
            Debug.Log($"[ActiveDealMissionHUD] Started pact mission '{title}' with {durationSeconds}s timer, {penaltyAmount} penalty, and {rewardAmount} reward.");
        }

        /// <summary>
        /// Called when the local player loots a corpse. Completes the Loot Body deal if active.
        /// </summary>
        public void NotifyCorpseLooted()
        {
            if (!_isMissionActive) return;
            if (IsLootMission)
            {
                Debug.Log("[ActiveDealMissionHUD] Corpse successfully looted! Completing Loot Body pact.");
                CompleteMission();
            }
        }

        /// <summary>
        /// Called when the local player eliminates another player. Completes the Kill Player deal if active.
        /// </summary>
        public void NotifyPlayerKilled()
        {
            if (!_isMissionActive) return;
            if (IsKillMission)
            {
                Debug.Log("[ActiveDealMissionHUD] Target player eliminated! Completing Kill Player pact.");
                CompleteMission();
            }
        }

        public void CompleteMission()
        {
            if (!_isMissionActive) return;
            _isMissionActive = false;

            // Credit the promised reward to the player's match economy
            if (MatchEconomyManager.Instance != null && NetworkManager.Singleton != null)
            {
                MatchEconomyManager.Instance.ApplyPactSuccessReward(NetworkManager.Singleton.LocalClientId, _rewardAmount);
            }

            if (missionTitleText != null)
            {
                missionTitleText.text = "<b>PACT FULFILLED!</b>";
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"PACT FULFILLED! Dark pact completed. +{_rewardAmount} credits secured.", 4f);
            }

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(HideAfterDelay(3.5f));
            }
            else
            {
                SetVisible(false);
            }
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

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(HideAfterDelay(4.5f));
            }
            else
            {
                SetVisible(false);
            }
        }

        private IEnumerator HideAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }

            // Also explicitly toggle child objects so nothing renders while hidden
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(visible);
            }

            if (missionPanel != null && missionPanel != gameObject)
            {
                missionPanel.SetActive(visible);
            }
        }
    }
}
