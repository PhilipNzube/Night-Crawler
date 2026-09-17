using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Netcode;
using Arikan;

/// <summary>
/// SOLID — SRP: Configures minimap visibility and player marker tracking exclusively for the Adventurer / Explorer role.
/// Automatically handles:
/// 1. Minimap visibility via CanvasGroup without breaking active GameObject coroutines.
/// 2. Fog elimination on the Minimap Camera so underground scenes never render a solid white/cyan fog sheet.
/// 3. Automatic spawn and binding of the Player Marker (Map Icon) above the explorer's head.
/// 4. Dynamic height tracking so the minimap camera stays at the proper distance above the player inside mine tunnels.
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

    [Header("Minimap Camera & Heading Rotation")]
    [Tooltip("If true, the minimap rotates with the player's facing direction (heading-up). If false, minimap stays fixed North-up.")]
    public bool rotateMapWithPlayer = true;

    [Tooltip("Height of the camera above the player's floor level inside mine tunnels.")]
    [Range(2f, 25f)]
    public float cameraHeightAbovePlayer = 7.0f;

    [Tooltip("Zoom distance (orthographic half-size) of the minimap camera for underground tunnels.")]
    [Range(5f, 60f)]
    public float tunnelOrthographicSize = 20.0f;

    [Header("Player Marker Settings")]
    [Tooltip("Custom texture for the player icon. If left blank, a crisp directional arrow is auto-generated.")]
    public Texture playerMarkerTexture;

    [Tooltip("Tint color of the player marker.")]
    public Color playerMarkerColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("Width of the player arrow on the minimap.")]
    [Range(0.2f, 8f)]
    public float markerWidth = 1.6f;

    [Tooltip("Length of the player arrow on the minimap.")]
    [Range(0.2f, 8f)]
    public float markerLength = 2.2f;

    [Tooltip("Rotation offset around Y in degrees. Default 90 aligns arrow pointing straight forward.")]
    [Range(-180f, 180f)]
    public float markerRotationOffset = 90f;

    [Tooltip("Height offset of the marker above the player.")]
    [Range(0.5f, 10f)]
    public float markerHeightOffset = 2.2f;

    private CanvasGroup _canvasGroup;
    private Transform _currentFollowTarget;
    private GameObject _spawnedMarkerObj;
    private GameObject _spawnedVisuals;
    private Material _markerMaterial;
    private bool _previousFogState;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Locate target UI to manage visibility via CanvasGroup
        GameObject targetUI = minimapRoot != null ? minimapRoot : (aaMinimapManager != null ? aaMinimapManager.gameObject : gameObject);
        _canvasGroup = targetUI.GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = targetUI.AddComponent<CanvasGroup>();
        }

        // Auto-detect aaMinimapManager in scene if not assigned
        if (aaMinimapManager == null)
        {
            aaMinimapManager = FindFirstObjectByType<AAMAP.MinimapManager>(FindObjectsInactive.Include);
        }

        ConfigureMinimapCamera();

        // Ensure initially hidden via canvas group so GameObject stays active to run coroutines
        SetMinimapVisibility(false);
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Temporarily disables global fog only while the Minimap Camera renders,
    /// completely preventing the all-white fog washout in underground mines.
    /// </summary>
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (aaMinimapManager != null && aaMinimapManager.minimapCamera != null)
        {
            if (cam.gameObject == aaMinimapManager.minimapCamera)
            {
                _previousFogState = RenderSettings.fog;
                RenderSettings.fog = false;
            }
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (aaMinimapManager != null && aaMinimapManager.minimapCamera != null)
        {
            if (cam.gameObject == aaMinimapManager.minimapCamera)
            {
                RenderSettings.fog = _previousFogState;
            }
        }
    }

    /// <summary>
    /// Configures the minimap camera with solid black background, close clipping, and reasonable zoom
    /// to avoid rendering distant skybox, global fog, or clipping outside the mine walls.
    /// </summary>
    public void ConfigureMinimapCamera()
    {
        if (aaMinimapManager == null) return;

        aaMinimapManager.rotateWithTarget = rotateMapWithPlayer;
        aaMinimapManager.clearFlags = AAMAP.MinimapClearFlags.SolidColor;
        aaMinimapManager.backgroundColor = Color.black;
        aaMinimapManager.nearClippingPlane = 0.1f;
        aaMinimapManager.farClippingPlane = 35f;
        aaMinimapManager.minimapRange = tunnelOrthographicSize;
        aaMinimapManager.minimapHeight = cameraHeightAbovePlayer;

        if (aaMinimapManager.minimapCamera != null)
        {
            var cam = aaMinimapManager.minimapCamera.GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 35f;
                cam.orthographicSize = tunnelOrthographicSize;

                int minimapLayer = LayerMask.NameToLayer("Minimap");
                if (minimapLayer != -1)
                {
                    cam.cullingMask |= (1 << minimapLayer);
                }
            }
        }
    }

    /// <summary>
    /// Sets minimap visibility using CanvasGroup and camera activation.
    /// NEVER deactivates the UI GameObject with SetActive(false) so coroutines and scripts remain operational.
    /// </summary>
    public void SetMinimapVisibility(bool visible)
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
        }

        if (aaMinimapManager != null)
        {
            aaMinimapManager.enabled = visible;
            if (aaMinimapManager.minimapCamera != null)
            {
                aaMinimapManager.minimapCamera.SetActive(visible);
            }
        }

        if (_spawnedMarkerObj != null)
        {
            _spawnedMarkerObj.SetActive(visible);
        }

        // Only call SetActive on minimapRoot if it is a completely separate container from this script and aaMinimapManager
        if (minimapRoot != null && minimapRoot != gameObject && (aaMinimapManager == null || minimapRoot != aaMinimapManager.gameObject))
        {
            minimapRoot.SetActive(visible);
        }
    }

    private void LateUpdate()
    {
        // Keep the minimap camera positioned above the player and update rotation
        if (_currentFollowTarget != null && aaMinimapManager != null && aaMinimapManager.minimapCamera != null)
        {
            Vector3 camPos = _currentFollowTarget.position;
            camPos.y += cameraHeightAbovePlayer;
            aaMinimapManager.minimapCamera.transform.position = camPos;

            if (rotateMapWithPlayer)
            {
                aaMinimapManager.rotateWithTarget = true;
                aaMinimapManager.minimapCamera.transform.rotation = Quaternion.Euler(90f, _currentFollowTarget.eulerAngles.y, 0f);
            }
            else
            {
                aaMinimapManager.rotateWithTarget = false;
                aaMinimapManager.minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        UpdateMarkerTransform();
    }

    private void OnValidate()
    {
        UpdateMarkerTransform();
        ConfigureMinimapCamera();
    }

    /// <summary>
    /// Updates the player marker's position, rotation, scale, and material live in real-time.
    /// Called every frame in LateUpdate and on Inspector changes in OnValidate.
    /// </summary>
    public void UpdateMarkerTransform()
    {
        if (_spawnedMarkerObj == null) return;

        _spawnedMarkerObj.transform.localPosition = new Vector3(0f, markerHeightOffset, 0f);
        _spawnedMarkerObj.transform.localRotation = Quaternion.identity;
        _spawnedMarkerObj.transform.localScale = Vector3.one;

        if (_spawnedVisuals == null)
        {
            Transform v = _spawnedMarkerObj.transform.Find("Visuals");
            if (v != null) _spawnedVisuals = v.gameObject;
        }

        if (_spawnedVisuals != null)
        {
            _spawnedVisuals.transform.localPosition = Vector3.zero;
            _spawnedVisuals.transform.localRotation = Quaternion.Euler(90f, markerRotationOffset, 0f);
            _spawnedVisuals.transform.localScale = new Vector3(markerWidth, markerLength, 1f);

            if (_markerMaterial == null)
            {
                var mr = _spawnedVisuals.GetComponent<MeshRenderer>();
                if (mr != null) _markerMaterial = mr.material;
            }

            if (_markerMaterial != null)
            {
                if (_markerMaterial.HasProperty("_BaseColor")) _markerMaterial.SetColor("_BaseColor", playerMarkerColor);
                if (_markerMaterial.HasProperty("_Color")) _markerMaterial.SetColor("_Color", playerMarkerColor);

                Texture iconTex = playerMarkerTexture != null ? playerMarkerTexture : CreateDefaultArrowTexture();
                if (_markerMaterial.HasProperty("_BaseMap")) _markerMaterial.SetTexture("_BaseMap", iconTex);
                if (_markerMaterial.HasProperty("_MainTex")) _markerMaterial.SetTexture("_MainTex", iconTex);
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
                var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
                Instance.BindToPlayer(playerObj);
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
                Instance.BindToPlayer(possessedTarget);
            }
            else
            {
                Instance.SetMinimapVisibility(false);
            }
        }
        else
        {
            Instance.SetMinimapVisibility(false);
        }
    }

    private void Start()
    {
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

        // Give a short grace period for network traits & InvestigatorAbilities to fully initialize
        yield return new WaitForSeconds(0.3f);

        bool isAdventurer = CheckIfAdventurer(localPlayerObj);

        if (isAdventurer)
        {
            Debug.Log($"[AdventurerMinimapSetup] Local player '{localPlayerObj.name}' is Adventurer/Explorer. Activating Minimap!");
            SetMinimapVisibility(true);
            BindToPlayer(localPlayerObj);
        }
        else
        {
            Debug.Log($"[AdventurerMinimapSetup] Local player '{localPlayerObj.name}' is not Adventurer. Minimap hidden.");
            SetMinimapVisibility(false);
        }
    }

    private void BindToPlayer(GameObject playerObj)
    {
        _currentFollowTarget = playerObj.transform;

        if (miniMapView != null)
        {
            miniMapView.FollowCentered(_currentFollowTarget);
        }

        if (aaMinimapManager != null)
        {
            aaMinimapManager.SetTargetObject(playerObj);
        }

        SetupPlayerMarker(playerObj);
        ConfigureMinimapCamera();
    }

    /// <summary>
    /// Creates or configures the Map Icon marker right above the player's head,
    /// using an unlit transparent material that is unaffected by cave darkness or shadows.
    /// </summary>
    private void SetupPlayerMarker(GameObject playerObj)
    {
        if (playerObj == null) return;

        int minimapLayer = LayerMask.NameToLayer("Minimap");

        // Check if player already has a Map Icon child
        Transform existingMarker = playerObj.transform.Find("Map Icon");
        if (existingMarker != null)
        {
            _spawnedMarkerObj = existingMarker.gameObject;

            // Remove any legacy MapIcon script that might hijack world rotation
            var oldMapIcon = _spawnedMarkerObj.GetComponent<AAMAP.MapIcon>();
            if (oldMapIcon != null) Destroy(oldMapIcon);

            Transform v = _spawnedMarkerObj.transform.Find("Visuals");
            if (v != null) _spawnedVisuals = v.gameObject;

            if (minimapLayer != -1)
            {
                _spawnedMarkerObj.layer = minimapLayer;
                foreach (Transform child in _spawnedMarkerObj.transform)
                {
                    child.gameObject.layer = minimapLayer;
                }
            }
            _spawnedMarkerObj.SetActive(true);
            UpdateMarkerTransform();
            ExcludeMinimapLayerFromPlayerCameras(playerObj, minimapLayer);
            return;
        }

        // Create marker root
        _spawnedMarkerObj = new GameObject("Map Icon");
        _spawnedMarkerObj.transform.SetParent(playerObj.transform, false);
        _spawnedMarkerObj.transform.localPosition = new Vector3(0f, markerHeightOffset, 0f);
        _spawnedMarkerObj.transform.localRotation = Quaternion.identity;
        _spawnedMarkerObj.transform.localScale = Vector3.one;
        if (minimapLayer != -1)
        {
            _spawnedMarkerObj.layer = minimapLayer;
        }

        // Create Quad visual laying flat facing up towards camera (+Y), with texture pointing forward (+Z)
        _spawnedVisuals = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _spawnedVisuals.name = "Visuals";
        _spawnedVisuals.transform.SetParent(_spawnedMarkerObj.transform, false);
        _spawnedVisuals.transform.localPosition = Vector3.zero;
        _spawnedVisuals.transform.localRotation = Quaternion.Euler(90f, markerRotationOffset, 0f);
        _spawnedVisuals.transform.localScale = new Vector3(markerWidth, markerLength, 1f);
        if (minimapLayer != -1)
        {
            _spawnedVisuals.layer = minimapLayer;
        }

        // Remove collider
        var collider = _spawnedVisuals.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        // Prepare texture
        bool isCustomTexture = playerMarkerTexture != null;
        Texture iconTex = isCustomTexture ? playerMarkerTexture : CreateDefaultArrowTexture();
        Color tint = playerMarkerColor;

        // Create transparent unlit material (eliminates solid rectangle background around arrow)
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");

        _markerMaterial = new Material(shader);
        if (_markerMaterial.HasProperty("_Surface"))
        {
            _markerMaterial.SetFloat("_Surface", 1f); // 1 = Transparent
            _markerMaterial.SetFloat("_Blend", 0f);   // 0 = Alpha
            _markerMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _markerMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _markerMaterial.SetInt("_ZWrite", 0);
            _markerMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _markerMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        if (_markerMaterial.HasProperty("_BaseMap")) _markerMaterial.SetTexture("_BaseMap", iconTex);
        if (_markerMaterial.HasProperty("_MainTex")) _markerMaterial.SetTexture("_MainTex", iconTex);
        if (_markerMaterial.HasProperty("_BaseColor")) _markerMaterial.SetColor("_BaseColor", tint);
        if (_markerMaterial.HasProperty("_Color")) _markerMaterial.SetColor("_Color", tint);

        var mr = _spawnedVisuals.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.material = _markerMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // Apply any real-time adjustments
        UpdateMarkerTransform();

        // Ensure main cameras never render the floating marker in the 3D scene
        ExcludeMinimapLayerFromPlayerCameras(playerObj, minimapLayer);

        string layerName = minimapLayer != -1 ? "Minimap" : "Default";
        Debug.Log($"[AdventurerMinimapSetup] Player Map Icon created on '{playerObj.name}' at offset {markerHeightOffset} on layer {layerName}");
    }

    /// <summary>
    /// Strips the Minimap layer from Camera.main and player cameras so the 3D floating arrow
    /// is invisible to the player's eyes and only visible on the top-down minimap camera.
    /// </summary>
    private void ExcludeMinimapLayerFromPlayerCameras(GameObject playerObj, int minimapLayer)
    {
        if (minimapLayer == -1) return;

        int maskToRemove = 1 << minimapLayer;

        if (Camera.main != null && (aaMinimapManager == null || Camera.main.gameObject != aaMinimapManager.minimapCamera))
        {
            Camera.main.cullingMask &= ~maskToRemove;
        }

        if (playerObj != null)
        {
            Camera[] cams = playerObj.GetComponentsInChildren<Camera>(true);
            foreach (var c in cams)
            {
                if (aaMinimapManager == null || c.gameObject != aaMinimapManager.minimapCamera)
                {
                    c.cullingMask &= ~maskToRemove;
                }
            }
        }
    }

    /// <summary>
    /// Generates a crisp directional arrow texture (pointing forward) with a sleek outline.
    /// Works independently with 0 external dependencies.
    /// </summary>
    private Texture2D CreateDefaultArrowTexture()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color transparent = new Color(0, 0, 0, 0);
        Color whiteOutline = Color.white;
        Color fill = playerMarkerColor;

        // Clear texture
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, transparent);
            }
        }

        // Draw a clean chevron / triangle pointing towards +Y (forward)
        Vector2 tip = new Vector2(size * 0.5f, size * 0.85f);
        Vector2 left = new Vector2(size * 0.2f, size * 0.15f);
        Vector2 right = new Vector2(size * 0.8f, size * 0.15f);
        Vector2 center = new Vector2(size * 0.5f, size * 0.35f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                if (IsInsideTriangle(p, tip, left, center) || IsInsideTriangle(p, tip, center, right))
                {
                    // Check if near edge for outline
                    float distEdge = MinDistanceToEdges(p, tip, left, center, right);
                    if (distEdge <= 2.5f)
                    {
                        tex.SetPixel(x, y, whiteOutline);
                    }
                    else
                    {
                        tex.SetPixel(x, y, fill);
                    }
                }
            }
        }

        tex.Apply();
        return tex;
    }

    private static bool IsInsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static float MinDistanceToEdges(Vector2 p, Vector2 tip, Vector2 left, Vector2 center, Vector2 right)
    {
        float d1 = DistToSegment(p, tip, left);
        float d2 = DistToSegment(p, tip, right);
        float d3 = DistToSegment(p, left, center);
        float d4 = DistToSegment(p, right, center);
        return Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
    }

    private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab);
        t = Mathf.Clamp01(t);
        Vector2 closest = a + t * ab;
        return Vector2.Distance(p, closest);
    }

    private bool CheckIfAdventurer(GameObject playerObj)
    {
        if (playerObj == null) return false;

        // 1. Direct InvestigatorAbilities profession check
        var abilities = playerObj.GetComponent<InvestigatorAbilities>();
        if (abilities != null && abilities.profession == InvestigatorProfession.Explorer)
        {
            return true;
        }

        // 2. Name check fallback
        string pName = playerObj.name.ToLower();
        if (pName.Contains("adventure") || pName.Contains("explorer")) return true;

        // 3. CharacterSelectManager fallback
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
