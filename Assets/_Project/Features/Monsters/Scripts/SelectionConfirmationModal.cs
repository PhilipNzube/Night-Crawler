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
            if (modalWindow != null)
            {
                modalWindow.OpenWindow();
            }
            else
            {
                gameObject.SetActive(true);
            }
        }

        public void CloseWindow()
        {
            if (modalWindow != null)
            {
                modalWindow.CloseWindow();
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
