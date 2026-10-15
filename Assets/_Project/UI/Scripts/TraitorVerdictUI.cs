using System.Collections;
using UnityEngine;
using TMPro;
using Unity.Netcode;
using Michsky.UI.Heat;
using NightCrawler.Systems;
using NightCrawler.Economy;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — SRP: Client UI Controller for the Traitor Verdict and End-of-Match HQ Forensic Audit sequence.
    /// Attached to SharedHud GO so it remains active and functional for all operatives (including fallen investigators
    /// and the Wraith) regardless of InvestigatorsHUD state.
    /// 
    /// Controls:
    /// 1. VerdictLoadingOverlay: Fullscreen overlay with animated ellipses ("HQ is determining the verdict...").
    /// 2. VerdictModal: Displays official HQ findings (Found Guilty / Not Guilty), reward deposits,
    ///    and liar/traitor disciplinary strike penalties.
    /// </summary>
    public class TraitorVerdictUI : MonoBehaviour
    {
        public static TraitorVerdictUI Instance { get; private set; }

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

        private Coroutine _verdictCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            SanitizeModal(verdictModal);

            if (verdictLoadingOverlay != null)
            {
                verdictLoadingOverlay.SetActive(false);
            }

            // Wire Verdict Modal Close Button
            if (verdictCloseButton != null)
            {
                verdictCloseButton.onClick.RemoveAllListeners();
                verdictCloseButton.onClick.AddListener(CloseVerdictModal);
            }
        }

        private void OnEnable()
        {
            TraitorReportSystemNet.OnVerdictReceived += HandleVerdictReceived;
        }

        private void OnDisable()
        {
            TraitorReportSystemNet.OnVerdictReceived -= HandleVerdictReceived;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // Continuous spinner rotation if active
            if (verdictLoadingOverlay != null && verdictLoadingOverlay.activeSelf && loadingSpinner != null)
            {
                loadingSpinner.transform.Rotate(0f, 0f, -180f * Time.unscaledDeltaTime);
            }
        }

        // =========================================================================
        //  Verdict Presentation Sequence
        // =========================================================================

        private void HandleVerdictReceived(TraitorReportEntry entry)
        {
            if (_verdictCoroutine != null)
            {
                StopCoroutine(_verdictCoroutine);
            }
            _verdictCoroutine = StartCoroutine(VerdictSequenceRoutine(entry));
        }

        public void DisplayVerdict(TraitorReportEntry entry)
        {
            HandleVerdictReceived(entry);
        }

        private IEnumerator VerdictSequenceRoutine(TraitorReportEntry entry)
        {
            if (NetworkManager.Singleton == null) yield break;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            bool isAccuser = (localId == entry.accuserClientId);
            bool isCarrier = (localId == entry.carrierClientId);
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
                : (isCarrier
                    ? $"HQ is auditing financial records for the treason dossier against {entry.accusedName} that you extracted"
                    : (isAccused 
                        ? "HQ Internal Affairs is auditing your financial records for unauthorized deals" 
                        : $"HQ is auditing financial records regarding treason complaints against {entry.accusedName}"));

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
                    if (isAccuser || isCarrier)
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

                        if (isCarrier && !isAccuser)
                        {
                            description = $"<b>TREASON CONFIRMED (EVIDENCE EXTRACTED)!</b>\n\nYou safely retrieved and delivered the physical treason dossier against <b>{entry.accusedName}</b> to HQ!\n\nHQ audited their financial records and confirmed illicit funds from demonic deals!\n\n<color=#22FF66><b>Recovery Bounty: +{reward} Cinders</b></color> added to your account immediately!\n\n{disciplinaryStatus}";
                        }
                        else
                        {
                            description = $"<b>TREASON CONFIRMED!</b>\n\nHQ audited <b>{entry.accusedName}</b>'s financial records and confirmed illicit funds received from demonic deals!\n\n<color=#22FF66><b>Reward: +{reward} Cinders</b></color> added to your account immediately!\n\n{disciplinaryStatus}";
                        }
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
                    else if (isCarrier)
                    {
                        description = $"HQ investigated the treason dossier you delivered against <b>{entry.accusedName}</b>.\n\n<b>Verdict: NOT GUILTY.</b>\nNo illicit transactions were found. The complaint has been dismissed and the original accuser (<b>{entry.accuserName}</b>) was penalized.";
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
        //  Utilities
        // =========================================================================

        private static void SanitizeModal(ModalWindowManager modal)
        {
            if (modal == null) return;
            modal.startBehaviour = ModalWindowManager.StartBehaviour.Enable;
            if (modal.isOn) modal.CloseWindow();
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer != null && localPlayer.TryGetComponent<StarterAssets.StarterAssetsInputs>(out var inputs))
            {
                inputs.cursorLocked = false;
                inputs.cursorInputForLook = false;
            }
        }

        private void RelockCursor()
        {
            if (GameManager.Instance != null && GameManager.Instance.gameEnded.Value) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer != null && localPlayer.TryGetComponent<StarterAssets.StarterAssetsInputs>(out var inputs))
            {
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }
        }
    }
}
