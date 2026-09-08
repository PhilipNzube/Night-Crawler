using System.Collections;
using UnityEngine;
using Unity.Netcode;
using Arikan;

/// <summary>
/// SOLID — SRP: Configures minimap visibility exclusively for the Adventurer / Explorer role.
/// If the local player is the Adventurer, the minimap UI is enabled and bound to center on them.
/// For all other characters (Miner, Medic, Priest, Hazard Specialist, Girl), the minimap remains hidden.
/// </summary>
public class AdventurerMinimapSetup : MonoBehaviour
{
    [Header("Minimap References")]
    [Tooltip("The MiniMapView component in your HUD scene.")]
    public MiniMapView miniMapView;

    [Tooltip("The root UI GameObject of the minimap (panel/canvas).")]
    public GameObject minimapRoot;

    private void Start()
    {
        // Start hidden until local player spawns and role is confirmed
        if (minimapRoot != null) minimapRoot.SetActive(false);
        StartCoroutine(DetectRoleAndBindMinimap());
    }

    private IEnumerator DetectRoleAndBindMinimap()
    {
        // Wait until local client object is spawned
        while (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null 
               || NetworkManager.Singleton.LocalClient.PlayerObject == null)
        {
            yield return new WaitForSeconds(0.2f);
        }

        var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
        bool isAdventurer = CheckIfAdventurer(localPlayerObj);

        if (isAdventurer)
        {
            Debug.Log("[AdventurerMinimapSetup] Local player is Adventurer/Explorer. Activating Minimap!");
            if (minimapRoot != null) minimapRoot.SetActive(true);

            if (miniMapView != null)
            {
                miniMapView.FollowCentered(localPlayerObj.transform);
            }
        }
        else
        {
            Debug.Log("[AdventurerMinimapSetup] Local player is not Adventurer. Minimap disabled.");
            if (minimapRoot != null) minimapRoot.SetActive(false);
        }
    }

    private bool CheckIfAdventurer(GameObject playerObj)
    {
        string pName = playerObj.name.ToLower();
        if (pName.Contains("adventure") || pName.Contains("explorer")) return true;

        if (CharacterSelectManager.Instance != null && NetworkManager.Singleton != null)
        {
            ulong myId = NetworkManager.Singleton.LocalClientId;
            int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(myId);
            if (idx >= 0 && CharacterSelectManager.Instance.availableCharacters != null 
                && idx < CharacterSelectManager.Instance.availableCharacters.Count)
            {
                var charData = CharacterSelectManager.Instance.availableCharacters[idx];
                if (charData != null && charData.profession == InvestigatorProfession.Explorer)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
