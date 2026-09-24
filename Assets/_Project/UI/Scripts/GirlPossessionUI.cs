using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Michsky.UI.Heat;
using NightCrawler.UI;

/// <summary>
/// SOLID — SRP: Modern Heat UI Possession interface for the Vengeful Spirit (Girl).
/// Powers:
/// 1. Toggling the Possession Modal via hotkey [P] or direct script call.
/// 2. Selecting from living investigator targets via Michsky HorizontalSelector.
/// 3. Displaying current possession energy pool.
/// 4. Confirming possession on the chosen target and ejecting/closing cleanly.
/// </summary>
public class GirlPossessionUI : MonoBehaviour
{
    public static GirlPossessionUI Instance { get; private set; }

    [Header("Heat UI Modal Window")]
    [Tooltip("The ModalWindowManager for the Possession Modal.")]
    public ModalWindowManager possessionModal;

    [Header("Target Selection")]
    [Tooltip("HorizontalSelector inside PossessionModal for cycling living investigators.")]
    public HorizontalSelector playerSelector;

    [Header("Possession Time Pool")]
    [Tooltip("Text displaying current possession energy (e.g. 'Possession Energy: 300s / 300s').")]
    public TextMeshProUGUI timeBankText;

    [Header("Action Buttons")]
    [Tooltip("ButtonManager for confirming possession on selected player.")]
    public ButtonManager possessButton;
    [Tooltip("ButtonManager for closing/canceling the possession modal.")]
    public ButtonManager cancelButton;

    [Header("Hotkeys")]
    [Tooltip("Primary toggle hotkey (default [P] for Possession).")]
    public Key toggleKey = Key.P;

    private readonly List<ulong> _targetClientIds = new List<ulong>();
    private bool _isOpen = false;
    private bool _isGirl = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (possessionModal == null)
        {
            possessionModal = GetComponent<ModalWindowManager>();
        }

        // If assigned to parent container, ensure we target child Total if present
        if (timeBankText != null)
        {
            var total = timeBankText.transform.Find("Total");
            if (total != null && total.TryGetComponent<TextMeshProUGUI>(out var totalTmp))
            {
                timeBankText = totalTmp;
            }
        }

        // Clean up any default template ExitGame calls
        SanitizeModal(possessionModal);

        // Bind buttons
        if (possessButton != null)
        {
            possessButton.onClick.RemoveAllListeners();
            possessButton.onClick.AddListener(OnPossessClicked);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(CloseUI);
        }

        if (playerSelector != null)
        {
            playerSelector.useLocalization = false;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen => _isOpen;

    private void Update()
    {
        if (PauseManager.IsGamePaused)
        {
            if (_isOpen) CloseUI();
            return;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current[toggleKey].wasPressedThisFrame && IsLocalPlayerGirl())
                ToggleUI();
            else if (_isOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
                CloseUI();
        }

        if (_isOpen)
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            UpdateTimeBankDisplay();
        }
    }

    private bool IsLocalPlayerGirl()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return false;
        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null) return false;

        // If currently possessing an investigator, hotkeys should not open
        if (playerObj.TryGetComponent<GirlPossession>(out var possession) && possession.isPossessing.Value)
            return false;

        if (_isGirl) return true;

        bool result = playerObj.GetComponent<GirlStealth>() != null
            || playerObj.GetComponent<GirlMaterialController>() != null
            || playerObj.GetComponent<GirlPossession>() != null;

        if (result) _isGirl = true;
        return result;
    }

    private GirlPossession GetLocalGirlPossession()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return null;
        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        return playerObj != null ? playerObj.GetComponent<GirlPossession>() : null;
    }

    public void ToggleUI()
    {
        if (_isOpen) CloseUI();
        else OpenUI();
    }

    public void OpenUI()
    {
        _isOpen = true;
        RefreshLivingTargets();
        UpdateTimeBankDisplay();

        if (possessionModal != null)
        {
            if (!possessionModal.gameObject.activeSelf)
                possessionModal.gameObject.SetActive(true);
            possessionModal.OpenWindow();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SetPlayerLookInputs(false);
    }

    public void CloseUI()
    {
        _isOpen = false;

        // Let Michsky's Animator handle visibility — do NOT touch SetActive or canvasGroup.alpha
        if (possessionModal != null)
            possessionModal.CloseWindow();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SetPlayerLookInputs(true);
    }

    private void SetPlayerLookInputs(bool allowLookAndLock)
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var inputs = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.cursorLocked = allowLookAndLock;
                inputs.cursorInputForLook = allowLookAndLock;
            }
        }
    }

    private void RefreshLivingTargets()
    {
        _targetClientIds.Clear();
        if (playerSelector == null || NetworkManager.Singleton == null) return;

        playerSelector.items.Clear();

        ulong localId = NetworkManager.Singleton.LocalClientId;
        var girlPossession = GetLocalGirlPossession();
        bool isAlreadyPossessing = girlPossession != null && girlPossession.isPossessing.Value;

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            ulong clientId = kvp.Key;
            if (clientId == localId) continue; // Skip self

            var clientObj = kvp.Value.PlayerObject;
            if (clientObj == null) continue;

            // Skip dead or corpses
            if (clientObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0))
            {
                continue;
            }
            if (clientObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead)
            {
                continue;
            }

            string charName = null;
            if (clientObj.TryGetComponent<NetworkPlayerName>(out var netName) && 
                !string.IsNullOrEmpty(netName.playerName.Value.ToString()) && 
                !netName.playerName.Value.ToString().StartsWith("Player "))
            {
                charName = netName.playerName.Value.ToString();
            }

            if (string.IsNullOrEmpty(charName))
            {
                string registered = GirlRevealManager.GetRegisteredPlayerName(clientId);
                if (!string.IsNullOrEmpty(registered) && !registered.StartsWith("Player "))
                {
                    charName = registered;
                }
            }

            if (string.IsNullOrEmpty(charName))
            {
                string pnmName = PlayerNameManager.GetPlayerName(clientId);
                if (!string.IsNullOrEmpty(pnmName) && !pnmName.StartsWith("Player "))
                {
                    charName = pnmName;
                }
            }

            if (string.IsNullOrEmpty(charName))
            {
                charName = clientObj.name.Replace("(Clone)", "").Trim();
            }

            if (string.IsNullOrEmpty(charName))
            {
                charName = $"Investigator {clientId}";
            }

            _targetClientIds.Add(clientId);
            playerSelector.CreateNewItem(charName);
        }

        if (_targetClientIds.Count == 0)
        {
            playerSelector.CreateNewItem("No Living Targets");
            if (possessButton != null) possessButton.Interactable(false);
        }
        else
        {
            if (possessButton != null) possessButton.Interactable(!isAlreadyPossessing);
        }

        playerSelector.index = 0;
        playerSelector.UpdateUI();
    }

    private void UpdateTimeBankDisplay()
    {
        var girlPossession = GetLocalGirlPossession();
        if (girlPossession == null) return;

        float remaining = girlPossession.RemainingPool;

        if (timeBankText != null)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            timeBankText.text = $"{minutes:00}:{seconds:00}";
        }

        if (remaining <= 0f && possessButton != null)
        {
            possessButton.Interactable(false);
        }
    }

    public void OnPossessClicked()
    {
        if (_targetClientIds.Count == 0 || playerSelector == null) return;

        int selectedIdx = playerSelector.index;
        if (selectedIdx < 0 || selectedIdx >= _targetClientIds.Count) return;

        ulong targetClientId = _targetClientIds[selectedIdx];
        Debug.Log($"[GirlPossessionUI] Initiating possession on Client {targetClientId}");

        var girlPossession = GetLocalGirlPossession();
        if (girlPossession != null)
        {
            girlPossession.PossessTargetByClientId(targetClientId);
        }

        CloseUI();
    }

    public void OnReleaseClicked()
    {
        Debug.Log("[GirlPossessionUI] Releasing possession of target.");

        var girlPossession = GetLocalGirlPossession();
        if (girlPossession != null)
        {
            girlPossession.RequestRelease();
        }

        CloseUI();
    }

    /// <summary>
    /// Strips any accidental ExitGame calls copied from modal templates.
    /// </summary>
    private static void SanitizeModal(ModalWindowManager modal)
    {
        if (modal == null) return;
        modal.useLocalization = false;
        modal.titleKey = string.Empty;
        modal.descriptionKey = string.Empty;

        // Strip ExitGame component if attached
        var exitComp = modal.GetComponent("ExitGame");
        if (exitComp != null)
        {
            Destroy(exitComp);
        }
    }
}
