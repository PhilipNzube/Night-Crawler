using UnityEngine;
using TMPro;
using Michsky.UI.Heat;
using StarterAssets;
using Unity.Netcode;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — SRP: Displays the Deal Completion Modal ("its a done deal") to the Investigator.
    /// Shows the completed deal title and the reward credits earned.
    /// Closes cleanly via the confirm/continue button and hotkeys.
    /// </summary>
    public class DealCompletionModalUI : MonoBehaviour
    {
        public static DealCompletionModalUI Instance { get; private set; }

        [Header("Heat UI Modal Window")]
        [Tooltip("The ModalWindowManager component on this CompletedDealModal.")]
        public ModalWindowManager modalWindow;

        [Header("UI Text Displays")]
        [Tooltip("The TextMeshProUGUI showing the deal/mission name.")]
        public TextMeshProUGUI dealNameText;

        [Tooltip("The TextMeshProUGUI showing the reward earned.")]
        public TextMeshProUGUI rewardText;

        [Tooltip("The TextMeshProUGUI showing the penalty (if configured in CompletedDealModal).")]
        public TextMeshProUGUI penaltyText;

        [Tooltip("The TextMeshProUGUI showing remaining reward / stake balance.")]
        public TextMeshProUGUI rewardLeftText;

        [Tooltip("Optional description text (similar to deal notification prompt).")]
        public TextMeshProUGUI descriptionText;

        [Header("Buttons")]
        public ButtonManager continueButton;

        private bool _isOpen = false;
        public bool IsOpen => _isOpen;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            AutoWireReferences();
            SanitizeModal();

            if (continueButton != null)
            {
                continueButton.onClick.RemoveListener(Hide);
                continueButton.onClick.AddListener(Hide);
            }

            if (modalWindow != null && modalWindow.confirmButton != null)
            {
                modalWindow.confirmButton.onClick.RemoveListener(Hide);
                modalWindow.confirmButton.onClick.AddListener(Hide);
            }

            // Initially ensure hidden
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!_isOpen) return;

            // Allow Enter, Space, or Escape to dismiss
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    Hide();
                }
            }
        }

        public void Show(string dealName, int rewardCredits, int penaltyCredits = 0, int rewardLeft = 0, string description = "")
        {
            // If local player is dead, suppress showing
            if (IsLocalPlayerDead()) return;

            AutoWireReferences();

            string cleanName = !string.IsNullOrWhiteSpace(dealName) ? dealName.ToUpper() : "DARK DEAL";
            if (dealNameText != null)
            {
                dealNameText.text = cleanName;
            }

            if (rewardText != null)
            {
                rewardText.text = $"{rewardCredits}";
            }

            if (penaltyText != null)
            {
                penaltyText.text = $"{penaltyCredits}";
            }

            if (rewardLeftText != null)
            {
                // If rewardLeft is not passed (0), display the earned reward or the player's stake
                int displayLeft = rewardLeft > 0 ? rewardLeft : rewardCredits;
                rewardLeftText.text = $"{displayLeft}";
            }

            string cleanDesc = !string.IsNullOrWhiteSpace(description) ? description : "The terms of this dark deal have been fulfilled. The promised bounty is yours.";
            if (descriptionText != null)
            {
                descriptionText.text = cleanDesc;
            }

            if (modalWindow != null)
            {
                modalWindow.useLocalization = false;
                modalWindow.titleKey = string.Empty;
                modalWindow.descriptionKey = string.Empty;
                if (modalWindow.windowTitle != null) modalWindow.windowTitle.text = "its a done deal";
                if (modalWindow.windowDescription != null && string.IsNullOrEmpty(modalWindow.windowDescription.text))
                {
                    modalWindow.windowDescription.text = cleanDesc;
                }
                try { modalWindow.UpdateUI(); } catch { }
            }

            _isOpen = true;
            gameObject.SetActive(true);

            // Unlock cursor for interacting with modal
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetPlayerLookInputs(false);

            if (modalWindow != null)
            {
                try { modalWindow.OpenWindow(); } catch { }
            }
        }

        public void Hide()
        {
            _isOpen = false;

            if (modalWindow != null)
            {
                try { modalWindow.CloseWindow(); } catch { }
            }

            gameObject.SetActive(false);

            // If possessed, mirror dismissal to possessing Girl
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localObj != null && localObj.TryGetComponent<PlayerPossessableNet>(out var pNet) && pNet.isPossessed.Value)
                {
                    pNet.RequestMirrorModalDismissedServerRpc(0);
                }
            }

            // Re-lock cursor back to gameplay
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SetPlayerLookInputs(true);
        }

        private void AutoWireReferences()
        {
            if (modalWindow == null) modalWindow = GetComponent<ModalWindowManager>();
            if (continueButton == null && modalWindow != null)
            {
                continueButton = modalWindow.confirmButton;
            }
        }

        private void SanitizeModal()
        {
            if (modalWindow == null) return;
            modalWindow.useLocalization = false;
            modalWindow.closeOnCancel = true;
            modalWindow.closeOnConfirm = true;
            modalWindow.showCancelButton = false;
            modalWindow.showConfirmButton = true;
            if (modalWindow.cancelButton != null) modalWindow.cancelButton.gameObject.SetActive(false);
            if (modalWindow.confirmButton != null) modalWindow.confirmButton.buttonText = "CONTINUE";

            var locObj = modalWindow.GetComponent("LocalizedObject") as Behaviour;
            if (locObj != null) locObj.enabled = false;
        }

        private bool IsLocalPlayerDead()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localObj != null)
                {
                    if ((localObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) ||
                        (localObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void SetPlayerLookInputs(bool allowLookAndLock)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localObj != null)
                {
                    var inputs = localObj.GetComponent<StarterAssetsInputs>();
                    if (inputs != null)
                    {
                        inputs.cursorLocked = allowLookAndLock;
                        inputs.cursorInputForLook = allowLookAndLock;
                    }
                }
            }
        }
    }
}
