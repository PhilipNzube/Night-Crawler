using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class PossessionSystemNet : NetworkBehaviour
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;
    
    private GameObject _activeMonster;
    private bool _isPossessing = false;

    // You'll need a reference to your main Camera
    public Camera mainCamera; 

    void Update()
    {
        if (!IsOwner) return;

        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            if (!_isPossessing) TryPossess();
            else ReleasePossession();
        }
    }

    private void TryPossess()
    {
        if (stats == null) return;
        
        Ray ray = mainCamera.ScreenPointToRay(new Vector2(Screen.width / 2, Screen.height / 2));
        if (Physics.Raycast(ray, out RaycastHit hit, stats.possessionRange, stats.possessionTargetLayer))
        {
            // Make sure the monster has a "MonsterID" or similar script
            if (hit.collider.CompareTag("Monster"))
            {
                _activeMonster = hit.collider.gameObject;
                TogglePossessionServerRpc(_activeMonster.GetComponent<NetworkObject>().NetworkObjectId, true);
            }
        }
    }

    [ServerRpc]
    private void TogglePossessionServerRpc(ulong monsterId, bool state)
    {
        // Tell everyone this monster is now player-controlled
        TogglePossessionClientRpc(monsterId, state);
    }

    [ClientRpc]
    private void TogglePossessionClientRpc(ulong monsterId, bool state)
    {
        NetworkObject monsterObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[monsterId];
        
        if (state)
        {
            _isPossessing = true;
            // 1. Disable the AI so it doesn't fight you
            if(monsterObj.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent)) agent.enabled = false;

            if (IsOwner)
            {
                // 2. Snap your camera to the monster's head/eyes
                mainCamera.transform.SetParent(monsterObj.transform);
                mainCamera.transform.localPosition = new Vector3(0, 1.8f, 0); // Adjust for monster height
                mainCamera.transform.localRotation = Quaternion.identity;
                
                // 3. Enable the Monster's Attack Script
            //    if(monsterObj.TryGetComponent(out GirlCombatNet combat)) combat.enabled = true;
            }
        }
        else
        {
            _isPossessing = false;
            // Re-enable AI
            if(monsterObj.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent)) agent.enabled = true;
            
            if (IsOwner)
            {
                // Return camera to the Girl's body
                mainCamera.transform.SetParent(transform);
                mainCamera.transform.localPosition = new Vector3(0, 1.5f, 0);
            }
        }
    }

    private void ReleasePossession()
    {
        if (_activeMonster != null)
        {
            TogglePossessionServerRpc(_activeMonster.GetComponent<NetworkObject>().NetworkObjectId, false);
            _activeMonster = null;
        }
    }
}