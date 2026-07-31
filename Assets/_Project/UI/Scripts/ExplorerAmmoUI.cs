using UnityEngine;
using TMPro;

public class ExplorerAmmoUI : MonoBehaviour
{
    public TextMeshProUGUI ammoText;
    public TextMeshProUGUI weaponText;

    private ExplorerCombatNet _combatNet;

    void Update()
    {
        if (_combatNet == null)
        {
            FindLocalCombatNet();
            return;
        }

        UpdateUI();
    }

    private void FindLocalCombatNet()
    {
        // Find the specific Explorer belonging to the local player
        var localPlayer = Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer != null)
        {
            _combatNet = localPlayer.GetComponent<ExplorerCombatNet>();
        }
    }

    private void UpdateUI()
    {
        if (_combatNet == null || ammoText == null) return;

        bool isGun = _combatNet.currentWeaponIndex.Value == 1;
        weaponText.text = isGun ? "WEAPON: GUN" : "WEAPON: AXE";
        
        if (isGun)
        {
            ammoText.text = "AMMO: " + _combatNet.currentAmmo.Value;
            ammoText.color = _combatNet.currentAmmo.Value <= 3 ? Color.red : Color.white;
        }
        else
        {
            ammoText.text = "AMMO: --";
            ammoText.color = Color.gray;
        }
    }
}
