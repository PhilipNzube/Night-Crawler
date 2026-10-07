using System.Collections;
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
        private static DealCompletionModalUI _instance;
        public static DealCompletionModalUI Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<DealCompletionModalUI>(FindObjectsInactive.Include);
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Heat UI Modal Window")]
        [Tooltip("The ModalWindowManager component on this CompletedDealModal.")]
        public ModalWindowManager modalWindow;

        [Tooltip("Optional explicit root GameObject for the modal window. If null, uses modalWindow.gameObject or this.gameObject.")]
        public GameObject modalRootObject;

        [Tooltip("CanvasGroup controlling completion modal visibility and input interception.")]
        public CanvasGroup canvasGroup;

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
            if (_instance == this) Instance = null;
        }

        private void Update()
        {
            if (!_isOpen) return;

            // Maintain cursor free while open
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            SetPlayerLookInputs(false);

            // Allow Enter, Numpad Enter, or Escape to dismiss
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame ||
                    UnityEngine.InputSystem.Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
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

        public void Show(string dealName, int rewardCredits, int penaltyCredits = 0, int rewardLeft = 0, string description = "")
        {
            // If local player is dead, suppress showing
            if (IsLocalPlayerDead()) return;

            AutoWireReferences();
            SanitizeModal();

            string cleanName = !string.IsNullOrWhiteSpace(dealName) ? dealName.ToUpper() : "DARK DEAL";
            if (dealNameText != null)
            {
                dealNameText.text = cleanName;
                EnsureHierarchyAndContainersActive(dealNameText);
            }

            if (rewardText != null)
            {
                rewardText.text = $"{rewardCredits}";
                EnsureHierarchyAndContainersActive(rewardText);
            }

            if (penaltyText != null)
            {
                penaltyText.text = $"{penaltyCredits}";
                EnsureHierarchyAndContainersActive(penaltyText);
            }

            if (rewardLeftText != null)
            {
                int displayLeft = rewardLeft > 0 ? rewardLeft : rewardCredits;
                rewardLeftText.text = $"{displayLeft}";
                EnsureHierarchyAndContainersActive(rewardLeftText);
            }

            string cleanDesc = !string.IsNullOrWhiteSpace(description) ? description : "The terms of this dark deal have been fulfilled. The promised bounty is yours.";
            if (descriptionText != null)
            {
                descriptionText.text = cleanDesc;
                EnsureHierarchyAndContainersActive(descriptionText);
            }

            ActivateAllCustomStatContainers();

            if (modalWindow != null)
            {
                modalWindow.useCustomContent = true;
                modalWindow.useLocalization = false;
                modalWindow.titleKey = string.Empty;
                modalWindow.descriptionKey = string.Empty;
                if (modalWindow.windowTitle != null) modalWindow.windowTitle.text = "its a done deal";
                if (modalWindow.windowDescription != null)
                {
                    modalWindow.windowDescription.text = cleanDesc;
                }
                try { modalWindow.UpdateUI(); } catch { }
            }

            RebuildLayouts();
            SetVisible(true, modifyCursor: true);

            // Sync with PlayerPossessableNet for mirroring to possessing Girl
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localObj != null && localObj.TryGetComponent<PlayerPossessableNet>(out var pNet))
                {
                    int displayLeft = rewardLeft > 0 ? rewardLeft : rewardCredits;
                    pNet.SetCompletionModalStateServerRpc(true, cleanName, rewardCredits, penaltyCredits, displayLeft, cleanDesc);
                }
            }

            Debug.Log($"[DealCompletionModalUI] Displayed completion modal for '{cleanName}' (reward={rewardCredits})");
        }

        public void HideLocalOnly()
        {
            SetVisible(false, modifyCursor: true);
        }

        public void Hide()
        {
            HideLocalOnly();

            var pNet = PlayerPossessableNet.GetLocalOrPossessed();
            if (pNet != null)
            {
                pNet.DismissModalBidirectionalServerRpc(0);
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
            modalWindow.useCustomContent = true;
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

            // Disable all LocalizedObject components across children so dynamic strings are not wiped
            var locObjs = GetComponentsInChildren<LocalizedObject>(true);
            foreach (var loc in locObjs)
            {
                if (loc != null) loc.enabled = false;
            }
        }

        public void RebuildLayouts()
        {
            var rts = GetComponentsInChildren<RectTransform>(true);
            for (int i = rts.Length - 1; i >= 0; i--)
            {
                if (rts[i] != null)
                {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rts[i]);
                }
            }
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
        private void EnsureHierarchyAndContainersActive(Component target)
        {
            if (target == null) return;
            Transform cur = target.transform;
            while (cur != null && cur != transform.parent)
            {
                if (!cur.gameObject.activeSelf) cur.gameObject.SetActive(true);
                var cg = cur.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
                cur = cur.parent;
            }
        }

        private void ActivateAllCustomStatContainers()
        {
            var allTransforms = GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                if (modalWindow != null && modalWindow.cancelButton != null && t == modalWindow.cancelButton.transform)
                    continue;

                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                }
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }
        }
    }
}
