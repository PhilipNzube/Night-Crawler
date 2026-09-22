using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Manages the death overlay screen when a player dies.
/// Displays a cinematic "YOU DIED" screen for the local victim, and provides
/// ally death notification support.
/// Includes dynamic runtime UI generation so it works out-of-the-box even before
/// the designer manually wires elements in the Inspector.
public class DeathUI : MonoBehaviour
{
    private static DeathUI _instance;
    public static DeathUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<DeathUI>(FindObjectsInactive.Include);
                if (_instance != null && !_instance.gameObject.activeInHierarchy)
                {
                    _instance.gameObject.SetActive(true);
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Death Screen UI (Local Victim)")]
    [Tooltip("Root GameObject or panel for the local death screen.")]
    public GameObject deathPanel;

    [Tooltip("CanvasGroup controlling death screen alpha fade.")]
    public CanvasGroup deathCanvasGroup;

    [Tooltip("Title text element (e.g. 'YOU DIED').")]
    public TMP_Text titleText;

    [Tooltip("Subtitle text element explaining corpse lootability / match status.")]
    public TMP_Text subtitleText;

    [Tooltip("Optional prompt element on death screen showing '[SPACE / CLICK] Spectate Survivors'.")]
    public TMP_Text spectatePromptText;

    [Tooltip("Optional Heat UI Hotkey Indicator or GameObject for the spectate prompt.")]
    public GameObject spectatePromptObject;

    [Tooltip("Optional Michsky Heat HotkeyEvent component for the spectate prompt.")]
    public Michsky.UI.Heat.HotkeyEvent heatSpectateHotkey;

    [Header("Michsky Heat / Dark UI")]
    [Tooltip("Optional: ModalWindowManager to display death screen dialog.")]
    public Michsky.UI.Heat.ModalWindowManager heatModalWindow;

    [Header("Optional Ally Death Banner (Other Players)")]
    [Tooltip("Banner displayed when a teammate dies.")]
    public GameObject allyDeathBanner;
    public TMP_Text allyDeathText;

    [Header("Ally Alert Feed (Bottom-Right Scrollable)")]
    [Tooltip("ScrollRect container for the bottom-right ally alert feed.")]
    public ScrollRect allyAlertScrollRect;

    [Tooltip("Content transform inside the ScrollRect where alert cards are appended.")]
    public Transform allyAlertContent;

    [Tooltip("Optional custom prefab for alert cards. If null, a sleek card is generated procedurally.")]
    public GameObject alertItemPrefab;

    [Tooltip("Maximum number of alerts kept in history before oldest is culled.")]
    public int maxAlerts = 8;

    [Header("Animation Settings")]
    public float fadeInDuration = 1.2f;

    [Tooltip("How long each alert message stays visible in the feed before animating out (seconds).")]
    public float alertLifetime = 6.0f;

    private Coroutine _fadeCoroutine;
    private Coroutine _allyBannerCoroutine;
    private string _lastAlertMessage = "";
    private float _lastAlertTime = -999f;
    private bool _skipRequested = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (heatSpectateHotkey != null)
        {
            heatSpectateHotkey.onHotkeyPress.AddListener(RequestSkipToSpectator);
        }

        // Hide death screen initially
        if (deathCanvasGroup != null)
        {
            deathCanvasGroup.alpha = 0f;
            deathCanvasGroup.interactable = false;
            deathCanvasGroup.blocksRaycasts = false;
        }

        if (deathPanel != null)
        {
            deathPanel.SetActive(false);
        }

        // Ensure AllyBanner / ScrollRect is ACTIVE so match notifications are seen
        if (allyAlertScrollRect != null)
        {
            allyAlertScrollRect.gameObject.SetActive(true);
            if (allyAlertContent == null && allyAlertScrollRect.content != null)
            {
                allyAlertContent = allyAlertScrollRect.content;
            }
        }
        else if (allyDeathBanner != null)
        {
            allyDeathBanner.SetActive(true);
            var sr = allyDeathBanner.GetComponentInChildren<ScrollRect>(true);
            if (sr != null)
            {
                allyAlertScrollRect = sr;
                if (sr.content != null) allyAlertContent = sr.content;
            }
        }

        // Hide the template text if assigned so only dynamic entries appear
        if (allyDeathText != null)
        {
            allyDeathText.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Displays the death overlay for the local player who just died.
    /// </summary>
    public void ShowDeathScreen(string title = "YOU DIED", string subtitle = "Your soul has fallen. Allies can still loot your body.")
    {
        // Safety guard: ANY living player (Investigator or Girl) must NEVER see 'YOU DIED' or trigger spectator mode!
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayerObj = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObj != null)
            {
                bool hasTh = localPlayerObj.TryGetComponent<TargetHealth>(out var th);
                bool hasHs = localPlayerObj.TryGetComponent<HealthSystem>(out var hs);

                bool isThAlive = hasTh && !th.isCorpse.Value && th.CurrentHealth > 0f;
                bool isHsAlive = hasHs && !hs.IsDead && hs.CurrentHealth > 0f;

                if (isThAlive || isHsAlive)
                {
                    Debug.Log("[DeathUI] Suppressed ShowDeathScreen — Local player is still ALIVE!");
                    HideDeathScreen();
                    return;
                }
            }
        }

        if (deathPanel != null) deathPanel.SetActive(true);
        if (titleText != null) titleText.text = title;

        NightCrawler.UI.MichskyUIBridge.OpenModal(heatModalWindow);

        _skipRequested = false;
        if (spectatePromptObject != null)
        {
            spectatePromptObject.SetActive(true);
            if (subtitleText != null) subtitleText.text = subtitle;
        }
        else if (spectatePromptText != null)
        {
            if (subtitleText != null) subtitleText.text = subtitle;
            spectatePromptText.gameObject.SetActive(true);
            spectatePromptText.text = "<b>[SPACE / CLICK]</b> TO SPECTATE";
        }
        else if (subtitleText != null)
        {
            subtitleText.text = $"{subtitle}\n\n<size=85%><b>Press [SPACE] or [CLICK] to Spectate</b></size>";
        }

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeInDeathScreenRoutine());
    }

    /// <summary>
    /// Immediately dismisses and resets the death screen overlay.
    /// </summary>
    public void HideDeathScreen()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        if (spectatePromptObject != null)
        {
            spectatePromptObject.SetActive(false);
        }

        if (spectatePromptText != null)
        {
            spectatePromptText.gameObject.SetActive(false);
        }

        if (deathCanvasGroup != null)
        {
            deathCanvasGroup.alpha = 0f;
            deathCanvasGroup.blocksRaycasts = false;
            deathCanvasGroup.interactable = false;
        }

        if (deathPanel != null)
        {
            deathPanel.SetActive(false);
        }

        NightCrawler.UI.MichskyUIBridge.CloseModal(heatModalWindow);
    }

    public void RequestSkipToSpectator()
    {
        _skipRequested = true;
    }

    private IEnumerator FadeInDeathScreenRoutine()
    {
        if (deathCanvasGroup == null) yield break;

        deathCanvasGroup.gameObject.SetActive(true);
        deathCanvasGroup.blocksRaycasts = false; // Don't block camera orbit

        // 1. Fade in dramatic "YOU DIED" screen
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            deathCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }
        deathCanvasGroup.alpha = 1f;

        // 2. Hold death screen for emotional weight (or allow player to skip immediately with Space/Click/Button)
        float holdTimer = 2.5f;
        while (holdTimer > 0f)
        {
            holdTimer -= Time.deltaTime;
            if (_skipRequested) break;
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.anyKey.wasPressedThisFrame) break;
            if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) break;
            yield return null;
        }

        // 3. Smoothly fade out the black death curtain to reveal the live match
        float fadeOutDuration = 0.8f;
        float fadeOutElapsed = 0f;
        while (fadeOutElapsed < fadeOutDuration)
        {
            fadeOutElapsed += Time.deltaTime;
            deathCanvasGroup.alpha = Mathf.Clamp01(1f - (fadeOutElapsed / fadeOutDuration));
            yield return null;
        }
        deathCanvasGroup.alpha = 0f;
        deathCanvasGroup.blocksRaycasts = false;
        if (deathPanel != null) deathPanel.SetActive(false);

        // 4. Engage Spectator Mode seamlessly
        if (SpectatorController.Instance != null)
        {
            SpectatorController.Instance.StartSpectating();
        }
    }

    /// <summary>
    /// Shows an onscreen notification and adds an entry into the bottom-right scrollable feed.
    /// </summary>
    public void ShowAllyDeathNotification(string victimName)
    {
        PostPlayerDied(victimName);
    }

    /// <summary>
    /// Global helper to post any match/ally notification into the feed.
    /// </summary>
    public static void PostEvent(string message, Color? accentColor = null)
    {
        if (Instance != null)
        {
            Instance.AddAllyAlertEntry(message, accentColor);
        }
    }

    public void PostMatchEvent(string message, Color? accentColor = null)
    {
        AddAllyAlertEntry(message, accentColor);
    }

    public void PostPlayerJoined(string playerName)
    {
        AddAllyAlertEntry($"{playerName} has entered the match.", new Color(0.2f, 0.85f, 0.95f, 1f));
    }

    public void PostPlayerLeft(string playerName, bool wasDead = false)
    {
        string desc = wasDead 
            ? $"{playerName} (Fallen) has left the match." 
            : $"{playerName} has left the match.";
        AddAllyAlertEntry(desc, new Color(0.95f, 0.6f, 0.15f, 1f));
    }

    public void PostPlayerDied(string victimName)
    {
        AddAllyAlertEntry($"{victimName} has fallen.", new Color(0.95f, 0.2f, 0.2f, 1f));
    }

    public void PostDealResponse(string playerName, bool accepted)
    {
        if (accepted)
        {
            AddAllyAlertEntry($"{playerName} accepted your dark pact!", new Color(0.2f, 0.9f, 0.4f, 1f));
        }
        else
        {
            AddAllyAlertEntry($"{playerName} rejected your dark pact.", new Color(0.95f, 0.35f, 0.2f, 1f));
        }
    }

    public void PostPossessionEvent(string message, Color accentColor)
    {
        AddAllyAlertEntry(message, accentColor);
    }

    /// <summary>
    /// Appends a new alert item into the scrollable feed using the user's template or prefab.
    /// </summary>
    public void AddAllyAlertEntry(string message, Color? accentColor = null)
    {
        // Deduplication: ignore identical messages received within 4 seconds
        if (message == _lastAlertMessage && (Time.time - _lastAlertTime) < 4f)
        {
            return;
        }
        _lastAlertMessage = message;
        _lastAlertTime = Time.time;

        if (allyAlertContent == null && allyAlertScrollRect != null)
        {
            allyAlertContent = allyAlertScrollRect.content;
        }

        if (allyAlertContent == null) return;

        if (allyAlertScrollRect != null && !allyAlertScrollRect.gameObject.activeInHierarchy)
        {
            allyAlertScrollRect.gameObject.SetActive(true);
        }

        // Cull oldest if max reached
        while (allyAlertContent.childCount >= maxAlerts && allyAlertContent.childCount > 0)
        {
            Destroy(allyAlertContent.GetChild(0).gameObject);
        }

        GameObject entryObj = null;
        if (alertItemPrefab != null)
        {
            entryObj = Instantiate(alertItemPrefab, allyAlertContent);
        }
        else if (allyDeathText != null)
        {
            entryObj = Instantiate(allyDeathText.gameObject, allyAlertContent);
        }

        if (entryObj != null)
        {
            entryObj.SetActive(true);
            var txt = entryObj.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                txt.text = message;
            }

            if (accentColor.HasValue)
            {
                foreach (var img in entryObj.GetComponentsInChildren<UnityEngine.UI.Image>())
                {
                    if (img.gameObject.name.ToLower().Contains("accent") || img.gameObject.name.ToLower().Contains("bar"))
                    {
                        img.color = accentColor.Value;
                        break;
                    }
                }
            }

            // Animate card in, keep on screen, then animate card out and destroy
            StartCoroutine(AnimateAlertEntryLifecycle(entryObj, alertLifetime));
        }

        StartCoroutine(ScrollToBottomRoutine());
    }

    private IEnumerator AnimateAlertEntryLifecycle(GameObject entry, float lifetime)
    {
        if (entry == null) yield break;

        var cg = entry.GetComponent<CanvasGroup>();
        if (cg == null) cg = entry.AddComponent<CanvasGroup>();

        // 1. Animate In (Fade in 0.25s + subtle pop from 0.90 to 1.0 scale)
        cg.alpha = 0f;
        entry.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        float inElapsed = 0f;
        float inDuration = 0.25f;
        while (inElapsed < inDuration)
        {
            if (entry == null) yield break;
            inElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(inElapsed / inDuration);
            float ease = Mathf.Sin(t * Mathf.PI * 0.5f); // Smooth ease-out
            cg.alpha = ease;
            entry.transform.localScale = Vector3.Lerp(new Vector3(0.9f, 0.9f, 1f), Vector3.one, ease);
            yield return null;
        }

        if (entry == null) yield break;
        cg.alpha = 1f;
        entry.transform.localScale = Vector3.one;

        // 2. Visible on-screen duration
        yield return new WaitForSeconds(lifetime);

        if (entry == null) yield break;

        // 3. Animate Out (Fade out 0.35s + subtle scale down)
        float outElapsed = 0f;
        float outDuration = 0.35f;
        while (outElapsed < outDuration)
        {
            if (entry == null) yield break;
            outElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(outElapsed / outDuration);
            float ease = t * t; // Smooth ease-in fade
            cg.alpha = 1f - ease;
            entry.transform.localScale = Vector3.Lerp(Vector3.one, new Vector3(0.9f, 0.9f, 1f), ease);
            yield return null;
        }

        // 4. Clean up / destroy
        if (entry != null)
        {
            Destroy(entry);
        }
    }

    private IEnumerator ScrollToBottomRoutine()
    {
        yield return null; // Wait 1 frame for Layout rebuild
        if (allyAlertScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            allyAlertScrollRect.verticalNormalizedPosition = 0f; // Scroll to newest entry
        }
    }
}
