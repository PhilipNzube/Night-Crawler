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
    [Tooltip("The MiniMapView component in your HUD scene (legacy).")]
    public MiniMapView miniMapView;

    [Tooltip("The MinimapManager component from AA Map and Minimap System.")]
    public AAMAP.MinimapManager aaMinimapManager;

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

        // Auto-detect aaMinimapManager in scene if not assigned
        if (aaMinimapManager == null)
        {
            aaMinimapManager = FindFirstObjectByType<AAMAP.MinimapManager>(FindObjectsInactive.Include);
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

        if (aaMinimapManager != null)
        {
            aaMinimapManager.gameObject.SetActive(visible);
            if (aaMinimapManager.minimapCamera != null)
            {
                aaMinimapManager.minimapCamera.SetActive(visible);
            }
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

            if (NetworkManager.Singleton != null && 
                NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                var playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
                if (Instance.miniMapView != null)
                {
                    Instance.miniMapView.FollowCentered(playerTransform);
                }
                if (Instance.aaMinimapManager != null)
                {
                    Instance.aaMinimapManager.SetTargetObject(playerTransform.gameObject);
                }
            }

            Debug.Log("[AdventurerMinimapSetup] Minimap inherited and unlocked from Explorer corpse!");
        }
    }

    /// <summary>
    /// Activates or deactivates minimap when the Girl possesses or releases an Explorer.
    /// </summary>
    public static void OnPossessionChanged(GameObject possessedTarget, bool isPossessing)
    {
        if (Instance == null) return;

        if (isPossessing && possessedTarget != null)
        {
            bool isAdv = Instance.CheckIfAdventurer(possessedTarget);
            if (isAdv)
            {
                Instance.SetMinimapVisibility(true);
                if (Instance.miniMapView != null)
                {
                    Instance.miniMapView.FollowCentered(possessedTarget.transform);
                }
                if (Instance.aaMinimapManager != null)
                {
                    Instance.aaMinimapManager.SetTargetObject(possessedTarget);
                }
            }
            else
            {
                Instance.SetMinimapVisibility(false);
            }
        }
        else
        {
            // Restore: Girl or non-adventurer should not see minimap
            Instance.SetMinimapVisibility(false);
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
            if (aaMinimapManager != null)
            {
                aaMinimapManager.SetTargetObject(localPlayerObj);
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
