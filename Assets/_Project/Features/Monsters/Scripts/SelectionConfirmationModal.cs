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
            SanitizeModal(modalWindow);
            DeactivateModal();
        }

        private void Start()
        {
            SanitizeModal(modalWindow);
            DeactivateModal();
        }

        /// <summary>
        /// Strips invalid state, forces StartBehaviour.Disable, alpha = 0, and deactivates GameObject.
        /// </summary>
        public static void SanitizeModal(ModalWindowManager modal)
        {
            if (modal == null) return;

            modal.useLocalization = false;
            modal.closeOnCancel = true;
            modal.closeOnConfirm = true;
            modal.startBehaviour = ModalWindowManager.StartBehaviour.Disable;
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
        /// Strictly deactivates the modal GameObject, resetting CanvasGroup to transparent non-blocking.
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

                if (modalWindow.gameObject.activeSelf)
                {
                    modalWindow.gameObject.SetActive(false);
                }
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

                modalWindow.OpenWindow();
            }
        }

        public void CloseWindow()
        {
            DeactivateModal();
        }
    }
}
