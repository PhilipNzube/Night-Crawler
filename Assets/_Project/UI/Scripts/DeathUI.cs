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

    [Header("Optional Ally Death Banner (Other Players)")]
    [Tooltip("Banner displayed when a teammate dies.")]
    public GameObject allyDeathBanner;

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
            // Replace the scene-authored InputAction (was hard-wired to Space) with the player's binding
            KeybindingManager.BindHotkeyEvent(heatSpectateHotkey, "SpectatePlayer");
            heatSpectateHotkey.onHotkeyPress.AddListener(RequestSkipToSpectator);
        }

        if (spectatePromptText == null && deathPanel != null)
        {
            spectatePromptText = deathPanel.transform.Find("DeathSkipPromptText")?.GetComponent<TMP_Text>();
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
        if (allyDeathBanner != null)
        {
            allyDeathBanner.SetActive(true);
        }

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
            var sr = allyDeathBanner.GetComponentInChildren<ScrollRect>(true);
            if (sr != null)
            {
                allyAlertScrollRect = sr;
                if (sr.content != null) allyAlertContent = sr.content;
            }
        }

        // Ensure content has a layout group and size fitter so alert entries are auto-positioned properly
        if (allyAlertContent != null)
        {
            RectTransform contentRt = allyAlertContent as RectTransform;
            if (contentRt != null)
            {
                contentRt.pivot = new Vector2(0.5f, 0f);
                contentRt.anchorMin = new Vector2(0f, 0f);
                contentRt.anchorMax = new Vector2(1f, 0f);
                contentRt.anchoredPosition = Vector2.zero;
            }

            var vlg = allyAlertContent.GetComponent<VerticalLayoutGroup>();
            if (vlg == null)
            {
                vlg = allyAlertContent.gameObject.AddComponent<VerticalLayoutGroup>();
            }
            vlg.childAlignment = TextAnchor.LowerRight;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childScaleWidth = false;
            vlg.childScaleHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;

            var csf = allyAlertContent.GetComponent<ContentSizeFitter>();
            if (csf == null)
            {
                csf = allyAlertContent.gameObject.AddComponent<ContentSizeFitter>();
            }
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
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

        // ── Close pause menu if it was open ────────────────────────────────
        if (PauseManager.IsGamePaused && PauseManager.Instance != null)
        {
            PauseManager.Instance.ResumeGame();
        }

        if (deathPanel != null) deathPanel.SetActive(true);
        if (titleText != null) titleText.text = title;

        // Immediately hide any active deal tracker HUD or deal notification prompts
        if (NightCrawler.UI.ActiveDealMissionHUD.Instance != null)
        {
            NightCrawler.UI.ActiveDealMissionHUD.Instance.Hide();
        }
        if (DealNotificationUI.Instance != null)
        {
            DealNotificationUI.Instance.Hide();
        }
        if (NightCrawler.UI.DealCompletionModalUI.Instance != null)
        {
            NightCrawler.UI.DealCompletionModalUI.Instance.Hide();
        }
        if (NightCrawler.UI.DealFailureModalUI.Instance != null)
        {
            NightCrawler.UI.DealFailureModalUI.Instance.Hide();
        }



        // ── Determine whether there are survivors left to spectate ──────────
        bool hasSurvivors = HasAliveSurvivorsToSpectate();
        string spectateKeyStr = KeybindingManager.GetBoundKeyString("SpectatePlayer", "SPACE");

        _skipRequested = false;
        if (spectatePromptObject != null)
        {
            spectatePromptObject.SetActive(hasSurvivors);
            if (subtitleText != null) subtitleText.text = subtitle;
        }
        else if (spectatePromptText != null)
        {
            if (subtitleText != null) subtitleText.text = subtitle;
            if (hasSurvivors)
            {
                spectatePromptText.gameObject.SetActive(true);
                spectatePromptText.text = $"<b>[{spectateKeyStr}]</b> TO SPECTATE PLAYERS";
            }
            else
            {
                spectatePromptText.gameObject.SetActive(false);
            }
        }
        else if (subtitleText != null)
        {
            subtitleText.text = hasSurvivors
                ? $"{subtitle}\n\n<size=85%><b>Press [{spectateKeyStr}] to Spectate Players</b></size>"
                : subtitle;
        }

        // Also hide or update the Heat HotkeyEvent spectate button
        if (heatSpectateHotkey != null)
        {
            heatSpectateHotkey.gameObject.SetActive(hasSurvivors);
            if (hasSurvivors)
            {
                SetHotkeyVisual(heatSpectateHotkey, spectateKeyStr, "SPECTATE PLAYERS");
            }
        }

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeInDeathScreenRoutine(hasSurvivors));
    }

    /// <summary>
    /// Sets the visual text elements on a Michsky Heat HotkeyEvent button.
    /// </summary>
    public static void SetHotkeyVisual(Michsky.UI.Heat.HotkeyEvent hotkey, string keyStr, string labelStr)
    {
        if (hotkey == null) return;
        hotkey.keyID = keyStr;
        hotkey.hotkeyLabel = labelStr;
        hotkey.SetLabel(labelStr);
        hotkey.UpdateUI();

        var tmps = hotkey.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        foreach (var t in tmps)
        {
            if (t == null) continue;
            string n = t.gameObject.name.ToLower();
            if (n.Contains("label"))
            {
                t.text = labelStr;
            }
            else
            {
                t.text = keyStr;
            }
        }
    }

    /// <summary>
    /// Called by SpectatorController when the last watched survivor dies.
    /// Exits spectator mode and returns to this player's own death screen.
    /// </summary>
    public void ReturnFromSpectatorToDeath()
    {
        if (SpectatorController.Instance != null && SpectatorController.Instance.IsSpectating)
        {
            SpectatorController.Instance.StopSpectating();
        }

        // Close pause menu if somehow open
        if (PauseManager.IsGamePaused && PauseManager.Instance != null)
        {
            PauseManager.Instance.ResumeGame();
        }

        if (deathPanel != null) deathPanel.SetActive(true);

        // Keep spectate prompt available if survivors are still alive!
        bool hasSurvivors = HasAliveSurvivorsToSpectate();
        string spectateKeyStr = KeybindingManager.GetBoundKeyString("SpectatePlayer", "SPACE");

        if (spectatePromptObject != null) spectatePromptObject.SetActive(hasSurvivors);
        if (spectatePromptText != null)
        {
            spectatePromptText.gameObject.SetActive(hasSurvivors);
            if (hasSurvivors)
            {
                spectatePromptText.text = $"<b>[{spectateKeyStr}]</b> TO SPECTATE PLAYERS";
            }
        }
        if (heatSpectateHotkey != null)
        {
            heatSpectateHotkey.gameObject.SetActive(hasSurvivors);
            if (hasSurvivors)
            {
                SetHotkeyVisual(heatSpectateHotkey, spectateKeyStr, "SPECTATE PLAYERS");
            }
        }

        if (subtitleText != null)
        {
            subtitleText.text = hasSurvivors
                ? "Your soul has fallen. Allies can still loot your body."
                : "All survivors have fallen.";
        }

        if (deathCanvasGroup != null)
        {
            deathCanvasGroup.gameObject.SetActive(true);
            deathCanvasGroup.alpha = 1f;
            deathCanvasGroup.blocksRaycasts = hasSurvivors;
            deathCanvasGroup.interactable = hasSurvivors;
        }

        Debug.Log($"[DeathUI] Returned from spectator — hasSurvivors: {hasSurvivors}");
    }

    /// <summary>
    /// Returns true if there is at least one alive non-local Investigator to spectate.
    /// Strictly filters out any monsters, the Girl entity, corpses, and self.
    /// </summary>
    public bool HasAliveSurvivorsToSpectate()
    {
        ulong localId = Unity.Netcode.NetworkManager.Singleton != null
            ? Unity.Netcode.NetworkManager.Singleton.LocalClientId
            : ulong.MaxValue;

        var allHealths = FindObjectsByType<TargetHealth>(FindObjectsSortMode.None);
        foreach (var th in allHealths)
        {
            if (th == null || th.gameObject == null) continue;

            // Strict monster exclusion: check components and names
            if (th.GetComponent<MonsterAI>() != null ||
                th.GetComponent<MonsterController>() != null ||
                th.GetComponentInChildren<MonsterAI>() != null ||
                th.GetComponentInChildren<MonsterController>() != null ||
                th.name.ToLower().Contains("monster") ||
                th.name.ToLower().Contains("zombie") ||
                th.name.ToLower().Contains("crawler") ||
                th.name.ToLower().Contains("berserker") ||
                th.name.ToLower().Contains("brute"))
            {
                continue;
            }

            // Exclude self
            var netObj = th.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.OwnerClientId == localId) continue;

            // Skip the Girl / Wraith
            if (th.GetComponent<GirlPossession>() != null || th.GetComponent<GirlStealth>() != null ||
                th.gameObject.name.ToLower().Contains("girl") || th.gameObject.name.ToLower().Contains("wraith")) continue;

            if (th.isCorpse.Value || th.CurrentHealth <= 0f) continue;
            if (th.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead) continue;

            // Must be living human investigator
            bool isInvestigator = th.GetComponent<StarterAssets.ThirdPersonController>() != null ||
                                  th.GetComponent<InvestigatorCombatNet>() != null ||
                                  th.GetComponent<NetworkPlayer>() != null ||
                                  (netObj != null && netObj.IsPlayerObject);

            if (isInvestigator)
            {
                return true;
            }
        }
        return false;
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
    }

    public void RequestSkipToSpectator()
    {
        if (!HasAliveSurvivorsToSpectate()) return;
        _skipRequested = true;
    }

    private void Update()
    {
        if (deathPanel == null || !deathPanel.activeSelf) return;

        bool hasSurvivors = HasAliveSurvivorsToSpectate();

        if (!hasSurvivors)
        {
            if (spectatePromptObject != null && spectatePromptObject.activeSelf) spectatePromptObject.SetActive(false);
            if (spectatePromptText != null && spectatePromptText.gameObject.activeSelf) spectatePromptText.gameObject.SetActive(false);
            if (heatSpectateHotkey != null && heatSpectateHotkey.gameObject.activeSelf) heatSpectateHotkey.gameObject.SetActive(false);
            return;
        }

        // Only allow hotkey trigger once the death screen has finished fading in
        if (deathCanvasGroup != null && deathCanvasGroup.alpha >= 0.9f)
        {
            bool triggerSpectate = KeybindingManager.IsActionTriggered("SpectatePlayer")
                || (KeybindingManager.Instance == null && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame);

            if (triggerSpectate)
            {
                RequestSkipToSpectator();
            }
        }
    }

    private IEnumerator FadeInDeathScreenRoutine(bool hasSurvivors)
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

        // 2. If no survivors to spectate, stay on death screen permanently — nothing more to do
        if (!hasSurvivors)
        {
            _fadeCoroutine = null;
            yield break;
        }

        // 3. Hold death screen for emotional weight (or allow player to skip immediately with a key press)
        float holdTimer = 2.5f;
        while (holdTimer > 0f)
        {
            holdTimer -= Time.deltaTime;
            if (_skipRequested) break;
            // Only the bound Spectate Survivors input (handled in Update / HotkeyEvent) may skip the hold
            yield return null;
        }

        // 4. Smoothly fade out the black death curtain to reveal the live match
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

        // 5. Engage Spectator Mode seamlessly (re-check survivors one last time in case they all died during the hold)
        if (HasAliveSurvivorsToSpectate() && SpectatorController.Instance != null)
        {
            SpectatorController.Instance.StartSpectating();
        }
        else
        {
            // All gone by the time the timer ran — resurface the death screen
            ReturnFromSpectatorToDeath();
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
        string msg = accepted
            ? $"{playerName} accepted your dark deal!"
            : $"{playerName} rejected your dark deal.";

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification(msg, 4f);
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

        if (allyDeathBanner != null && !allyDeathBanner.activeSelf)
        {
            allyDeathBanner.SetActive(true);
        }

        if (allyAlertScrollRect != null && !allyAlertScrollRect.gameObject.activeSelf)
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
            try
            {
                entryObj = Instantiate(alertItemPrefab, allyAlertContent);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DeathUI] Failed to instantiate alertItemPrefab: {ex.Message}. Falling back to procedural card.");
                entryObj = null;
            }
        }

        // Procedural fallback if no prefab assigned or instantiation failed
        if (entryObj == null)
        {
            entryObj = CreateProceduralAlertEntry(message, accentColor ?? new Color(0.2f, 0.85f, 0.95f, 1f));
        }

        if (entryObj != null)
        {
            RectTransform rt = entryObj.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localPosition = Vector3.zero;
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }

            var le = entryObj.GetComponent<LayoutElement>();
            if (le == null) le = entryObj.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            le.flexibleWidth = 1f;

            entryObj.SetActive(true);

            // Heat UI QuestItem support: ensure it doesn't self-minimize/disable on Start()
            var questItem = entryObj.GetComponent<Michsky.UI.Heat.QuestItem>() ?? entryObj.GetComponentInChildren<Michsky.UI.Heat.QuestItem>();
            if (questItem != null)
            {
                questItem.defaultState = Michsky.UI.Heat.QuestItem.DefaultState.Expanded;
                questItem.useLocalization = false;
                questItem.questText = message;
                questItem.minimizeAfter = 0; // DeathUI handles lifecycle duration to prevent conflicts
                questItem.UpdateUI();

                var anim = questItem.GetComponent<Animator>();
                if (anim != null) anim.enabled = false; // Prevent animator from conflicting with fade coroutine
                questItem.enabled = false; // Prevent Start() from calling SetActive(false)
            }

            var txt = entryObj.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                txt.text = message;

                // Ensure RectTransform stretches across the card with proper margins
                RectTransform textRt = txt.rectTransform;
                if (textRt != null)
                {
                    textRt.anchorMin = new Vector2(0f, 0f);
                    textRt.anchorMax = new Vector2(1f, 1f);
                    textRt.pivot = new Vector2(0f, 0.5f);
                    textRt.offsetMin = new Vector2(16f, 2f);  // Left padding (keeps space for accent bar)
                    textRt.offsetMax = new Vector2(-12f, -2f); // Right padding
                }

                // Professional single-line truncation with Ellipsis
                txt.enableAutoSizing = false;
                txt.fontSize = 13.5f;
                txt.enableWordWrapping = false;
                txt.overflowMode = TextOverflowModes.Ellipsis;
                txt.alignment = TextAlignmentOptions.MidlineLeft;
            }

            // Ensure viewport / scroll rect has a RectMask2D and does not extend beyond screen bounds
            if (allyAlertScrollRect != null)
            {
                var mask = allyAlertScrollRect.GetComponent<RectMask2D>();
                if (mask == null && allyAlertScrollRect.viewport != null)
                    mask = allyAlertScrollRect.viewport.GetComponent<RectMask2D>();
                if (mask == null)
                {
                    mask = allyAlertScrollRect.gameObject.AddComponent<RectMask2D>();
                }

                var srRt = allyAlertScrollRect.GetComponent<RectTransform>();
                if (srRt != null && srRt.offsetMax.x > 0f)
                {
                    srRt.offsetMax = new Vector2(0f, srRt.offsetMax.y);
                }
            }

            if (accentColor.HasValue)
            {
                foreach (var img in entryObj.GetComponentsInChildren<UnityEngine.UI.Image>())
                {
                    if (img.gameObject.name.ToLower().Contains("accent") || img.gameObject.name.ToLower().Contains("bar") || img.gameObject.name.ToLower().Contains("indicator"))
                    {
                        img.color = accentColor.Value;
                        break;
                    }
                }
            }

            // Animate card in with spring easing, hold, and animate card out
            StartCoroutine(AnimateAlertEntryLifecycle(entryObj, alertLifetime));
        }

        StartCoroutine(ScrollToBottomRoutine());
    }

    private GameObject CreateProceduralAlertEntry(string message, Color accent)
    {
        if (allyAlertContent == null) return null;

        GameObject card = new GameObject("AllyAlertEntry", typeof(RectTransform), typeof(CanvasGroup), typeof(UnityEngine.UI.Image), typeof(LayoutElement));
        card.transform.SetParent(allyAlertContent, false);

        var rt = card.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 44f);
        rt.localScale = Vector3.one;

        var le = card.GetComponent<LayoutElement>();
        le.preferredHeight = 44f;
        le.flexibleWidth = 1f;

        var img = card.GetComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.07f, 0.09f, 0.13f, 0.90f); // Sleek modern translucent backdrop

        // Left accent bar
        GameObject accentObj = new GameObject("AccentBar", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        accentObj.transform.SetParent(card.transform, false);
        var accentRt = accentObj.GetComponent<RectTransform>();
        accentRt.anchorMin = new Vector2(0f, 0f);
        accentRt.anchorMax = new Vector2(0f, 1f);
        accentRt.pivot = new Vector2(0f, 0.5f);
        accentRt.anchoredPosition = Vector2.zero;
        accentRt.sizeDelta = new Vector2(4f, 0f);
        var accentImg = accentObj.GetComponent<UnityEngine.UI.Image>();
        accentImg.color = accent;

        // Text
        GameObject textObj = new GameObject("AlertText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(card.transform, false);
        var textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0f, 0f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.offsetMin = new Vector2(12f, 2f);
        textRt.offsetMax = new Vector2(-8f, -2f);

        var tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = 13.5f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(0.95f, 0.96f, 0.98f, 1f);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        return card;
    }

    private IEnumerator AnimateAlertEntryLifecycle(GameObject entry, float lifetime)
    {
        if (entry == null) yield break;

        var cg = entry.GetComponent<CanvasGroup>();
        if (cg == null) cg = entry.AddComponent<CanvasGroup>();

        RectTransform rt = entry.GetComponent<RectTransform>();
        Vector3 initialScale = rt != null ? rt.localScale : Vector3.one;
        if (initialScale == Vector3.zero) initialScale = Vector3.one;

        // 1. Animate In (Smooth Ease-Out Back Spring curve)
        cg.alpha = 0f;
        if (rt != null) rt.localScale = initialScale * 0.88f;

        float inElapsed = 0f;
        float inDuration = 0.30f;
        while (inElapsed < inDuration)
        {
            if (entry == null) yield break;
            inElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(inElapsed / inDuration);
            float ease = 1f + 2.70158f * Mathf.Pow(t - 1f, 3) + 1.70158f * Mathf.Pow(t - 1f, 2);

            cg.alpha = Mathf.Clamp01(t * 1.5f);
            if (rt != null) rt.localScale = Vector3.LerpUnclamped(initialScale * 0.88f, initialScale, ease);
            yield return null;
        }

        if (entry == null) yield break;
        cg.alpha = 1f;
        if (rt != null) rt.localScale = initialScale;

        // 2. Visible on-screen duration (hold timer, with 14s absolute failsafe)
        float holdElapsed = 0f;
        float totalTimer = 0f;
        while (holdElapsed < lifetime && totalTimer < lifetime + 8f)
        {
            if (entry == null) yield break;
            totalTimer += Time.deltaTime;
            if (LoadingScreen.Instance == null || !LoadingScreen.Instance.IsLoadingScreenActive)
            {
                holdElapsed += Time.deltaTime;
            }
            yield return null;
        }

        if (entry == null) yield break;

        // 3. Animate Out (Smooth Ease-In)
        float outElapsed = 0f;
        float outDuration = 0.25f;
        while (outElapsed < outDuration)
        {
            if (entry == null) yield break;
            outElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(outElapsed / outDuration);
            float ease = t * t;

            cg.alpha = 1f - ease;
            if (rt != null) rt.localScale = Vector3.Lerp(initialScale, initialScale * 0.88f, ease);
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
