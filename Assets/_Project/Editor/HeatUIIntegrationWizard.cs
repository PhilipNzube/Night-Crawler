#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Michsky.UI.Heat;
using NightCrawler.UI;
using NightCrawler.Economy;
using NightCrawler.Economy.UI;
using NightCrawler.Monsters;
using NightCrawler.Systems;

namespace NightCrawler.EditorTools
{
    /// <summary>
    /// Automated Heat UI Integration & Visual Reskinning Wizard for Night Crawler.
    /// Provides:
    /// 1. Automatic Backup & 1-Click Revert/Restore for scenes.
    /// 2. Global Gothic Horror theming in Heat UI Manager.
    /// 3. Scene-wide visual restyler (swaps all text fonts to Heat's RobotoCondensed, 
    ///    restyles all buttons with dark-crimson glassmorphism, restyles panels).
    /// 4. Automated placement and wiring of Heat UI components.
    /// </summary>
    public class HeatUIIntegrationWizard : EditorWindow
    {
        private const string LOBBY_SCENE_PATH = "Assets/_Project/Scenes/LobbyScene.unity";
        private const string GAME_SCENE_PATH = "Assets/_Project/Scenes/GameScene.unity";
        private const string HEAT_PREFABS_ROOT = "Assets/ThirdParty/Heat - Complete Modern UI/Prefabs/";
        private const string HEAT_FONTS_ROOT = "Assets/ThirdParty/Heat - Complete Modern UI/Fonts/";

        private Vector2 _scrollPos;
        private string _statusMessage = "Ready. You can apply the Heat UI style, auto-integrate prefabs, or revert anytime.";
        private MessageType _statusType = MessageType.Info;

        [MenuItem("Night Crawler/Heat UI/Auto-Integration Wizard %#H", priority = 10)]
        [MenuItem("Tools/Night Crawler/Heat UI Wizard", priority = 10)]
        public static void ShowWindow()
        {
            var window = GetWindow<HeatUIIntegrationWizard>("Heat UI Wizard");
            window.minSize = new Vector2(500, 620);
            window.Show();
        }

        [MenuItem("Night Crawler/Heat UI/1-Click Run Full Integration", priority = 11)]
        public static void RunFullIntegrationMenu()
        {
            if (EditorUtility.DisplayDialog("Auto-Integrate Heat UI",
                "This will automatically:\n" +
                "1. Create safety backups of your scenes\n" +
                "2. Apply the Gothic Horror palette (Crimson/Obsidian/Cinders) to Heat UI\n" +
                "3. Auto-restyle all texts, buttons, and panels across LobbyScene & GameScene\n" +
                "4. Instantiate and wire Heat UI staking inputs, buttons, and modal dialogs\n\n" +
                "(You can revert back to your original scenes anytime with the Revert button)\n\n" +
                "Do you want to proceed?", "Yes, Auto-Integrate", "Cancel"))
            {
                RunAllSteps();
            }
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.Space(10);
            GUILayout.Label("NIGHT CRAWLER — HEAT UI AUTOMATOR & RESKINNER", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Transform your basic UI into AAA Michsky Heat UI! Automatically converts fonts, colors, " +
                "button skins, panel backings, and hooks up the Staking & Upgrade systems. " +
                "Automatic backups are created so you can revert anytime.", MessageType.None);
            EditorGUILayout.Space(8);

            // Status message
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
                EditorGUILayout.Space(6);
            }

            // --- MASTER 1-CLICK BUTTON ---
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.2f);
            if (GUILayout.Button("⚡ 1-CLICK FULL AUTO-INTEGRATION & RESKIN", GUILayout.Height(44)))
            {
                RunAllSteps();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(12);

            // --- STEP 1: THEME CONFIGURATION ---
            EditorGUILayout.LabelField("Step 1: Theme Setup", EditorStyles.boldLabel);
            if (GUILayout.Button("🔥 Apply Gothic Horror Theme to Heat UI Manager", GUILayout.Height(30)))
            {
                ApplyHorrorTheme();
                _statusMessage = "Applied Gothic Horror palette (Blood Crimson, Obsidian Slate, Cinder Ember) to Heat UI Manager!";
                _statusType = MessageType.Info;
            }
            EditorGUILayout.Space(10);

            // --- STEP 2: LOBBY SCENE ---
            EditorGUILayout.LabelField("Step 2: Lobby Scene Auto-Reskin & Integration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Automatically restyles all text fonts to Heat's RobotoCondensed, styles all buttons with crimson hover glow, and wires Staking & Upgrades.", MessageType.None);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🏛 Auto-Transform LobbyScene", GUILayout.Height(32)))
            {
                CreateBackup(LOBBY_SCENE_PATH);
                IntegrateLobbyScene();
                _statusMessage = "LobbyScene transformed with Heat UI fonts, button skins, staking inputs, and upgrade panels!";
                _statusType = MessageType.Info;
            }

            bool lobbyBackupExists = File.Exists(GetBackupPath(LOBBY_SCENE_PATH));
            GUI.enabled = lobbyBackupExists;
            if (GUILayout.Button("⏪ Revert LobbyScene", GUILayout.Height(32), GUILayout.Width(150)))
            {
                if (EditorUtility.DisplayDialog("Revert LobbyScene", "Restore LobbyScene back to the backup before integration?", "Yes, Revert", "Cancel"))
                {
                    RestoreBackup(LOBBY_SCENE_PATH);
                    _statusMessage = "LobbyScene successfully reverted to its previous backup!";
                    _statusType = MessageType.Warning;
                }
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            // --- STEP 3: GAME SCENE ---
            EditorGUILayout.LabelField("Step 3: Game Scene Auto-Reskin & Integration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Automatically restyles GameScene HUD, connects Heat Deal Modal, Active Deal Mission Tracker, and Pot Badge.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🎮 Auto-Transform GameScene", GUILayout.Height(32)))
            {
                CreateBackup(GAME_SCENE_PATH);
                IntegrateGameScene();
                _statusMessage = "GameScene transformed with Heat UI fonts, buttons, Deal modal, and mission tracker!";
                _statusType = MessageType.Info;
            }

            bool gameBackupExists = File.Exists(GetBackupPath(GAME_SCENE_PATH));
            GUI.enabled = gameBackupExists;
            if (GUILayout.Button("⏪ Revert GameScene", GUILayout.Height(32), GUILayout.Width(150)))
            {
                if (EditorUtility.DisplayDialog("Revert GameScene", "Restore GameScene back to the backup before integration?", "Yes, Revert", "Cancel"))
                {
                    RestoreBackup(GAME_SCENE_PATH);
                    _statusMessage = "GameScene successfully reverted to its previous backup!";
                    _statusType = MessageType.Warning;
                }
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(14);

            // --- EXPLANATION ACCORDION ---
            EditorGUILayout.LabelField("What Are the Upgrade Panels?", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Heat_InvestigatorUpgradePanel and Heat_GirlUpgradePanel are modal popup windows for spending Cinders on persistent stats " +
                "(Damage Resistance, Weapon Damage, Mask Filter, Spiritual Exorcism, Dead Summon Charges).\n\n" +
                "They are deactivated by default because they only pop open when the player clicks the 'UPGRADES' button in the lobby or character selection screen.",
                MessageType.Info);

            EditorGUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        public static void RunAllSteps()
        {
            CreateBackup(LOBBY_SCENE_PATH);
            CreateBackup(GAME_SCENE_PATH);
            ApplyHorrorTheme();
            IntegrateLobbyScene();
            IntegrateGameScene();
            EditorUtility.DisplayDialog("Heat UI Integration Complete", 
                "All scenes have been automatically transformed with Heat UI fonts, buttons, panel styling, and economy integration!\n\n" +
                "Backups were created. If you ever want to revert, open the Heat UI Wizard and click 'Revert'.", "Awesome!");
        }

        // =========================================================================
        //  BACKUP & RESTORE SYSTEM
        // =========================================================================

        private static string GetBackupPath(string scenePath) => scenePath + ".backup";

        public static void CreateBackup(string scenePath)
        {
            try
            {
                string backupPath = GetBackupPath(scenePath);
                if (File.Exists(scenePath))
                {
                    File.Copy(scenePath, backupPath, true);
                    AssetDatabase.Refresh();
                    Debug.Log($"[HeatUIIntegrationWizard] Created scene backup: {backupPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HeatUIIntegrationWizard] Failed to create backup: {ex.Message}");
            }
        }

        public static void RestoreBackup(string scenePath)
        {
            try
            {
                string backupPath = GetBackupPath(scenePath);
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, scenePath, true);
                    AssetDatabase.Refresh();
                    EditorSceneManager.OpenScene(scenePath);
                    Debug.Log($"[HeatUIIntegrationWizard] Restored scene from backup: {scenePath}");
                }
                else
                {
                    Debug.LogWarning($"[HeatUIIntegrationWizard] No backup found at: {backupPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HeatUIIntegrationWizard] Failed to restore backup: {ex.Message}");
            }
        }

        // =========================================================================
        //  STEP 1: THEME SETUP
        // =========================================================================

        public static void ApplyHorrorTheme()
        {
            var uiManager = Resources.Load<UIManager>("Heat UI Manager");
            if (uiManager == null)
            {
                Debug.LogError("[HeatUIIntegrationWizard] Cannot find 'Heat UI Manager' in Resources!");
                return;
            }

            Undo.RecordObject(uiManager, "Apply Horror Palette");

            // Dark Gothic Horror / Mining Disaster Palette
            uiManager.accentColor = new Color32(231, 76, 60, 255);       // Blood Crimson (#E74C3C)
            uiManager.accentColorInvert = new Color32(18, 20, 26, 255);   // Dark Obsidian
            uiManager.primaryColor = new Color32(245, 245, 245, 255);     // Clean off-white
            uiManager.secondaryColor = new Color32(22, 24, 30, 255);      // Obsidian charcoal
            uiManager.negativeColor = new Color32(192, 57, 43, 255);      // Deep Crimson
            uiManager.backgroundColor = new Color32(10, 11, 15, 255);     // Abyss Deep Black

            EditorUtility.SetDirty(uiManager);
            AssetDatabase.SaveAssets();

            Debug.Log("[HeatUIIntegrationWizard] Successfully applied Gothic Horror theme to Heat UI Manager!");
        }

        // =========================================================================
        //  STEP 2: LOBBY SCENE INTEGRATION & RESKIN
        // =========================================================================

        public static void IntegrateLobbyScene()
        {
            var currentScene = EditorSceneManager.GetActiveScene();
            if (currentScene.path != LOBBY_SCENE_PATH)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(LOBBY_SCENE_PATH);
                }
                else
                {
                    return;
                }
            }

            // 1. Global Visual Reskin of All Existing Text, Buttons, and Panels
            AutoReskinSceneVisuals();

            // 2. Find Canvas
            var lobbyCanvas = GameObject.Find("LobbyCanvas");
            if (lobbyCanvas == null)
            {
                lobbyCanvas = FindAnyObjectByType<Canvas>()?.gameObject;
            }
            if (lobbyCanvas == null)
            {
                Debug.LogError("[HeatUIIntegrationWizard] LobbyCanvas not found in LobbyScene!");
                return;
            }

            // 3. Character Selection UI
            var charSelect = FindAnyObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
            if (charSelect != null)
            {
                Undo.RecordObject(charSelect, "Integrate Heat UI Character Select");
                Transform parent = charSelect.transform;

                // Heat Stake Input
                if (charSelect.stakeInputField == null)
                {
                    var inputPrefab = LoadHeatPrefab("UI Elements/Input Field/Input Field");
                    if (inputPrefab != null)
                    {
                        var heatInputGo = (GameObject)PrefabUtility.InstantiatePrefab(inputPrefab, parent);
                        heatInputGo.name = "Heat_StakeInputField";
                        var rect = heatInputGo.GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            rect.anchoredPosition = new Vector2(0, -180);
                            rect.sizeDelta = new Vector2(260, 48);
                        }

                        var tmpInput = heatInputGo.GetComponentInChildren<TMP_InputField>(true);
                        if (tmpInput != null)
                        {
                            tmpInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                            charSelect.stakeInputField = tmpInput;
                        }

                        var ifm = heatInputGo.GetComponent<InputFieldManager>();
                        if (ifm != null && ifm.inputText != null)
                        {
                            ifm.inputText.placeholder.GetComponent<TextMeshProUGUI>().text = "Stake Cinders (Min: 2 ₵)...";
                        }
                    }
                }

                // Heat Upgrades Button
                if (charSelect.openUpgradesButton == null)
                {
                    var btnPrefab = LoadHeatPrefab("UI Elements/Button/Button");
                    if (btnPrefab != null)
                    {
                        var heatBtnGo = (GameObject)PrefabUtility.InstantiatePrefab(btnPrefab, parent);
                        heatBtnGo.name = "Heat_Btn_Upgrades";
                        var rect = heatBtnGo.GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            rect.anchoredPosition = new Vector2(-160, -250);
                            rect.sizeDelta = new Vector2(180, 45);
                        }

                        var bm = heatBtnGo.GetComponent<ButtonManager>();
                        if (bm != null)
                        {
                            bm.buttonText = "UPGRADES";
                            bm.UpdateUI();
                        }

                        var uBtn = heatBtnGo.GetComponent<Button>() ?? heatBtnGo.GetComponentInChildren<Button>(true) ?? heatBtnGo.AddComponent<Button>();
                        charSelect.openUpgradesButton = uBtn;
                    }
                }

                // Heat Upgrade Panel
                if (charSelect.upgradePanel == null)
                {
                    var panelGo = new GameObject("Heat_InvestigatorUpgradePanel", typeof(RectTransform), typeof(CanvasGroup));
                    panelGo.transform.SetParent(lobbyCanvas.transform, false);
                    var pRect = panelGo.GetComponent<RectTransform>();
                    pRect.anchorMin = new Vector2(0.5f, 0.5f);
                    pRect.anchorMax = new Vector2(0.5f, 0.5f);
                    pRect.sizeDelta = new Vector2(560, 680);

                    var bg = panelGo.AddComponent<Image>();
                    bg.color = new Color(0.08f, 0.09f, 0.12f, 0.95f);

                    var itemsContGo = new GameObject("ItemsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                    itemsContGo.transform.SetParent(panelGo.transform, false);
                    var icRect = itemsContGo.GetComponent<RectTransform>();
                    icRect.anchorMin = new Vector2(0.05f, 0.1f);
                    icRect.anchorMax = new Vector2(0.95f, 0.9f);
                    icRect.offsetMin = Vector2.zero;
                    icRect.offsetMax = Vector2.zero;

                    var vlg = itemsContGo.GetComponent<VerticalLayoutGroup>();
                    vlg.spacing = 10;
                    vlg.childControlHeight = false;
                    vlg.childControlWidth = true;

                    var upgradeUI = panelGo.AddComponent<LobbyUpgradeUI>();
                    upgradeUI.viewMode = UpgradeViewMode.InvestigatorStats;
                    upgradeUI.panelRoot = panelGo;
                    upgradeUI.itemsContainer = itemsContGo.transform;
                    upgradeUI.openButton = charSelect.openUpgradesButton;

                    charSelect.upgradePanel = panelGo;
                    panelGo.SetActive(false);
                }

                EditorUtility.SetDirty(charSelect);
            }

            // 4. The Girl's Screen UI
            var girlScreen = FindAnyObjectByType<GirlPlayerScreen>(FindObjectsInactive.Include);
            if (girlScreen != null)
            {
                Undo.RecordObject(girlScreen, "Integrate Heat UI Girl Screen");
                Transform parent = girlScreen.transform;

                // Heat Girl Stake Input
                if (girlScreen.stakeInputField == null)
                {
                    var inputPrefab = LoadHeatPrefab("UI Elements/Input Field/Input Field");
                    if (inputPrefab != null)
                    {
                        var heatInputGo = (GameObject)PrefabUtility.InstantiatePrefab(inputPrefab, parent);
                        heatInputGo.name = "Heat_GirlStakeInputField";
                        var rect = heatInputGo.GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            rect.anchoredPosition = new Vector2(0, -140);
                            rect.sizeDelta = new Vector2(260, 48);
                        }

                        var tmpInput = heatInputGo.GetComponentInChildren<TMP_InputField>(true);
                        if (tmpInput != null)
                        {
                            tmpInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                            girlScreen.stakeInputField = tmpInput;
                        }

                        var ifm = heatInputGo.GetComponent<InputFieldManager>();
                        if (ifm != null && ifm.inputText != null)
                        {
                            ifm.inputText.placeholder.GetComponent<TextMeshProUGUI>().text = "Girl Stake (Min: 2 ₵)...";
                        }
                    }
                }

                // Heat Girl Upgrades Button
                if (girlScreen.openUpgradesButton == null)
                {
                    var btnPrefab = LoadHeatPrefab("UI Elements/Button/Button (Shop)");
                    if (btnPrefab == null) btnPrefab = LoadHeatPrefab("UI Elements/Button/Button");

                    if (btnPrefab != null)
                    {
                        var heatBtnGo = (GameObject)PrefabUtility.InstantiatePrefab(btnPrefab, parent);
                        heatBtnGo.name = "Heat_Btn_GirlUpgrades";
                        var rect = heatBtnGo.GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            rect.anchoredPosition = new Vector2(160, -250);
                            rect.sizeDelta = new Vector2(180, 45);
                        }

                        var bm = heatBtnGo.GetComponent<ButtonManager>();
                        if (bm != null)
                        {
                            bm.buttonText = "SPIRIT VAULT";
                            bm.UpdateUI();
                        }

                        var uBtn = heatBtnGo.GetComponent<Button>() ?? heatBtnGo.GetComponentInChildren<Button>(true) ?? heatBtnGo.AddComponent<Button>();
                        girlScreen.openUpgradesButton = uBtn;
                    }
                }

                // Heat Girl Upgrade Panel
                if (girlScreen.upgradePanel == null)
                {
                    var panelGo = new GameObject("Heat_GirlUpgradePanel", typeof(RectTransform), typeof(CanvasGroup));
                    panelGo.transform.SetParent(lobbyCanvas.transform, false);
                    var pRect = panelGo.GetComponent<RectTransform>();
                    pRect.anchorMin = new Vector2(0.5f, 0.5f);
                    pRect.anchorMax = new Vector2(0.5f, 0.5f);
                    pRect.sizeDelta = new Vector2(560, 680);

                    var bg = panelGo.AddComponent<Image>();
                    bg.color = new Color(0.12f, 0.08f, 0.09f, 0.95f);

                    var itemsContGo = new GameObject("ItemsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                    itemsContGo.transform.SetParent(panelGo.transform, false);
                    var icRect = itemsContGo.GetComponent<RectTransform>();
                    icRect.anchorMin = new Vector2(0.05f, 0.1f);
                    icRect.anchorMax = new Vector2(0.95f, 0.9f);
                    icRect.offsetMin = Vector2.zero;
                    icRect.offsetMax = Vector2.zero;

                    var vlg = itemsContGo.GetComponent<VerticalLayoutGroup>();
                    vlg.spacing = 10;
                    vlg.childControlHeight = false;
                    vlg.childControlWidth = true;

                    var upgradeUI = panelGo.AddComponent<LobbyUpgradeUI>();
                    upgradeUI.viewMode = UpgradeViewMode.GirlStats;
                    upgradeUI.panelRoot = panelGo;
                    upgradeUI.itemsContainer = itemsContGo.transform;
                    upgradeUI.openButton = girlScreen.openUpgradesButton;

                    girlScreen.upgradePanel = panelGo;
                    panelGo.SetActive(false);
                }

                EditorUtility.SetDirty(girlScreen);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[HeatUIIntegrationWizard] Successfully integrated & reskinned LobbyScene!");
        }

        // =========================================================================
        //  STEP 3: GAME SCENE INTEGRATION & RESKIN
        // =========================================================================

        public static void IntegrateGameScene()
        {
            var currentScene = EditorSceneManager.GetActiveScene();
            if (currentScene.path != GAME_SCENE_PATH)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(GAME_SCENE_PATH);
                }
                else
                {
                    return;
                }
            }

            // 1. Reskin All In-Game HUD Text and Buttons
            AutoReskinSceneVisuals();

            var hudCanvas = GameObject.Find("HUDCanvas");
            if (hudCanvas == null)
            {
                hudCanvas = FindAnyObjectByType<Canvas>()?.gameObject;
            }
            if (hudCanvas == null)
            {
                Debug.LogError("[HeatUIIntegrationWizard] HUDCanvas not found in GameScene!");
                return;
            }

            // 2. Deal Notification Modal Window
            var dealNotif = FindAnyObjectByType<DealNotificationUI>(FindObjectsInactive.Include);
            if (dealNotif == null)
            {
                var dealGo = new GameObject("DealNotificationUI");
                dealGo.transform.SetParent(hudCanvas.transform, false);
                dealNotif = dealGo.AddComponent<DealNotificationUI>();
            }

            if (dealNotif != null && dealNotif.panel == null)
            {
                Undo.RecordObject(dealNotif, "Integrate Heat UI Deal Notification");
                var modalPrefab = LoadHeatPrefab("UI Elements/Modal Window/Modal Window (Custom Content)");
                if (modalPrefab == null) modalPrefab = LoadHeatPrefab("UI Elements/Modal Window/Modal Window");

                if (modalPrefab != null)
                {
                    var modalGo = (GameObject)PrefabUtility.InstantiatePrefab(modalPrefab, dealNotif.transform);
                    modalGo.name = "Heat_DealModalWindow";
                    var mw = modalGo.GetComponent<ModalWindowManager>();
                    if (mw != null)
                    {
                        dealNotif.panel = modalGo;
                        if (mw.windowTitle != null) dealNotif.titleText = mw.windowTitle;
                        if (mw.windowDescription != null) dealNotif.termsText = mw.windowDescription;
                        if (mw.confirmButton != null)
                        {
                            dealNotif.acceptButton = mw.confirmButton.GetComponent<Button>() ?? mw.confirmButton.GetComponentInChildren<Button>(true) ?? mw.confirmButton.gameObject.AddComponent<Button>();
                            mw.confirmButton.buttonText = "ACCEPT PACT [Y]";
                            mw.confirmButton.UpdateUI();
                        }
                        if (mw.cancelButton != null)
                        {
                            dealNotif.declineButton = mw.cancelButton.GetComponent<Button>() ?? mw.cancelButton.GetComponentInChildren<Button>(true) ?? mw.cancelButton.gameObject.AddComponent<Button>();
                            mw.cancelButton.buttonText = "DECLINE [N]";
                            mw.cancelButton.UpdateUI();
                        }
                    }
                    modalGo.SetActive(false);
                    EditorUtility.SetDirty(dealNotif);
                }
            }

            // 3. Active Deal Mission Tracker HUD
            var activeMission = FindAnyObjectByType<ActiveDealMissionHUD>(FindObjectsInactive.Include);
            if (activeMission == null)
            {
                var missionGo = new GameObject("ActiveDealMissionHUD");
                missionGo.transform.SetParent(hudCanvas.transform, false);
                activeMission = missionGo.AddComponent<ActiveDealMissionHUD>();
            }

            // 4. Match Pot HUD Widget
            var potHud = FindAnyObjectByType<MatchPotHUD>(FindObjectsInactive.Include);
            if (potHud == null)
            {
                var potGo = new GameObject("MatchPotHUD", typeof(RectTransform));
                potGo.transform.SetParent(hudCanvas.transform, false);
                var pRect = potGo.GetComponent<RectTransform>();
                pRect.anchorMin = new Vector2(0.5f, 1f);
                pRect.anchorMax = new Vector2(0.5f, 1f);
                pRect.pivot = new Vector2(0.5f, 1f);
                pRect.anchoredPosition = new Vector2(0, -25);
                pRect.sizeDelta = new Vector2(280, 50);

                potHud = potGo.AddComponent<MatchPotHUD>();
            }

            // 5. Dead Spawn Manager & Tracker checks
            var spawnMgr = FindAnyObjectByType<DeadSpawnManager>(FindObjectsInactive.Include);
            if (spawnMgr == null)
            {
                var mgrGo = new GameObject("DeadSpawnManager");
                mgrGo.AddComponent<Unity.Netcode.NetworkObject>();
                mgrGo.AddComponent<DeadSpawnManager>();
            }

            var deadTracker = FindAnyObjectByType<DeadPlayerTracker>(FindObjectsInactive.Include);
            if (deadTracker == null)
            {
                var dtGo = new GameObject("DeadPlayerTracker");
                dtGo.AddComponent<Unity.Netcode.NetworkObject>();
                dtGo.AddComponent<DeadPlayerTracker>();
            }

            // 6. The Girl's Dead Monster Summon HUD
            var summonHud = FindAnyObjectByType<GirlMonsterSummonHUD>(FindObjectsInactive.Include);
            if (summonHud == null)
            {
                var summonGo = new GameObject("GirlMonsterSummonHUD");
                summonGo.transform.SetParent(hudCanvas.transform, false);
                summonGo.AddComponent<GirlMonsterSummonHUD>();
            }

            // 7. The Girl's Deal UI
            var girlDealUI = FindAnyObjectByType<GirlDealUI>(FindObjectsInactive.Include);
            if (girlDealUI == null)
            {
                var dealUIGo = new GameObject("GirlDealUI");
                dealUIGo.transform.SetParent(hudCanvas.transform, false);
                dealUIGo.AddComponent<GirlDealUI>();
            }

            // 8. Match End Result Screen
            var matchResult = FindAnyObjectByType<MatchResultOverlay>(FindObjectsInactive.Include);
            if (matchResult == null)
            {
                var resultGo = new GameObject("MatchResultOverlay");
                resultGo.transform.SetParent(hudCanvas.transform, false);
                resultGo.AddComponent<MatchResultOverlay>();
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[HeatUIIntegrationWizard] Successfully integrated & reskinned GameScene!");
        }

        // =========================================================================
        //  SCENE-WIDE VISUAL RESKINNER ENGINE
        // =========================================================================

        /// <summary>
        /// Automatically traverses all Text, Buttons, and Panels in the active scene
        /// and attaches Heat UI's native managers:
        /// - UIManagerText on every TextMeshProUGUI
        /// - UIManagerImage on every panel/container Image
        /// - UIElementSound on every Button
        /// - PanelManager on the main Canvas
        /// </summary>
        public static void AutoReskinSceneVisuals()
        {
            var uiManager = Resources.Load<UIManager>("Heat UI Manager");
            if (uiManager == null)
            {
                Debug.LogWarning("[HeatUIIntegrationWizard] 'Heat UI Manager' asset not found in Resources!");
            }

            var boldFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HEAT_FONTS_ROOT + "RobotoCondensed-Bold SDF.asset");
            var mediumFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HEAT_FONTS_ROOT + "RobotoCondensed-Medium SDF.asset");
            var regularFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HEAT_FONTS_ROOT + "RobotoCondensed-Regular SDF.asset");

            if (boldFont == null) boldFont = mediumFont ?? regularFont;
            if (mediumFont == null) mediumFont = boldFont;

            int textCount = 0;
            int buttonCount = 0;
            int imageCount = 0;

            // 1. Attach and Configure UIManagerText on All TextMeshPro Texts
            var allTexts = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var txt in allTexts)
            {
                if (txt == null) continue;
                Undo.RecordObject(txt.gameObject, "Heat UI Text Reskin");

                string goName = txt.gameObject.name.ToUpper();

                // Attach Heat UI's native UIManagerText component
                var mgrText = txt.gameObject.GetComponent<UIManagerText>();
                if (mgrText == null)
                {
                    mgrText = Undo.AddComponent<UIManagerText>(txt.gameObject);
                }

                if (mgrText != null)
                {
                    mgrText.UIManagerAsset = uiManager;
                    mgrText.useCustomFont = false;
                    mgrText.useCustomColor = false;

                    if (goName.Contains("TITLE") || goName.Contains("HEADER") || goName.Contains("NAME") || txt.fontSize >= 24)
                    {
                        mgrText.fontType = UIManagerText.FontType.Bold;
                        mgrText.colorType = UIManagerText.ColorType.Primary;
                        if (boldFont != null) txt.font = boldFont;
                        txt.color = new Color(0.96f, 0.96f, 0.96f, 1f);
                    }
                    else if (goName.Contains("ERROR") || goName.Contains("WARN"))
                    {
                        mgrText.fontType = UIManagerText.FontType.Medium;
                        mgrText.colorType = UIManagerText.ColorType.Negative;
                        if (mediumFont != null) txt.font = mediumFont;
                        txt.color = new Color(0.91f, 0.30f, 0.24f, 1f);
                    }
                    else
                    {
                        mgrText.fontType = UIManagerText.FontType.Regular;
                        mgrText.colorType = UIManagerText.ColorType.Primary;
                        if (mediumFont != null) txt.font = mediumFont;
                        txt.color = new Color(0.85f, 0.87f, 0.90f, 1f);
                    }

                    EditorUtility.SetDirty(mgrText);
                }

                EditorUtility.SetDirty(txt);
                textCount++;
            }

            // 2. Attach UIElementSound and Configure Colors on All Standard Buttons
            var allButtons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var btn in allButtons)
            {
                if (btn == null) continue;
                Undo.RecordObject(btn.gameObject, "Heat UI Button Reskin");

                btn.transition = Selectable.Transition.ColorTint;
                var colors = btn.colors;
                colors.normalColor = new Color(0.11f, 0.12f, 0.16f, 1f);     // Dark Obsidian
                colors.highlightedColor = new Color(0.91f, 0.30f, 0.24f, 1f); // Blood Crimson Hover Glow
                colors.pressedColor = new Color(0.75f, 0.22f, 0.17f, 1f);     // Deep Pressed Red
                colors.selectedColor = new Color(0.91f, 0.30f, 0.24f, 1f);
                colors.disabledColor = new Color(0.08f, 0.09f, 0.11f, 0.5f);
                colors.fadeDuration = 0.15f;
                btn.colors = colors;

                // Attach Heat UI's native audio sound component
                var soundComp = btn.gameObject.GetComponent<UIElementSound>();
                if (soundComp == null)
                {
                    soundComp = Undo.AddComponent<UIElementSound>(btn.gameObject);
                }
                if (soundComp != null)
                {
                    soundComp.enableHoverSound = true;
                    soundComp.enableClickSound = true;
                    EditorUtility.SetDirty(soundComp);
                }

                // Restyle child button text
                var btnText = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (btnText != null)
                {
                    Undo.RecordObject(btnText, "Heat UI Button Text Reskin");
                    if (boldFont != null) btnText.font = boldFont;
                    btnText.color = Color.white;
                    EditorUtility.SetDirty(btnText);
                }

                EditorUtility.SetDirty(btn);
                buttonCount++;
            }

            // 3. Attach UIManagerImage on Panel Backgrounds
            var allImages = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var img in allImages)
            {
                if (img == null) continue;
                string goName = img.gameObject.name.ToLower();

                if (goName.Contains("panel") || goName.Contains("background") || goName.Contains("container"))
                {
                    Undo.RecordObject(img.gameObject, "Heat UI Panel Reskin");

                    var mgrImg = img.gameObject.GetComponent<UIManagerImage>();
                    if (mgrImg == null)
                    {
                        mgrImg = Undo.AddComponent<UIManagerImage>(img.gameObject);
                    }

                    if (mgrImg != null)
                    {
                        mgrImg.UIManagerAsset = uiManager;
                        mgrImg.colorType = UIManagerImage.ColorType.Secondary;
                        EditorUtility.SetDirty(mgrImg);
                    }

                    img.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);
                    EditorUtility.SetDirty(img);
                    imageCount++;
                }
            }

            // 4. Attach PanelManager on Canvas
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                var panelMgr = canvas.gameObject.GetComponent<PanelManager>();
                if (panelMgr == null)
                {
                    panelMgr = Undo.AddComponent<PanelManager>(canvas.gameObject);
                    EditorUtility.SetDirty(panelMgr);
                }
            }

            Debug.Log($"[HeatUIIntegrationWizard] Successfully attached Heat UI Managers: {textCount} UIManagerTexts, {imageCount} UIManagerImages, {buttonCount} UIElementSounds, and PanelManager on Canvas!");
        }

        private static GameObject LoadHeatPrefab(string relativePath)
        {
            string fullPath = Path.Combine(HEAT_PREFABS_ROOT, relativePath + ".prefab").Replace('\\', '/');
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[HeatUIIntegrationWizard] Missing Heat prefab at path: {fullPath}");
            }
            return prefab;
        }
    }
}
#endif
