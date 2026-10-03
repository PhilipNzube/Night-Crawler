using System.Collections;
using UnityEngine;
using TMPro;
using Michsky.UI.Heat;
using StarterAssets;
using Unity.Netcode;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — SRP: Displays the Deal Failure Modal ("pay up!!") to the Investigator.
    /// Shows the failed deal title and the penalty credits deducted from stake.
    /// Closes cleanly via the confirm/continue button and hotkeys.
    /// </summary>
    public class DealFailureModalUI : MonoBehaviour
    {
        private static DealFailureModalUI _instance;
        public static DealFailureModalUI Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<DealFailureModalUI>(FindObjectsInactive.Include);
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Heat UI Modal Window")]
        [Tooltip("The ModalWindowManager component on this FailedToCmpleteDealModal.")]
        public ModalWindowManager modalWindow;

        [Tooltip("Optional explicit root GameObject for the modal window. If null, uses modalWindow.gameObject or this.gameObject.")]
        public GameObject modalRootObject;

        [Tooltip("CanvasGroup controlling failure modal visibility and input interception.")]
        public CanvasGroup canvasGroup;

        [Header("UI Text Displays")]
        [Tooltip("The TextMeshProUGUI showing the deal/mission name.")]
        public TextMeshProUGUI dealNameText;

        [Tooltip("The TextMeshProUGUI showing the penalty deducted.")]
        public TextMeshProUGUI penaltyText;

        [Tooltip("Optional description text (similar to deal notification prompt).")]
        public TextMeshProUGUI descriptionText;

        [Header("Buttons")]
        public ButtonManager continueButton;

        private bool _isOpen = false;
        public bool IsOpen => _isOpen;
        private Coroutine _ensureVisibleRoutine;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

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

            // Initially hidden
            SetVisible(false, modifyCursor: false);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (!_isOpen) return;

            // Maintain cursor free while open
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            SetPlayerLookInputs(false);

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

        private void LateUpdate()
        {
            if (!_isOpen) return;

            // Guarantee root modal stays active in hierarchy while open
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            if (modalWindow != null && !modalWindow.gameObject.activeSelf)
            {
                modalWindow.gameObject.SetActive(true);
            }

            if (modalRootObject != null && !modalRootObject.activeSelf)
            {
                modalRootObject.SetActive(true);
            }

            ForceAlphaOpaque();
        }

        public void Show(string dealName, int penaltyCredits, string description = "")
        {
            // If local player is dead, suppress showing
            if (IsLocalPlayerDead()) return;

            AutoWireReferences();
            SanitizeModal();

            string cleanName = !string.IsNullOrWhiteSpace(dealName) ? dealName.ToUpper() : "DARK DEAL";
            if (dealNameText != null)
            {
                dealNameText.text = cleanName;
            }

            if (penaltyText != null)
            {
                penaltyText.text = $"{penaltyCredits}";
            }

            string cleanDesc = !string.IsNullOrWhiteSpace(description) ? description : "You failed to uphold the terms in time. The spirit claims its tribute from your stake.";
            if (descriptionText != null)
            {
                descriptionText.text = cleanDesc;
            }

            if (modalWindow != null)
            {
                modalWindow.useLocalization = false;
                modalWindow.titleKey = string.Empty;
                modalWindow.descriptionKey = string.Empty;
                if (modalWindow.windowTitle != null) modalWindow.windowTitle.text = "pay up!!";
                if (modalWindow.windowDescription != null && string.IsNullOrEmpty(modalWindow.windowDescription.text))
                {
                    modalWindow.windowDescription.text = cleanDesc;
                }
                try { modalWindow.UpdateUI(); } catch { }
            }

            SetVisible(true, modifyCursor: true);
            Debug.Log($"[DealFailureModalUI] Displayed failure modal for '{cleanName}' (penalty={penaltyCredits})");
        }

        public void Hide()
        {
            SetVisible(false, modifyCursor: true);

            // If possessed, mirror dismissal to possessing Girl
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localObj != null && localObj.TryGetComponent<PlayerPossessableNet>(out var pNet) && pNet.isPossessed.Value)
                {
                    pNet.RequestMirrorModalDismissedServerRpc(1);
                }
            }
        }

        private void SetVisible(bool visible, bool modifyCursor = true)
        {
            _isOpen = visible;

            if (_ensureVisibleRoutine != null)
            {
                StopCoroutine(_ensureVisibleRoutine);
                _ensureVisibleRoutine = null;
            }

            if (visible)
            {
                // Activate parent hierarchy up to Canvas
                Transform cur = transform.parent;
                while (cur != null)
                {
                    if (!cur.gameObject.activeSelf) cur.gameObject.SetActive(true);
                    cur = cur.parent;
                }

                if (!gameObject.activeSelf) gameObject.SetActive(true);
                if (modalWindow != null && !modalWindow.gameObject.activeSelf) modalWindow.gameObject.SetActive(true);
                if (modalRootObject != null && !modalRootObject.activeSelf) modalRootObject.SetActive(true);

                transform.SetAsLastSibling();
                transform.localScale = Vector3.one;

                ForceAlphaOpaque();

                // Ensure parent Canvas is enabled
                var parentCanvas = GetComponentInParent<Canvas>();
                if (parentCanvas != null && !parentCanvas.enabled) parentCanvas.enabled = true;

                _ensureVisibleRoutine = StartCoroutine(EnsureVisibleRoutine());

                if (modifyCursor)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    SetPlayerLookInputs(false);
                }
            }
            else
            {
                ForceAlphaHidden();

                if (modifyCursor)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    SetPlayerLookInputs(true);
                }
            }
        }

        private void ForceAlphaOpaque()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(true);
                if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            var childCanvasGroups = GetComponentsInChildren<CanvasGroup>(true);
            foreach (var cg in childCanvasGroups)
            {
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }
        }

        private void ForceAlphaHidden()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(true);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            var childCanvasGroups = GetComponentsInChildren<CanvasGroup>(true);
            foreach (var cg in childCanvasGroups)
            {
                if (cg != null)
                {
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
            }
        }

        private IEnumerator EnsureVisibleRoutine()
        {
            for (int i = 0; i < 20; i++)
            {
                if (!_isOpen) yield break;
                ForceAlphaOpaque();
                yield return null;
            }
            _ensureVisibleRoutine = null;
        }

        private void AutoWireReferences()
        {
            if (modalWindow == null) modalWindow = GetComponent<ModalWindowManager>();
            if (continueButton == null && modalWindow != null)
            {
                continueButton = modalWindow.confirmButton;
            }
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(true);
            }
        }

        private void SanitizeModal()
        {
            if (modalWindow == null) return;
            modalWindow.useLocalization = false;
            modalWindow.titleKey = string.Empty;
            modalWindow.descriptionKey = string.Empty;
            modalWindow.closeOnCancel = true;
            modalWindow.closeOnConfirm = true;
            modalWindow.showCancelButton = false;
            modalWindow.showConfirmButton = true;
            modalWindow.startBehaviour = ModalWindowManager.StartBehaviour.Enable;
            modalWindow.enabled = false;
            modalWindow.isOn = true;

            if (modalWindow.cancelButton != null) modalWindow.cancelButton.gameObject.SetActive(false);
            if (modalWindow.confirmButton != null) modalWindow.confirmButton.buttonText = "CONTINUE";

            var anim = GetComponent<Animator>();
            if (anim != null) anim.enabled = false;

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
