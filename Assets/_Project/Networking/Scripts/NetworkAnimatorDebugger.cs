using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

public class NetworkAnimatorDebugger : NetworkBehaviour
{
    void Awake()
    {
        Debug.Log("--- [NETWORK ANIMATOR DEBUGGER] STARTING REPORT ---");

        // 1. Check for Animator
        Animator anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();

        if (anim == null)
        {
            Debug.LogError("[DEBUGGER] FAIL: No 'Animator' component found on this object or its children!");
        }
        else
        {
            Debug.Log($"[DEBUGGER] SUCCESS: Found Animator on GameObject: {anim.gameObject.name}");
            
            // 2. Check for Controller
            if (anim.runtimeAnimatorController == null)
            {
                Debug.LogError("[DEBUGGER] FAIL: The 'Animator' component has NO Controller assigned! You must drag an Animator Controller into the 'Controller' slot.");
            }
            else
            {
                Debug.Log($"[DEBUGGER] SUCCESS: Found Animator Controller: {anim.runtimeAnimatorController.name}");
                Debug.Log($"[DEBUGGER] Layer Count: {anim.layerCount}");
            }
        }

        // 3. Check for NetworkAnimator
        NetworkAnimator netAnim = GetComponent<NetworkAnimator>();
        if (netAnim == null) netAnim = GetComponentInChildren<NetworkAnimator>();

        if (netAnim == null)
        {
            Debug.LogWarning("[DEBUGGER] INFO: No 'NetworkAnimator' found. If you aren't syncing animations, this is fine.");
        }
        else
        {
            Debug.Log("[DEBUGGER] SUCCESS: Found NetworkAnimator component.");
            
            // Check if NetworkAnimator is actually pointing to the right Animator
            // Note: In older versions of Netcode, we check the private field via reflection if needed, 
            // but usually, it's just the component slot.
        }

        Debug.Log("--- [REPORT COMPLETE] ---");
    }
}
