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
        private ulong _girlSenderClientId = 0;
        private bool _isMissionActive = false;
        private string _activeMissionTitle = string.Empty;
        private AudioSource _audioSource;

        public bool IsMissionActive => _isMissionActive;
        public bool IsLootMission => !string.IsNullOrEmpty(_activeMissionTitle) && 
            (_activeMissionTitle.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0 || 
             _activeMissionTitle.IndexOf("corpse", StringComparison.OrdinalIgnoreCase) >= 0 || 
             _activeMissionTitle.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0);
        public bool IsKillMission => !IsLootMission;

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

            // If the local player is dead, immediately hide and dismiss the pact HUD
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localPlayerObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localPlayerObj != null)
                {
                    if ((localPlayerObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) ||
                        (localPlayerObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead))
                    {
                        Hide();
                        return;
                    }
                }
            }

            _timeRemaining -= Time.deltaTime;

            if (missionTimerText != null)
            {
                int mins = Mathf.Max(0, Mathf.FloorToInt(_timeRemaining / 60f));
                int secs = Mathf.Max(0, Mathf.FloorToInt(_timeRemaining % 60f));
                missionTimerText.text = $"{mins:00}:{secs:00}";
            }

            if (_timeRemaining <= 0f)
            {
                if (!_isMirroredPossession)
                {
                    FailMission();
                }
            }
        }

        private bool _isMirroredPossession = false;
        public bool IsMirroredPossession => _isMirroredPossession;
        public float TimeRemaining => _timeRemaining;

        private PlayerPossessableNet GetLocalPossessable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localObj != null)
                {
                    return localObj.GetComponent<PlayerPossessableNet>();
                }
            }
            return null;
        }

        public void StartPossessedMirror(string title, string terms, float durationSeconds, int penaltyAmount, int rewardAmount, float timeRemaining)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            _activeMissionTitle = title;
            _totalDuration = Mathf.Max(1f, durationSeconds);
            _timeRemaining = Mathf.Max(0f, timeRemaining);
            _penaltyAmount = penaltyAmount;
            _rewardAmount = rewardAmount;
            _isMissionActive = true;
            _isMirroredPossession = true;

            if (missionTitleText != null)
            {
                missionTitleText.text = !string.IsNullOrWhiteSpace(title) ? title.ToUpper() : "DARK DEAL";
            }

            SetVisible(true);
            Debug.Log($"[ActiveDealMissionHUD] Mirrored possessed player's deal mission '{title}' ({_timeRemaining:0}s remaining).");
        }

        public void Hide()
        {
            _isMissionActive = false;
            _isMirroredPossession = false;
            SetVisible(false);
            if (DealCompletionModalUI.Instance != null) DealCompletionModalUI.Instance.Hide();
            if (DealFailureModalUI.Instance != null) DealFailureModalUI.Instance.Hide();
        }

        public void StartMission(string title, string terms, int durationSeconds, int penaltyAmount, int rewardAmount = 30, ulong girlClientId = 0)
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
            _girlSenderClientId = girlClientId;
            _isMissionActive = true;
            _isMirroredPossession = false;

            // Only show the mission of the deal at the top, no extra nonsense texts
            if (missionTitleText != null)
            {
                missionTitleText.text = !string.IsNullOrWhiteSpace(title) ? title.ToUpper() : "DARK DEAL";
            }

            // Sync active deal state onto local player's PlayerPossessableNet
            var localPlayer = GetLocalPossessable();
            if (localPlayer != null)
            {
                localPlayer.SetActiveDealServerRpc(title, terms, _totalDuration, rewardAmount, penaltyAmount);
            }

            SetVisible(true);
            Debug.Log($"[ActiveDealMissionHUD] Started deal mission '{title}' with {durationSeconds}s timer, {penaltyAmount} penalty, and {rewardAmount} reward.");
        }

        /// <summary>
        /// Called when the local player loots a corpse. Completes the Loot Body deal if active.
        /// </summary>
        public void NotifyCorpseLooted()
        {
            if (!_isMissionActive) return;
            if (IsLootMission)
            {
                Debug.Log("[ActiveDealMissionHUD] Corpse successfully looted! Completing Loot Body deal.");
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
                Debug.Log("[ActiveDealMissionHUD] Target player eliminated! Completing Kill Player deal.");
                CompleteMission();
            }
        }

        public void CompleteMission()
        {
            if (!_isMissionActive) return;
            _isMissionActive = false;

            int netGain = Mathf.Max(0, _rewardAmount - _penaltyAmount);

            // Credit the promised reward to the player's match economy
            if (MatchEconomyManager.Instance != null && NetworkManager.Singleton != null)
            {
                MatchEconomyManager.Instance.ApplyDealSuccessReward(NetworkManager.Singleton.LocalClientId, netGain);
            }

            if (missionTitleText != null)
            {
                missionTitleText.text = !string.IsNullOrWhiteSpace(_activeMissionTitle) ? _activeMissionTitle.ToUpper() : "DARK DEAL";
            }

            // Report completion outcome across the network to the Girl
            if (DealSystemNet.Instance != null)
            {
                DealSystemNet.Instance.ReportDealOutcome(_girlSenderClientId, true, _activeMissionTitle, _rewardAmount);
            }

            // Show dedicated deal completion modal
            int currentStake = MatchEconomyManager.Instance != null ? MatchEconomyManager.Instance.GetPlayerStake(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0) : _rewardAmount;
            var completionModal = DealCompletionModalUI.Instance ?? FindFirstObjectByType<DealCompletionModalUI>(FindObjectsInactive.Include);
            if (completionModal != null)
            {
                completionModal.gameObject.SetActive(true);
                completionModal.Show(_activeMissionTitle, _rewardAmount, _penaltyAmount, currentStake, "The dark deal was successfully executed. The promised bounty is yours.");
            }

            // If possessed, mirror outcome to the possessing Girl
            var localPoss = GetLocalPossessable();
            if (localPoss != null)
            {
                localPoss.RequestMirrorDealCompletionServerRpc(_activeMissionTitle, _rewardAmount, _penaltyAmount, currentStake, "The dark deal was successfully executed. The promised bounty is yours.");
                localPoss.ClearActiveDealServerRpc();
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"DEAL COMPLETED! Dark deal finished. +{netGain} credits gained.", 4f);
            }

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(HideAfterDelay(2.5f));
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

            Debug.LogWarning($"[ActiveDealMissionHUD] Deal timer expired for '{_activeMissionTitle}'! Applying {_penaltyAmount} penalty.");

            // Deduct penalty from stake
            if (MatchEconomyManager.Instance != null && NetworkManager.Singleton != null)
            {
                MatchEconomyManager.Instance.ApplyDealFailurePenalty(NetworkManager.Singleton.LocalClientId, _penaltyAmount);
            }

            if (missionTitleText != null)
            {
                missionTitleText.text = !string.IsNullOrWhiteSpace(_activeMissionTitle) ? _activeMissionTitle.ToUpper() : "DARK DEAL";
            }

            // Report failure outcome across the network to the Girl
            if (DealSystemNet.Instance != null)
            {
                DealSystemNet.Instance.ReportDealOutcome(_girlSenderClientId, false, _activeMissionTitle, _penaltyAmount);
            }

            // Show dedicated deal failure modal
            var failureModal = DealFailureModalUI.Instance ?? FindFirstObjectByType<DealFailureModalUI>(FindObjectsInactive.Include);
            if (failureModal != null)
            {
                failureModal.gameObject.SetActive(true);
                failureModal.Show(_activeMissionTitle, _penaltyAmount, "You failed to uphold the terms before the timer expired. The spirit claims its tribute from your stake.");
            }

            // If possessed, mirror failure to the possessing Girl
            var localPoss = GetLocalPossessable();
            if (localPoss != null)
            {
                localPoss.RequestMirrorDealFailureServerRpc(_activeMissionTitle, _penaltyAmount, "You failed to uphold the terms before the timer expired. The spirit claims its tribute from your stake.");
                localPoss.ClearActiveDealServerRpc();
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"DEAL FAILED! Time expired. -{_penaltyAmount} credits deducted from stake.", 4.5f);
            }

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(HideAfterDelay(2.5f));
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
