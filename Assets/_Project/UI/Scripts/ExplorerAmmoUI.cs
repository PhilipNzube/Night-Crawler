using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Handles only ammo/weapon UI display for the local Investigator player.
/// 
/// DIP: Caches the reference once on Start via the network layer.
/// No per-frame searching — removes the expensive FindObjectOfType pattern.
/// </summary>
public class ExplorerAmmoUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI ammoText;
    public TextMeshProUGUI weaponText;

    private ExplorerCombatNet _combatNet;
    private bool _initialized = false;

    void Start()
    {
        // Attempt immediate cache; if NetworkManager isn't ready yet, Update will retry once
        TryCacheLocalCombatNet();
    }

    void Update()
    {
        if (!_initialized)
        {
            TryCacheLocalCombatNet();
            return;
        }

        UpdateUI();
    }

    private void TryCacheLocalCombatNet()
    {
        if (NetworkManager.Singleton == null) return;

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        _combatNet  = localPlayer.GetComponent<ExplorerCombatNet>();
        _initialized = _combatNet != null;
    }

    private void UpdateUI()
    {
        if (_combatNet == null || ammoText == null) return;

        bool isGun = _combatNet.currentWeaponIndex.Value == 1;
        weaponText.text = isGun ? "WEAPON: GUN" : "WEAPON: AXE";

        if (isGun)
        {
            ammoText.text  = "AMMO: " + _combatNet.currentAmmo.Value;
            ammoText.color = _combatNet.currentAmmo.Value <= 3 ? Color.red : Color.white;
        }
        else
        {
            ammoText.text  = "AMMO: --";
            ammoText.color = Color.gray;
        }
    }
}
