using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Michsky.UI.Heat;
using NightCrawler.Systems;
using NightCrawler.Economy;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — SRP: Client UI Controller for the Traitor Reporting, Evidence Destruction,
    /// and End-of-Match HQ Verdict Audit sequence.
    /// 
    /// Controls:
    /// 1. FileTraitorReportModal: Allows operatives to lodge an official complaint against suspicious players.
    /// 2. DestroyFiledReportModal: Prompts carriers to incinerate/destroy physical evidence to drop charges.
    /// 3. FiledReportCount: HUD counter displaying active dossiers carried by the local operative.
    /// 4. ErrorModal: Displays single-report limit warnings and invalid filing attempts.
    /// 5. VerdictLoadingOverlayPanel: Fullscreen overlay with animated ellipses ("HQ is determining the verdict...").
    /// 6. VerdictModal: Displays official HQ findings (Found Guilty / Not Guilty), reward deposits, and liar/traitor disciplinary strike penalties.
    /// </summary>
    public class TraitorReportUI : MonoBehaviour
    {
        public static TraitorReportUI Instance { get; private set; }

        [Header("File Report Modal")]
        [Tooltip("The ModalWindowManager component on FileTraitorReportModal.")]
        public ModalWindowManager fileReportModal;

        [Tooltip("HorizontalSelector for choosing which connected operative to accuse.")]
        public HorizontalSelector playerSelector;

        [Tooltip("Confirm / Submit button on FileTraitorReportModal.")]
        public ButtonManager fileConfirmButton;

        [Tooltip("Cancel / Close button on FileTraitorReportModal.")]
        public ButtonManager fileCancelButton;

        [Tooltip("Warning text inside FileTraitorReportModal explaining false accusation penalties.")]
        public TextMeshProUGUI fileWarningText;

        [Header("Destroy Report Modal")]
        [Tooltip("The ModalWindowManager component on DestroyFiledReportModal.")]
        public ModalWindowManager destroyReportModal;

        [Tooltip("Confirm / Incinerate button on DestroyFiledReportModal.")]
        public ButtonManager destroyConfirmButton;

        [Tooltip("Cancel / Keep button on DestroyFiledReportModal.")]
        public ButtonManager destroyCancelButton;

        [Tooltip("Description text inside DestroyFiledReportModal.")]
        public TextMeshProUGUI destroyDescriptionText;

        [Header("Verdict Loading Overlay")]
        [Tooltip("The fullscreen overlay GameObject displayed when HQ is conducting the financial audit.")]
        public GameObject verdictLoadingOverlay;

        [Tooltip("Status text displaying audit progress with animated ellipses.")]
        public TextMeshProUGUI loadingStatusText;

        [Tooltip("Optional rotating spinner GameObject inside VerdictLoadingOverlayPanel.")]
        public GameObject loadingSpinner;

        [Header("Verdict Modal")]
        [Tooltip("The ModalWindowManager component on VerdictModal.")]
        public ModalWindowManager verdictModal;

        [Tooltip("Close / Dismiss button on VerdictModal.")]
        public ButtonManager verdictCloseButton;

        [Header("Error Modal (InvestigatorsHudGO)")]
        [Tooltip("The ModalWindowManager component on ErrorModal.")]
        public ModalWindowManager errorModal;

        [Tooltip("Close button on ErrorModal.")]
        public ButtonManager errorCloseButton;

        [Header("Carried Dossier HUD Indicator")]
        [Tooltip("GameObject indicating active dossiers held by the player.")]
        public GameObject filedReportCountGO;

        [Tooltip("Text label displaying the count of held dossiers (e.g. '1 Dossier' or 'x1').")]
        public TextMeshProUGUI filedReportCountText;

        private readonly List<ulong> _targetClientIds = new List<ulong>();
        private Coroutine _verdictCoroutine;
        private bool _isFilingOpen = false;
        private bool _isDestroyingOpen = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            SanitizeModal(fileReportModal);
            SanitizeModal(destroyReportModal);
            SanitizeModal(verdictModal);
            SanitizeModal(errorModal);

            if (verdictLoadingOverlay != null)
            {
                verdictLoadingOverlay.SetActive(false);
            }

            // Wire File Modal Buttons
            if (fileConfirmButton != null)
            {
                fileConfirmButton.onClick.RemoveAllListeners();
                fileConfirmButton.onClick.AddListener(OnFileConfirmClicked);
            }
            if (fileCancelButton != null)
            {
                fileCancelButton.onClick.RemoveAllListeners();
                fileCancelButton.onClick.AddListener(CloseFileModal);
            }

            // Wire Destroy Modal Buttons
            if (destroyConfirmButton != null)
            {
                destroyConfirmButton.onClick.RemoveAllListeners();
                destroyConfirmButton.onClick.AddListener(OnDestroyConfirmClicked);
            }
            if (destroyCancelButton != null)
            {
                destroyCancelButton.onClick.RemoveAllListeners();
                destroyCancelButton.onClick.AddListener(CloseDestroyModal);
            }

            // Wire Verdict Modal Buttons
            if (verdictCloseButton != null)
            {
                verdictCloseButton.onClick.RemoveAllListeners();
                verdictCloseButton.onClick.AddListener(CloseVerdictModal);
            }

            // Wire Error Modal Buttons
            if (errorCloseButton != null)
            {
                errorCloseButton.onClick.RemoveAllListeners();
                errorCloseButton.onClick.AddListener(CloseErrorModal);
            }

            if (playerSelector != null)
            {
                playerSelector.useLocalization = false;
            }

            // Initialize warning text
            if (fileWarningText != null)
            {
                fileWarningText.text = $"<color=#FF8844><b>HQ CAUTION:</b></color> Lodging false allegations without financial evidence of illicit pacts will result in an immediate <b>{TraitorReportSystemNet.FalseAccusationFine} Cinder fine</b> and a disciplinary <b>[LIAR]</b> tag visible to all operatives!";
            }
        }

        private void Start()
        {
            UpdateDossierHUD();
        }

        private void OnEnable()
        {
            TraitorReportSystemNet.OnReportFiled += HandleReportFiled;
            TraitorReportSystemNet.OnReportDestroyed += HandleReportDestroyed;
            TraitorReportSystemNet.OnVerdictReceived += HandleVerdictReceived;
            TraitorReportSystemNet.OnReportError += ShowError;
            TraitorReportSystemNet.OnReportsStateChanged += UpdateDossierHUD;
        }

        private void OnDisable()
        {
            TraitorReportSystemNet.OnReportFiled -= HandleReportFiled;
            TraitorReportSystemNet.OnReportDestroyed -= HandleReportDestroyed;
            TraitorReportSystemNet.OnVerdictReceived -= HandleVerdictReceived;
            TraitorReportSystemNet.OnReportError -= ShowError;
            TraitorReportSystemNet.OnReportsStateChanged -= UpdateDossierHUD;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (PauseManager.IsGamePaused || SpectatorController.IsAnySpectating) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

            // Check if player is an investigator (not the Girl)
            if (IsLocalPlayerGirl()) return;

            // 1. Hotkey to File Report
            if (KeybindingManager.IsActionTriggered("FileReport") || (KeybindingManager.Instance == null && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.K].wasPressedThisFrame))
            {
                if (!_isFilingOpen && !_isDestroyingOpen)
                {
                    OpenFileModal();
                }
                else if (_isFilingOpen)
                {
                    CloseFileModal();
                }
            }

            // 2. Hotkey to Destroy Carried Report
            if (KeybindingManager.IsActionTriggered("DestroyReport") || (KeybindingManager.Instance == null && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.J].wasPressedThisFrame))
            {
                if (!_isDestroyingOpen && !_isFilingOpen)
                {
                    OpenDestroyModal();
                }
                else if (_isDestroyingOpen)
                {
                    CloseDestroyModal();
                }
            }

            // Continuous spinner rotation if active
            if (verdictLoadingOverlay != null && verdictLoadingOverlay.activeSelf && loadingSpinner != null)
            {
                loadingSpinner.transform.Rotate(0f, 0f, -180f * Time.unscaledDeltaTime);
            }
        }

        // =========================================================================
        //  Filing Reports Flow
        // =========================================================================

        public void OpenFileModal()
        {
            if (fileReportModal == null) return;

            RefreshAccusablePlayers();
            _isFilingOpen = true;

            if (!fileReportModal.gameObject.activeSelf)
                fileReportModal.gameObject.SetActive(true);

            fileReportModal.OpenWindow();
            UnlockCursor();
        }

        public void CloseFileModal()
        {
            if (fileReportModal == null) return;
            _isFilingOpen = false;
            fileReportModal.CloseWindow();
            RelockCursor();
        }

        private void RefreshAccusablePlayers()
        {
            _targetClientIds.Clear();
            if (playerSelector == null || NetworkManager.Singleton == null) return;

            playerSelector.items.Clear();
            ulong localId = NetworkManager.Singleton.LocalClientId;

            // Resolve local player's own name across all sources to guarantee they never see themselves
            string localPlayerName = CloudCharacterSaveManager.Instance != null 
                ? CloudCharacterSaveManager.Instance.GetPlayerName() 
                : (PlayerNameManager.HasSavedName() ? PlayerNameManager.GetPlayerName() : "Investigator");

            var candidateIds = new HashSet<ulong>();

            // 1. From ConnectedClientsIds (available on both Host and Client in Netcode)
            if (NetworkManager.Singleton.ConnectedClientsIds != null)
            {
                foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (id != localId) candidateIds.Add(id);
                }
            }

            // 2. From ConnectedClients (Server / Host)
            if (NetworkManager.Singleton.IsServer && NetworkManager.Singleton.ConnectedClients != null)
            {
                foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
                {
                    if (kvp.Key != localId) candidateIds.Add(kvp.Key);
                }
            }

            // 3. From SpawnManager spawned player objects
            if (NetworkManager.Singleton.SpawnManager != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects != null)
            {
                foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
                {
                    if (netObj != null && netObj.IsPlayerObject)
                    {
                        if (netObj.OwnerClientId != localId && !netObj.IsLocalPlayer && !netObj.IsOwner)
                        {
                            candidateIds.Add(netObj.OwnerClientId);
                        }
                    }
                }
            }

            // 4. From PlayerReadyTracker snapshot (if available)
            if (PlayerReadyTracker.Instance != null)
            {
                var snapshot = PlayerReadyTracker.Instance.GetLobbySnapshot();
                if (snapshot != null)
                {
                    foreach (var kvp in snapshot)
                    {
                        if (kvp.Key != localId && !kvp.Value.isGirl)
                        {
                            candidateIds.Add(kvp.Key);
                        }
                    }
                }
            }

            foreach (ulong clientId in candidateIds)
            {
                // Strict check 1: Never allow local player client ID
                if (clientId == localId) continue;

                // Resolve player object if spawned
                GameObject clientObj = null;
                if (NetworkManager.Singleton.SpawnManager != null)
                {
                    var netObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
                    if (netObj != null)
                    {
                        if (netObj.IsLocalPlayer || netObj.IsOwner) continue;
                        clientObj = netObj.gameObject;
                    }
                }

                if (clientObj == null && NetworkManager.Singleton.ConnectedClients != null &&
                    NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    if (client.PlayerObject != null)
                    {
                        if (client.PlayerObject.IsLocalPlayer || client.PlayerObject.IsOwner) continue;
                        clientObj = client.PlayerObject.gameObject;
                    }
                }

                // Skip the Girl entity
                if (clientObj != null)
                {
                    if (clientObj.GetComponent<GirlPossession>() != null ||
                        clientObj.GetComponent<GirlMovement>() != null ||
                        clientObj.GetComponent<GirlStealth>() != null)
                    {
                        continue;
                    }
                }

                // Resolve player display name
                string pName = PlayerNameManager.GetPlayerName(clientId);
                if (string.IsNullOrEmpty(pName) || pName.StartsWith("Player "))
                {
                    if (clientObj != null && clientObj.TryGetComponent<NetworkPlayerName>(out var netName) && 
                        !string.IsNullOrEmpty(netName.playerName.Value.ToString()))
                    {
                        pName = netName.playerName.Value.ToString();
                    }
                    else if (PlayerReadyTracker.Instance != null && 
                             PlayerReadyTracker.Instance.GetLobbySnapshot().TryGetValue(clientId, out var lobbyInfo) && 
                             !string.IsNullOrEmpty(lobbyInfo.playerName))
                    {
                        pName = lobbyInfo.playerName;
                    }
                    else
                    {
                        pName = $"Operative {clientId}";
                    }
                }

                // Strict check 2: Never allow any candidate whose name matches the local filer's name
                if (!string.IsNullOrEmpty(localPlayerName) && 
                    pName.Trim().Equals(localPlayerName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Filter out already reported players so players don't see duplicate targets
                if (TraitorReportSystemNet.Instance != null && TraitorReportSystemNet.Instance.HasActiveReportAgainst(clientId))
                {
                    continue;
                }

                _targetClientIds.Add(clientId);
                playerSelector.CreateNewItem(pName);
            }

            // Final sanity sweep: ensure no item in playerSelector has local player's name
            for (int i = playerSelector.items.Count - 1; i >= 0; i--)
            {
                var it = playerSelector.items[i];
                if (it != null && !string.IsNullOrEmpty(localPlayerName) && 
                    it.itemTitle.Trim().Equals(localPlayerName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    playerSelector.items.RemoveAt(i);
                    if (i < _targetClientIds.Count) _targetClientIds.RemoveAt(i);
                }
            }

            if (_targetClientIds.Count == 0)
            {
                playerSelector.CreateNewItem("No Other Operatives");
                if (fileConfirmButton != null) fileConfirmButton.Interactable(false);
            }
            else
            {
                if (fileConfirmButton != null) fileConfirmButton.Interactable(true);
            }

            playerSelector.index = 0;
            playerSelector.UpdateUI();
        }

        private void OnFileConfirmClicked()
        {
            if (_targetClientIds.Count == 0 || playerSelector == null) return;
            int selIdx = playerSelector.index;
            if (selIdx < 0 || selIdx >= _targetClientIds.Count) return;

            ulong accusedId = _targetClientIds[selIdx];
            if (TraitorReportSystemNet.Instance != null)
            {
                TraitorReportSystemNet.Instance.RequestFileReport(accusedId);
            }

            CloseFileModal();
        }

        // =========================================================================
        //  Destroying Reports Flow
        // =========================================================================

        public void OpenDestroyModal()
        {
            if (destroyReportModal == null || NetworkManager.Singleton == null) return;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            if (TraitorReportSystemNet.Instance == null || !TraitorReportSystemNet.Instance.PlayerCarriesDossier(localId))
            {
                ShowError("You are not carrying any active physical evidence dossiers to destroy.");
                return;
            }

            var reportOpt = TraitorReportSystemNet.Instance.GetFirstCarriedReport(localId);
            if (!reportOpt.HasValue) return;

            var report = reportOpt.Value;
            if (destroyDescriptionText != null)
            {
                destroyDescriptionText.text = $"You are about to incinerate the physical treason dossier against <b>{report.accusedName}</b>.\n\nAll pending charges will be dropped and the HQ investigation will be aborted.\n\nAre you sure you want to destroy this evidence?";
            }

            _isDestroyingOpen = true;
            if (!destroyReportModal.gameObject.activeSelf)
                destroyReportModal.gameObject.SetActive(true);

            destroyReportModal.OpenWindow();
            UnlockCursor();
        }

        public void CloseDestroyModal()
        {
            if (destroyReportModal == null) return;
            _isDestroyingOpen = false;
            destroyReportModal.CloseWindow();
            RelockCursor();
        }

        private void OnDestroyConfirmClicked()
        {
            if (NetworkManager.Singleton == null) return;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            if (TraitorReportSystemNet.Instance != null)
            {
                var reportOpt = TraitorReportSystemNet.Instance.GetFirstCarriedReport(localId);
                if (reportOpt.HasValue)
                {
                    TraitorReportSystemNet.Instance.RequestDestroyReport(reportOpt.Value.accusedClientId);
                }
            }

            CloseDestroyModal();
        }

        // =========================================================================
        //  End-of-Match Verdict Sequence (Loading Overlay -> Verdict Modal)
        // =========================================================================

        private void HandleVerdictReceived(TraitorReportEntry entry)
        {
            if (_verdictCoroutine != null)
            {
                StopCoroutine(_verdictCoroutine);
            }
            _verdictCoroutine = StartCoroutine(VerdictSequenceRoutine(entry));
        }

        private IEnumerator VerdictSequenceRoutine(TraitorReportEntry entry)
        {
            if (NetworkManager.Singleton == null) yield break;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            bool isAccuser = (localId == entry.accuserClientId);
            bool isAccused = (localId == entry.accusedClientId);

            // Check disciplinary record before audit rewards/penalties take effect
            int initialLiarStrikes = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.LiarStrikes : 0;
            int initialTraitorStrikes = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.TraitorStrikes : 0;
            bool hadStrikesBeforeAudit = (initialLiarStrikes > 0 || initialTraitorStrikes > 0);

            // 1. Show Animated Loader Panel
            if (verdictLoadingOverlay != null)
            {
                verdictLoadingOverlay.SetActive(true);
            }

            string baseMessage = isAccuser 
                ? $"HQ is auditing financial activities regarding your treason report against {entry.accusedName}"
                : (isAccused 
                    ? "HQ Internal Affairs is auditing your financial records for unauthorized deals" 
                    : $"HQ is auditing financial records regarding treason complaints against {entry.accusedName}");

            // Animate ellipses for 3.5 seconds
            float duration = 3.5f;
            float elapsed = 0f;
            int dotCount = 1;

            while (elapsed < duration)
            {
                if (loadingStatusText != null)
                {
                    string dots = new string('.', dotCount);
                    loadingStatusText.text = $"{baseMessage}{dots}";
                }
                dotCount = (dotCount % 3) + 1;
                elapsed += 0.4f;
                yield return new WaitForSecondsRealtime(0.4f);
            }

            // 2. Hide Loader Panel
            if (verdictLoadingOverlay != null)
            {
                verdictLoadingOverlay.SetActive(false);
            }

            // 3. Populate and Open Verdict Modal
            if (verdictModal != null)
            {
                string title = entry.isGuilty ? "HQ VERDICT: FOUND GUILTY" : "HQ VERDICT: NOT GUILTY";
                string description;

                if (entry.isGuilty)
                {
                    int reward = Mathf.Max(TraitorReportSystemNet.MinimumTraitorBounty, entry.seizedDealMoney);
                    if (isAccuser)
                    {
                        int currentLiar = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.LiarStrikes : 0;
                        int currentTraitor = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.TraitorStrikes : 0;

                        string disciplinaryStatus;
                        if (hadStrikesBeforeAudit)
                        {
                            if (currentLiar > 0)
                            {
                                disciplinaryStatus = $"<i>Disciplinary Record: 1 false accusation strike redeemed! <b>{currentLiar}</b> more truthful report(s) needed to clear your [LIAR] tag.</i>";
                            }
                            else if (currentTraitor > 0)
                            {
                                disciplinaryStatus = $"<i>Disciplinary Record: 1 treason strike redeemed! <b>{currentTraitor}</b> more valid report(s) needed to clear your [TRAITOR] tag.</i>";
                            }
                            else
                            {
                                disciplinaryStatus = "<i>Disciplinary Record: <b>PARDON GRANTED!</b> All previous strikes have been fully expunged by HQ!</i>";
                            }
                        }
                        else
                        {
                            disciplinaryStatus = "<i>Disciplinary Record: <b>Clean Standing</b> (No disciplinary strikes on file. Commended by HQ for field vigilance).</i>";
                        }

                        description = $"<b>TREASON CONFIRMED!</b>\n\nHQ audited <b>{entry.accusedName}</b>'s financial records and confirmed illicit funds received from demonic deals!\n\n<color=#22FF66><b>Reward: +{reward} Cinders</b></color> added to your account immediately!\n\n{disciplinaryStatus}";
                    }
                    else if (isAccused)
                    {
                        int totalTraitor = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.TraitorStrikes : 1;
                        description = $"<b>CONVICTED OF TREASON!</b>\n\nHQ Internal Affairs discovered unauthorized pact money on your account!\n\n<color=#FF4444>All deal earnings seized.</color>\n\n<i>Disciplinary Record: Branded with the <b>[TRAITOR (x{totalTraitor})]</b> tag on your profile and lobby lineup!</i>";
                    }
                    else
                    {
                        description = $"HQ Internal Affairs concluded the audit on <b>{entry.accusedName}</b>.\n\n<b>Verdict: GUILTY OF TREASON.</b>\nAll illicit assets were confiscated and bounties distributed to the reporting operative.";
                    }
                }
                else
                {
                    if (isAccuser)
                    {
                        int totalLiar = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.LiarStrikes : 1;
                        description = $"<b>FALSE ACCUSATION!</b>\n\nHQ audited <b>{entry.accusedName}</b>'s bank records and found ZERO unauthorized funds or completed deals.\n\n<color=#FF4444><b>Penalty: -{TraitorReportSystemNet.FalseAccusationFine} Cinders fine</b></color> deducted from your balance.\n\n<i>Disciplinary Record: Branded with the <b>[LIAR (x{totalLiar})]</b> tag on your lobby profile and lineup! You must file {totalLiar} truthful report{(totalLiar > 1 ? "s" : "")} to clear your record.</i>";
                    }
                    else if (isAccused)
                    {
                        description = $"<b>EXONERATED OF ALL CHARGES!</b>\n\nHQ completed the forensic audit and verified no unauthorized funds. The complaint has been dismissed.\n\nThe false accuser (<b>{entry.accuserName}</b>) was fined and branded as a Liar.";
                    }
                    else
                    {
                        description = $"HQ investigated the treason dossier against <b>{entry.accusedName}</b>.\n\n<b>Verdict: NOT GUILTY.</b>\nNo illicit transactions were found. The false accuser has been penalized.";
                    }
                }

                MichskyUIBridge.OpenModal(verdictModal, title, description);
                UnlockCursor();
            }
        }

        public void CloseVerdictModal()
        {
            if (verdictModal == null) return;
            verdictModal.CloseWindow();
            RelockCursor();
        }

        // =========================================================================
        //  Error Modal & Feedback
        // =========================================================================

        public void ShowError(string message)
        {
            if (errorModal != null)
            {
                MichskyUIBridge.OpenModal(errorModal, "HQ INVESTIGATION NOTICE", message);
                UnlockCursor();
            }
            else if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowStyledWarning("HQ REPORT ERROR", message, Color.red, 5.0f);
            }
        }

        public void CloseErrorModal()
        {
            if (errorModal == null) return;
            errorModal.CloseWindow();
            RelockCursor();
        }

        // =========================================================================
        //  HUD Dossier Counter
        // =========================================================================

        private void UpdateDossierHUD()
        {
            if (filedReportCountGO == null || NetworkManager.Singleton == null) return;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            int carriedCount = TraitorReportSystemNet.Instance != null 
                ? TraitorReportSystemNet.Instance.GetCarriedDossiersCount(localId) 
                : 0;

            if (carriedCount > 0)
            {
                filedReportCountGO.SetActive(true);
                if (filedReportCountText != null)
                {
                    string destroyKey = KeybindingManager.GetBoundKeyString("DestroyReport", "J");
                    filedReportCountText.text = carriedCount > 1 
                        ? $"Dossiers: {carriedCount} [{destroyKey}]" 
                        : $"Dossier: 1 [{destroyKey}]";
                }
            }
            else
            {
                filedReportCountGO.SetActive(false);
            }
        }

        private void HandleReportFiled(TraitorReportEntry entry) => UpdateDossierHUD();
        private void HandleReportDestroyed(TraitorReportEntry entry) => UpdateDossierHUD();

        // =========================================================================
        //  Utilities
        // =========================================================================

        private static void SanitizeModal(ModalWindowManager modal)
        {
            if (modal == null) return;
            modal.startBehaviour = ModalWindowManager.StartBehaviour.Enable;
            if (modal.isOn) modal.CloseWindow();
        }

        private bool IsLocalPlayerGirl()
        {
            if (NetworkManager.Singleton == null) return false;
            var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (localPlayer == null) return false;

            return localPlayer.GetComponent<GirlPossession>() != null ||
                   localPlayer.GetComponent<GirlMovement>() != null ||
                   localPlayer.GetComponent<GirlStealth>() != null;
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RelockCursor()
        {
            if (GameManager.Instance != null && GameManager.Instance.gameEnded.Value) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
