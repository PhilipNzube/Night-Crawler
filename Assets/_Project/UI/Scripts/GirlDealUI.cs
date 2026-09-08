using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Deal creation interface for the Girl player.
/// Allows selecting an active connected player, picking a premade template or typing
/// custom terms, configuring rewards (e.g. weapon grant), and sending the deal across the network.
/// </summary>
public class GirlDealUI : MonoBehaviour
{
    public static GirlDealUI Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject mainPanel;

    [Header("Player Target Selection")]
    public TMP_Dropdown playerDropdown;

    [Header("Template Selection")]
    public TMP_Dropdown templateDropdown;

    [Header("Deal Configuration Inputs")]
    public TMP_InputField titleInput;
    public TMP_InputField termsInput;
    public TMP_InputField rewardInput;
    public Toggle grantWeaponToggle;

    [Header("Buttons")]
    public Button sendDealButton;
    public Button closeButton;

    [Header("Hotkeys")]
    public Key toggleKey = Key.T;

    private readonly List<ulong> _connectedPlayerIds = new List<ulong>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (sendDealButton != null) sendDealButton.onClick.AddListener(OnSendDealClicked);
        if (closeButton != null) closeButton.onClick.AddListener(CloseUI);
        if (templateDropdown != null) templateDropdown.onValueChanged.AddListener(OnTemplateSelected);

        if (mainPanel != null) mainPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        PopulateTemplateOptions();
    }

    private void Update()
    {
        // Toggle deal menu with hotkey (only for the Girl player)
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            ToggleUI();
        }
    }

    public void ToggleUI()
    {
        if (mainPanel == null) return;

        bool active = !mainPanel.activeSelf;
        if (active)
        {
            RefreshConnectedPlayers();
            mainPanel.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            CloseUI();
        }
    }

    public void CloseUI()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void RefreshConnectedPlayers()
    {
        _connectedPlayerIds.Clear();
        if (playerDropdown == null || NetworkManager.Singleton == null) return;

        playerDropdown.ClearOptions();
        List<string> options = new List<string>();

        ulong localId = NetworkManager.Singleton.LocalClientId;
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            ulong id = kvp.Key;
            if (id == localId) continue; // Don't send deal to self

            string pName = PlayerNameManager.GetPlayerName(id);
            if (string.IsNullOrEmpty(pName)) pName = $"Investigator {id}";

            _connectedPlayerIds.Add(id);
            options.Add(pName);
        }

        if (options.Count == 0)
        {
            options.Add("No active teammates");
        }

        playerDropdown.AddOptions(options);
    }

    private void PopulateTemplateOptions()
    {
        if (templateDropdown == null) return;

        templateDropdown.ClearOptions();
        List<string> templates = new List<string>
        {
            "Choose a Premade Deal...",
            "1. Blood Pact — Eliminate an Investigator",
            "2. Grave Robber — Loot the Medic's Vials",
            "3. The Betrayer — Sabotage the Squad",
            "4. Dark Escape — Lead Them Into the Shadows",
            "5. Custom Pact"
        };
        templateDropdown.AddOptions(templates);
    }

    private void OnTemplateSelected(int index)
    {
        switch (index)
        {
            case 1:
                SetDealFields("BLOOD PACT", "Eliminate one of your fellow investigators before the mine's air runs out.", "Melee Pickaxe Weapon Granted", true);
                break;
            case 2:
                SetDealFields("GRAVE ROBBER", "Locate the Medic's body, loot all remaining healing vials, and survive.", "Pickaxe Weapon + Life Support", true);
                break;
            case 3:
                SetDealFields("THE BETRAYER", "Separate from the squad and lead them into the deep shafts.", "Melee Pickaxe Weapon Granted", true);
                break;
            case 4:
                SetDealFields("DARK ESCAPE", "Follow my whispers in the dark to find the secret way out of the mine.", "Melee Weapon + Ghost Guidance", true);
                break;
            case 5:
                SetDealFields("CUSTOM PACT", "", "Melee Pickaxe Weapon Granted", true);
                break;
        }
    }

    private void SetDealFields(string title, string terms, string reward, bool grantWeapon)
    {
        if (titleInput != null) titleInput.text = title;
        if (termsInput != null) termsInput.text = terms;
        if (rewardInput != null) rewardInput.text = reward;
        if (grantWeaponToggle != null) grantWeaponToggle.isOn = grantWeapon;
    }

    private void OnSendDealClicked()
    {
        if (_connectedPlayerIds.Count == 0)
        {
            if (NotificationManager.Instance != null)
                NotificationManager.Instance.ShowNotification("No eligible players found.", 2f);
            return;
        }

        int selectedIdx = playerDropdown != null ? playerDropdown.value : 0;
        if (selectedIdx < 0 || selectedIdx >= _connectedPlayerIds.Count) return;

        ulong targetId = _connectedPlayerIds[selectedIdx];
        string title = titleInput != null && !string.IsNullOrEmpty(titleInput.text) ? titleInput.text : "PACT WITH THE SHADOWS";
        string terms = termsInput != null ? termsInput.text : "";
        string reward = rewardInput != null ? rewardInput.text : "Melee Pickaxe Weapon";
        bool grantWeapon = grantWeaponToggle != null ? grantWeaponToggle.isOn : true;

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.SendDeal(targetId, title, terms, reward, grantWeapon);
        }

        string targetName = playerDropdown.options[selectedIdx].text;
        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"Pact sent to {targetName}!", 3f);
        }

        CloseUI();
    }
}
