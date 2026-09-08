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
    public static AdventurerMinimapSetup Instance { get; private set; }

    [Header("Minimap References")]
    [Tooltip("The MiniMapView component in your HUD scene.")]
    public MiniMapView miniMapView;

    [Tooltip("The root UI GameObject of the minimap (panel/canvas).")]
    public GameObject minimapRoot;

    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // Ensure initially hidden via canvas group so GameObject stays active to run coroutines
        SetMinimapVisibility(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Sets minimap visibility using CanvasGroup and separate root object (if not this GameObject)
    /// so that this component's GameObject remains active to run coroutines.
    /// </summary>
    public void SetMinimapVisibility(bool visible)
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
        }

        if (minimapRoot != null && minimapRoot != gameObject)
        {
            minimapRoot.SetActive(visible);
        }
    }

    /// <summary>
    /// Unlocks the minimap for the local player when they loot an Explorer's corpse!
    /// </summary>
    public static void UnlockMinimapForLocalPlayer()
    {
        if (Instance != null)
        {
            Instance.SetMinimapVisibility(true);

            if (Instance.miniMapView != null && NetworkManager.Singleton != null && 
                NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                Instance.miniMapView.FollowCentered(NetworkManager.Singleton.LocalClient.PlayerObject.transform);
            }

            Debug.Log("[AdventurerMinimapSetup] Minimap inherited and unlocked from Explorer corpse!");
        }
    }

    private void Start()
    {
        // Start hidden via CanvasGroup until local player spawns and role is confirmed
        SetMinimapVisibility(false);
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
            SetMinimapVisibility(true);

            if (miniMapView != null)
            {
                miniMapView.FollowCentered(localPlayerObj.transform);
            }
        }
        else
        {
            Debug.Log("[AdventurerMinimapSetup] Local player is not Adventurer. Minimap disabled.");
            SetMinimapVisibility(false);
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
