using UnityEngine;
using TMPro;
using System.Collections;

public class MatchResultOverlay : MonoBehaviour
{
    public GameObject overlayPanel;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI subText;

    private void Awake()
    {
        // Force hide immediately on spawn/load
        if (overlayPanel != null) overlayPanel.SetActive(false);
    }

    private void Start()
    {
        // Re-lock cursor whenever a new match/scene begins
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // Setup initial UI states
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gameEnded.OnValueChanged += OnGameEnded;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gameEnded.OnValueChanged -= OnGameEnded;
        }
    }

    private void OnGameEnded(bool previousValue, bool newValue)
    {
        if (newValue) 
        {
            ShowResult("MATCH OVER"); // Local safety trigger
        }
    }

    // Simplified back to a direct call. The GameManager handles the search/retry now.
    public void ShowResultDirectly(string msg)
    {
        Debug.Log($"[UI] Setting Match Result Text: {msg}");
        ShowResult(msg);
    }

    private void ShowResult(string msg)
    {
        Debug.Log($"[UI-OVERLAY] ShowResult called with: {msg}");
        
        if (overlayPanel == null)
        {
            Debug.LogError("[UI-OVERLAY] CRITICAL ERROR: 'Overlay Panel' is not assigned in the Inspector! Please drag your UI Panel into the slot.");
            return;
        }

        if (resultText == null)
        {
            Debug.LogError("[UI-OVERLAY] CRITICAL ERROR: 'Result Text' is not assigned in the Inspector!");
            return;
        }

        overlayPanel.SetActive(true);
        Debug.Log("[UI-OVERLAY] Overlay Panel is now ACTIVE.");

        // --- NEW: UNLOCK CURSOR FOR THE BUILD ---
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Try to disable camera control if using StarterAssets
        if (Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            var inputs = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null) 
            {
                inputs.cursorLocked = false;
                inputs.cursorInputForLook = false;
            }
        }

        // 1. Set the Primary result message from the Server
        resultText.text = msg;

        // 2. Set Case-Specific subtext (VICTORY / DEFEAT)
        string outcome = "GAME OVER";
        if (Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            bool isGirl = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.name.Contains("Girl");
            
            // Check if our team won based on the message
            bool demonWon = msg.Contains("Demon Wins");
            bool explorersWon = msg.Contains("Explorers Win") || msg.Contains("Survived");

            if ((isGirl && demonWon) || (!isGirl && explorersWon)) outcome = "VICTORY";
            else if ((isGirl && explorersWon) || (!isGirl && demonWon)) outcome = "DEFEAT";
        }

        subText.text = $"{outcome} - Returning to lobby...";

        // Add some "oomph"
        StartCoroutine(ResultsPulse());
    }

    private IEnumerator ResultsPulse()
    {
        float t = 0;
        while (t < 1.0f)
        {
            t += Time.deltaTime;
            overlayPanel.transform.localScale = Vector3.one * (1.0f + Mathf.PingPong(t * 2, 0.1f));
            yield return null;
        }
    }
}
