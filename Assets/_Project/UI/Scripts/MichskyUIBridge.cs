using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using Michsky.UI.Heat;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — Adapter Pattern: Unified UI Bridge supporting BOTH standard Unity UGUI / TextMeshPro
    /// and Michsky's UI frameworks (Heat - Complete Modern UI and DARK - Complete Horror UI).
    /// Allows all gameplay scripts to communicate seamlessly with whichever UI system is in use.
    /// </summary>
    public static class MichskyUIBridge
    {
        // =========================================================================
        //  BUTTONS (Button & ButtonManager / BoxButtonManager)
        // =========================================================================

        public static void BindButton(Button standardBtn, ButtonManager heatBtn, UnityAction onClick)
        {
            if (standardBtn != null)
            {
                standardBtn.onClick.RemoveAllListeners();
                if (onClick != null) standardBtn.onClick.AddListener(onClick);
            }

            if (heatBtn != null)
            {
                heatBtn.onClick.RemoveAllListeners();
                if (onClick != null) heatBtn.onClick.AddListener(onClick);
            }
        }

        public static void SetButtonInteractable(Button standardBtn, ButtonManager heatBtn, bool interactable)
        {
            if (standardBtn != null)
            {
                standardBtn.interactable = interactable;
            }

            if (heatBtn != null)
            {
                heatBtn.isInteractable = interactable;
                heatBtn.UpdateUI();
            }
        }

        public static void SetButtonText(Button standardBtn, ButtonManager heatBtn, string text)
        {
            if (standardBtn != null)
            {
                var tmp = standardBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) tmp.text = text;
            }

            if (heatBtn != null)
            {
                heatBtn.buttonText = text;
                heatBtn.UpdateUI();
            }
        }

        // =========================================================================
        //  INPUT FIELDS (TMP_InputField & InputFieldManager)
        // =========================================================================

        public static void BindInputField(TMP_InputField standardInput, InputFieldManager heatInput, UnityAction<string> onValueChanged)
        {
            if (standardInput != null)
            {
                standardInput.onValueChanged.RemoveAllListeners();
                if (onValueChanged != null) standardInput.onValueChanged.AddListener(onValueChanged);
            }

            if (heatInput != null && heatInput.inputText != null)
            {
                heatInput.inputText.onValueChanged.RemoveAllListeners();
                if (onValueChanged != null) heatInput.inputText.onValueChanged.AddListener(onValueChanged);
            }
        }

        public static string GetInputText(TMP_InputField standardInput, InputFieldManager heatInput)
        {
            if (heatInput != null && heatInput.inputText != null)
            {
                return heatInput.inputText.text;
            }
            if (standardInput != null)
            {
                return standardInput.text;
            }
            return string.Empty;
        }

        public static void SetInputText(TMP_InputField standardInput, InputFieldManager heatInput, string text)
        {
            if (standardInput != null)
            {
                standardInput.text = text;
            }

            if (heatInput != null && heatInput.inputText != null)
            {
                heatInput.inputText.text = text;
            }
        }

        // =========================================================================
        //  MODAL WINDOWS (GameObject Panel & ModalWindowManager)
        // =========================================================================

        public static void OpenModal(GameObject standardModal, ModalWindowManager heatModal, string title, string description)
        {
            if (heatModal != null)
            {
                heatModal.titleText = title;
                heatModal.descriptionText = description;
                heatModal.OpenWindow();
                return;
            }

            if (standardModal != null)
            {
                standardModal.SetActive(true);
            }
        }

        public static void CloseModal(GameObject standardModal, ModalWindowManager heatModal)
        {
            if (heatModal != null)
            {
                heatModal.CloseWindow();
                return;
            }

            if (standardModal != null)
            {
                standardModal.SetActive(false);
            }
        }

        // =========================================================================
        //  PROGRESS & SLIDERS (Slider & ProgressBar / SliderManager)
        // =========================================================================

        public static void SetProgress(Slider standardSlider, ProgressBar heatBar, float current, float max)
        {
            if (standardSlider != null)
            {
                standardSlider.maxValue = max;
                standardSlider.value = current;
            }

            if (heatBar != null)
            {
                heatBar.minValue = 0;
                heatBar.maxValue = max;
                heatBar.currentValue = current;
                heatBar.UpdateUI();
            }
        }

        public static float GetSliderValue(Slider standardSlider, SliderManager heatSlider)
        {
            if (heatSlider != null)
            {
                return heatSlider.currentValue;
            }
            if (standardSlider != null)
            {
                return standardSlider.value;
            }
            return 0f;
        }

        public static void SetSliderValue(Slider standardSlider, SliderManager heatSlider, float value)
        {
            if (standardSlider != null)
            {
                standardSlider.value = value;
            }
            if (heatSlider != null)
            {
                heatSlider.currentValue = value;
                heatSlider.UpdateUI();
            }
        }
    }
}
