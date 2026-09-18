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

        public static void BindButton(Button standardBtn, ShopButtonManager heatShopBtn, UnityAction onClick)
        {
            if (standardBtn != null)
            {
                standardBtn.onClick.RemoveAllListeners();
                if (onClick != null) standardBtn.onClick.AddListener(onClick);
            }

            if (heatShopBtn != null)
            {
                heatShopBtn.onClick.RemoveAllListeners();
                if (onClick != null) heatShopBtn.onClick.AddListener(onClick);
            }
        }

        public static void BindButton(Button standardBtn, BoxButtonManager heatBoxBtn, UnityAction onClick)
        {
            if (standardBtn != null)
            {
                standardBtn.onClick.RemoveAllListeners();
                if (onClick != null) standardBtn.onClick.AddListener(onClick);
            }

            if (heatBoxBtn != null)
            {
                heatBoxBtn.onClick.RemoveAllListeners();
                if (onClick != null) heatBoxBtn.onClick.AddListener(onClick);
            }
        }

        public static void BindButton(Button standardBtn, PanelButton heatPanelBtn, UnityAction onClick)
        {
            if (standardBtn != null)
            {
                standardBtn.onClick.RemoveAllListeners();
                if (onClick != null) standardBtn.onClick.AddListener(onClick);
            }

            if (heatPanelBtn != null)
            {
                heatPanelBtn.onClick.RemoveAllListeners();
                if (onClick != null) heatPanelBtn.onClick.AddListener(onClick);
            }
        }

        public static void BindButton(Component buttonComp, UnityAction onClick)
        {
            if (buttonComp == null) return;

            if (buttonComp is Button standardBtn)
            {
                standardBtn.onClick.RemoveAllListeners();
                if (onClick != null) standardBtn.onClick.AddListener(onClick);
                return;
            }

            if (buttonComp is ButtonManager bm)
            {
                bm.onClick.RemoveAllListeners();
                if (onClick != null) bm.onClick.AddListener(onClick);
                return;
            }

            if (buttonComp is ShopButtonManager sbm)
            {
                sbm.onClick.RemoveAllListeners();
                if (onClick != null) sbm.onClick.AddListener(onClick);
                return;
            }

            if (buttonComp is BoxButtonManager bbm)
            {
                bbm.onClick.RemoveAllListeners();
                if (onClick != null) bbm.onClick.AddListener(onClick);
                return;
            }

            if (buttonComp is PanelButton pb)
            {
                pb.onClick.RemoveAllListeners();
                if (onClick != null) pb.onClick.AddListener(onClick);
                return;
            }

            BindButton(buttonComp.gameObject, onClick);
        }

        public static void BindButton(GameObject buttonObj, UnityAction onClick)
        {
            if (buttonObj == null) return;

            var bm = buttonObj.GetComponentInChildren<ButtonManager>(true);
            if (bm != null)
            {
                bm.onClick.RemoveAllListeners();
                if (onClick != null) bm.onClick.AddListener(onClick);
            }

            var sbm = buttonObj.GetComponentInChildren<ShopButtonManager>(true);
            if (sbm != null)
            {
                sbm.onClick.RemoveAllListeners();
                if (onClick != null) sbm.onClick.AddListener(onClick);
            }

            var bbm = buttonObj.GetComponentInChildren<BoxButtonManager>(true);
            if (bbm != null)
            {
                bbm.onClick.RemoveAllListeners();
                if (onClick != null) bbm.onClick.AddListener(onClick);
            }

            var pb = buttonObj.GetComponentInChildren<PanelButton>(true);
            if (pb != null)
            {
                pb.onClick.RemoveAllListeners();
                if (onClick != null) pb.onClick.AddListener(onClick);
            }

            var btn = buttonObj.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                if (onClick != null) btn.onClick.AddListener(onClick);
            }
        }

        public static void BindAnyButton(UnityAction onClick, params object[] buttonTargets)
        {
            if (buttonTargets == null) return;
            foreach (var target in buttonTargets)
            {
                if (target == null) continue;
                if (target is Component comp)
                {
                    BindButton(comp, onClick);
                }
                else if (target is GameObject go)
                {
                    BindButton(go, onClick);
                }
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

        public static void SetButtonInteractable(Button standardBtn, ShopButtonManager heatShopBtn, bool interactable)
        {
            if (standardBtn != null)
            {
                standardBtn.interactable = interactable;
            }

            if (heatShopBtn != null)
            {
                heatShopBtn.isInteractable = interactable;
                heatShopBtn.UpdateUI();
            }
        }

        public static void SetButtonInteractable(Button standardBtn, BoxButtonManager heatBoxBtn, bool interactable)
        {
            if (standardBtn != null)
            {
                standardBtn.interactable = interactable;
            }

            if (heatBoxBtn != null)
            {
                heatBoxBtn.isInteractable = interactable;
                heatBoxBtn.UpdateUI();
            }
        }

        public static void SetButtonInteractable(Component buttonComp, bool interactable)
        {
            if (buttonComp == null) return;
            if (buttonComp is Button btn) { btn.interactable = interactable; return; }
            if (buttonComp is ButtonManager bm) { bm.isInteractable = interactable; bm.UpdateUI(); return; }
            if (buttonComp is ShopButtonManager sbm) { sbm.isInteractable = interactable; sbm.UpdateUI(); return; }
            if (buttonComp is BoxButtonManager bbm) { bbm.isInteractable = interactable; bbm.UpdateUI(); return; }
            if (buttonComp is PanelButton pb) { pb.isInteractable = interactable; pb.UpdateUI(); return; }
            SetButtonInteractable(buttonComp.gameObject, interactable);
        }

        public static void SetButtonInteractable(GameObject buttonObj, bool interactable)
        {
            if (buttonObj == null) return;
            var bm = buttonObj.GetComponentInChildren<ButtonManager>(true);
            if (bm != null) { bm.isInteractable = interactable; bm.UpdateUI(); }
            var sbm = buttonObj.GetComponentInChildren<ShopButtonManager>(true);
            if (sbm != null) { sbm.isInteractable = interactable; sbm.UpdateUI(); }
            var bbm = buttonObj.GetComponentInChildren<BoxButtonManager>(true);
            if (bbm != null) { bbm.isInteractable = interactable; bbm.UpdateUI(); }
            var pb = buttonObj.GetComponentInChildren<PanelButton>(true);
            if (pb != null) { pb.isInteractable = interactable; pb.UpdateUI(); }
            var btn = buttonObj.GetComponentInChildren<Button>(true);
            if (btn != null) { btn.interactable = interactable; }
        }

        public static void SetAnyButtonInteractable(bool interactable, params object[] buttonTargets)
        {
            if (buttonTargets == null) return;
            foreach (var target in buttonTargets)
            {
                if (target == null) continue;
                if (target is Component comp)
                {
                    SetButtonInteractable(comp, interactable);
                }
                else if (target is GameObject go)
                {
                    SetButtonInteractable(go, interactable);
                }
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

        public static void SetButtonText(Button standardBtn, ShopButtonManager heatShopBtn, string text)
        {
            if (standardBtn != null)
            {
                var tmp = standardBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) tmp.text = text;
            }

            if (heatShopBtn != null)
            {
                heatShopBtn.buttonTitle = text;
                heatShopBtn.UpdateUI();
            }
        }

        public static void SetButtonText(Button standardBtn, BoxButtonManager heatBoxBtn, string text)
        {
            if (standardBtn != null)
            {
                var tmp = standardBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) tmp.text = text;
            }

            if (heatBoxBtn != null)
            {
                heatBoxBtn.buttonTitle = text;
                heatBoxBtn.UpdateUI();
            }
        }

        public static void SetButtonText(Component buttonComp, string text)
        {
            if (buttonComp == null) return;
            if (buttonComp is Button btn)
            {
                var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) tmp.text = text;
                return;
            }
            if (buttonComp is ButtonManager bm) { bm.buttonText = text; bm.UpdateUI(); return; }
            if (buttonComp is ShopButtonManager sbm) { sbm.buttonTitle = text; sbm.UpdateUI(); return; }
            if (buttonComp is BoxButtonManager bbm) { bbm.buttonTitle = text; bbm.UpdateUI(); return; }
            if (buttonComp is PanelButton pb) { pb.buttonText = text; pb.UpdateUI(); return; }
            SetButtonText(buttonComp.gameObject, text);
        }

        public static void SetButtonText(GameObject buttonObj, string text)
        {
            if (buttonObj == null) return;
            var bm = buttonObj.GetComponentInChildren<ButtonManager>(true);
            if (bm != null) { bm.buttonText = text; bm.UpdateUI(); }
            var sbm = buttonObj.GetComponentInChildren<ShopButtonManager>(true);
            if (sbm != null) { sbm.buttonTitle = text; sbm.UpdateUI(); }
            var bbm = buttonObj.GetComponentInChildren<BoxButtonManager>(true);
            if (bbm != null) { bbm.buttonTitle = text; bbm.UpdateUI(); }
            var pb = buttonObj.GetComponentInChildren<PanelButton>(true);
            if (pb != null) { pb.buttonText = text; pb.UpdateUI(); }
            var tmp = buttonObj.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) tmp.text = text;
        }

        public static void SetAnyButtonText(string text, params object[] buttonTargets)
        {
            if (buttonTargets == null) return;
            foreach (var target in buttonTargets)
            {
                if (target == null) continue;
                if (target is Component comp)
                {
                    SetButtonText(comp, text);
                }
                else if (target is GameObject go)
                {
                    SetButtonText(go, text);
                }
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

        public static void OpenModal(ModalWindowManager heatModal)
        {
            if (heatModal != null)
            {
                heatModal.OpenWindow();
            }
        }

        public static void OpenModal(ModalWindowManager heatModal, string title, string description)
        {
            if (heatModal != null)
            {
                if (!string.IsNullOrEmpty(title)) heatModal.titleText = title;
                if (!string.IsNullOrEmpty(description)) heatModal.descriptionText = description;
                heatModal.OpenWindow();
            }
        }

        public static void OpenModal(GameObject standardModal, ModalWindowManager heatModal, string title = null, string description = null)
        {
            if (heatModal != null)
            {
                if (!string.IsNullOrEmpty(title)) heatModal.titleText = title;
                if (!string.IsNullOrEmpty(description)) heatModal.descriptionText = description;
                heatModal.OpenWindow();
                return;
            }

            if (standardModal != null)
            {
                standardModal.SetActive(true);
            }
        }

        public static void CloseModal(ModalWindowManager heatModal)
        {
            if (heatModal != null)
            {
                heatModal.CloseWindow();
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

        public static void SetProgress(ProgressBar heatBar, float fraction)
        {
            if (heatBar != null)
            {
                float targetMax = heatBar.maxValue > 0f ? heatBar.maxValue : 100f;
                heatBar.currentValue = Mathf.Clamp01(fraction) * targetMax;
                heatBar.UpdateUI();
            }
        }

        public static void SetProgress(Slider standardSlider, ProgressBar heatBar, float fraction)
        {
            if (standardSlider != null)
            {
                standardSlider.value = fraction;
            }
            SetProgress(heatBar, fraction);
        }

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
            if (heatSlider != null && heatSlider.mainSlider != null)
            {
                return heatSlider.mainSlider.value;
            }
            if (standardSlider != null)
            {
                return standardSlider.value;
            }
            return 0f;
        }

        public static void SetSliderValue(SliderManager heatSlider, float value)
        {
            if (heatSlider != null && heatSlider.mainSlider != null)
            {
                heatSlider.mainSlider.value = value;
                heatSlider.UpdateUI();
            }
        }

        public static void SetSliderValue(Slider standardSlider, SliderManager heatSlider, float value)
        {
            if (standardSlider != null)
            {
                standardSlider.value = value;
            }
            SetSliderValue(heatSlider, value);
        }

        public static void SetSliderLimits(Slider standardSlider, SliderManager heatSlider, float min, float max, bool wholeNumbers = false)
        {
            if (standardSlider != null)
            {
                standardSlider.minValue = min;
                standardSlider.maxValue = max;
                standardSlider.wholeNumbers = wholeNumbers;
            }
            if (heatSlider != null && heatSlider.mainSlider != null)
            {
                heatSlider.mainSlider.minValue = min;
                heatSlider.mainSlider.maxValue = max;
                heatSlider.mainSlider.wholeNumbers = wholeNumbers;
                heatSlider.UpdateUI();
            }
        }
    }
}
