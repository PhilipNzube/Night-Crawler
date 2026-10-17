using TMPro;
using UnityEngine;
using Michsky.UI.Heat;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// Attached directly to the SelectionConfirmationModal GameObject in the scene.
    /// Encapsulates all UI elements within this modal, including slot capsules and buttons.
    /// </summary>
    public class SelectionConfirmationModal : MonoBehaviour
    {
        [Header("Heat UI Modal Window")]
        [Tooltip("The ModalWindowManager component on this GameObject.")]
        public ModalWindowManager modalWindow;
        public ModalWindowManager modalWindowManager => modalWindow;
        public bool IsOpen => modalWindow != null && modalWindow.isOn;

        [Header("Slot Capsules (Local Children)")]
        [Tooltip("UndeadSlotsLeft capsule under Content/Main Content.")]
        public GameObject undeadSlotsCapsule;
        [Tooltip("Total text inside UndeadSlotsLeft.")]
        public TextMeshProUGUI undeadSlotsText;

        [Tooltip("BerserkerSlotsLeft capsule under Content/Main Content.")]
        public GameObject berserkerSlotsCapsule;
        [Tooltip("Total text inside BerserkerSlotsLeft.")]
        public TextMeshProUGUI berserkerSlotsText;

        [Header("Modal Buttons")]
        [Tooltip("Confirm button inside Buttons/Confirm.")]
        public ButtonManager confirmButton;
        [Tooltip("Cancel button inside Buttons/Cancel.")]
        public ButtonManager cancelButton;

        private void Awake()
        {
            if (modalWindow == null)
            {
                modalWindow = GetComponent<ModalWindowManager>();
            }
            if (confirmButton != null)
            {
                confirmButton.checkForDoubleClick = false;
            }
            if (cancelButton != null)
            {
                cancelButton.checkForDoubleClick = false;
            }
        }

        private void Start()
        {
            PrimeModal();
        }

        /// <summary>
        /// Primes the modal on startup so Unity triggers Awake() and Start() during scene init,
        /// ensuring Michsky's StartBehaviour.Disable does not swallow the player's first card click.
        /// Uses the exact priming pattern from GirlDealUI.
        /// </summary>
        public void PrimeModal()
        {
            // 1. Temporarily activate GameObject so Unity executes Awake() and Start() on ModalWindowManager
            // during scene initialization on frame 0 rather than when the player taps a card for the first time!
            bool wasActive = gameObject.activeSelf;
            if (!wasActive)
            {
                gameObject.SetActive(true);
            }

            if (modalWindow == null)
            {
                modalWindow = GetComponent<ModalWindowManager>();
                if (modalWindow == null) modalWindow = GetComponentInChildren<ModalWindowManager>(true);
            }

            if (modalWindow != null)
            {
                if (!modalWindow.gameObject.activeSelf)
                {
                    modalWindow.gameObject.SetActive(true);
                }

                modalWindow.useLocalization = false;
                modalWindow.closeOnCancel = true;
                modalWindow.closeOnConfirm = true;
                modalWindow.startBehaviour = ModalWindowManager.StartBehaviour.Enable;
                modalWindow.onConfirm.RemoveAllListeners();
                modalWindow.isOn = false;

                var mc = modalWindow.GetComponent<CanvasGroup>();
                if (mc != null)
                {
                    mc.alpha = 0f;
                    mc.interactable = false;
                    mc.blocksRaycasts = false;
                }

                modalWindow.gameObject.SetActive(false);
            }

            var cg = GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// Strips invalid state, forces StartBehaviour.Enable, alpha = 0, and deactivates GameObject.
        /// </summary>
        public static void SanitizeModal(ModalWindowManager modal)
        {
            if (modal == null) return;

            modal.useLocalization = false;
            modal.closeOnCancel = true;
            modal.closeOnConfirm = true;
            modal.startBehaviour = ModalWindowManager.StartBehaviour.Enable;
            modal.isOn = false;

            var cg = modal.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }

            if (modal.gameObject.activeSelf)
            {
                modal.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Strictly closes and deactivates the modal, resetting CanvasGroup to transparent non-blocking.
        /// Follows the exact closing pattern of GirlDealUI.CloseDealModal.
        /// </summary>
        public void DeactivateModal()
        {
            if (modalWindow != null)
            {
                modalWindow.CloseWindow();

                var cg = modalWindow.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }

                modalWindow.gameObject.SetActive(false);
            }

            var rootCg = GetComponent<CanvasGroup>();
            if (rootCg != null)
            {
                rootCg.alpha = 0f;
                rootCg.interactable = false;
                rootCg.blocksRaycasts = false;
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Configures the modal to display Undead summon slots.
        /// </summary>
        public void ShowUndeadSlots(int remaining, int max, bool canSummon = true)
        {
            if (undeadSlotsCapsule != null) undeadSlotsCapsule.SetActive(true);
            if (berserkerSlotsCapsule != null) berserkerSlotsCapsule.SetActive(false);

            if (undeadSlotsText != null)
            {
                undeadSlotsText.text = $"{remaining} / {max}";
            }

            if (confirmButton != null)
            {
                confirmButton.Interactable(canSummon);
            }
        }

        /// <summary>
        /// Configures the modal to display Berserker summon slots.
        /// </summary>
        public void ShowBerserkerSlots(int remaining, int max, bool canSummon = true)
        {
            if (berserkerSlotsCapsule != null) berserkerSlotsCapsule.SetActive(true);
            if (undeadSlotsCapsule != null) undeadSlotsCapsule.SetActive(false);

            if (berserkerSlotsText != null)
            {
                berserkerSlotsText.text = $"{remaining} / {max}";
            }

            if (confirmButton != null)
            {
                confirmButton.Interactable(canSummon);
            }
        }

        public void OpenWindow()
        {
            // If the card button's persistent Inspector call fired this, route through GirlMonsterSummonHUD
            // so the correct card is selected, lock states are evaluated, and capsules are refreshed.
            if (GirlMonsterSummonHUD.Instance != null && !GirlMonsterSummonHUD.Instance.IsOpeningModal)
            {
                int detectedIndex = ResolveClickedCardIndex();
                GirlMonsterSummonHUD.Instance.OpenConfirmationModal(detectedIndex);
                return;
            }

            OpenModalDirectly();
        }

        private int ResolveClickedCardIndex()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                var sel = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                if (sel != null && GirlMonsterSummonHUD.Instance != null)
                {
                    return GirlMonsterSummonHUD.Instance.ResolveCardIndex(sel);
                }
            }
            return GirlMonsterSummonHUD.Instance != null ? GirlMonsterSummonHUD.Instance.SelectedMonsterIndex : 0;
        }

        public void OpenModalDirectly()
        {
            if (modalWindow != null)
            {
                modalWindow.startBehaviour = ModalWindowManager.StartBehaviour.Enable;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            var cg = GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            if (modalWindow != null)
            {
                if (!modalWindow.gameObject.activeSelf)
                {
                    modalWindow.gameObject.SetActive(true);
                }

                var modalCg = modalWindow.GetComponent<CanvasGroup>();
                if (modalCg != null)
                {
                    modalCg.alpha = 1f;
                    modalCg.interactable = true;
                    modalCg.blocksRaycasts = true;
                }

                // Force isOn to false before OpenWindow() so Michsky never early-exits on the first tap!
                modalWindow.isOn = false;
                modalWindow.OpenWindow();
            }
        }

        public void CloseWindow()
        {
            DeactivateModal();
        }
    }
}
