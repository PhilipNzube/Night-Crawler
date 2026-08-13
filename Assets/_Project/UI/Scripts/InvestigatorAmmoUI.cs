using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Updates ammo UI display for Investigator players.
/// </summary>
public class InvestigatorAmmoUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI ammoText;
    public Image weaponIcon;
    public Sprite axeIcon;
    public Sprite gunIcon;

    private InvestigatorCombatNet _combatNet;

    private void Update()
    {
        if (_combatNet == null)
        {
            TryFindLocalPlayer();
            return;
        }

        RefreshUI();
    }

    private void TryFindLocalPlayer()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayer == null) return;

        _combatNet = localPlayer.GetComponent<InvestigatorCombatNet>();
    }

    private void RefreshUI()
    {
        if (_combatNet == null) return;

        bool isGun = _combatNet.currentWeaponIndex.Value == 1;

        if (weaponIcon != null)
            weaponIcon.sprite = isGun ? gunIcon : axeIcon;

        if (ammoText != null)
        {
            if (isGun)
                ammoText.text = $"{_combatNet.currentAmmo.Value}";
            else
                ammoText.text = "──";
        }
    }
}
