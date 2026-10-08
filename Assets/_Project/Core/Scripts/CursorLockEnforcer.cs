using UnityEngine;

/// <summary>
/// Global system enforcer that ensures whenever the mouse cursor is locked in the application
/// (CursorLockMode.Locked), Cursor.visible is ALWAYS explicitly false.
///
/// Automatically instantiates at engine startup before any scene loads and persists permanently.
/// Covers all UI panel closings, pause menu resumes, scene changes, and window focus transitions.
/// </summary>
public class CursorLockEnforcer : MonoBehaviour
{
    private static CursorLockEnforcer _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_instance != null) return;

        var go = new GameObject("[GlobalCursorLockEnforcer]");
        _instance = go.AddComponent<CursorLockEnforcer>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        EnforceCursorInvisibility();
    }

    private void LateUpdate()
    {
        EnforceCursorInvisibility();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            EnforceCursorInvisibility();
        }
    }

    private void EnforceCursorInvisibility()
    {
        if (Cursor.lockState == CursorLockMode.Locked && Cursor.visible)
        {
            Cursor.visible = false;
        }
    }
}
