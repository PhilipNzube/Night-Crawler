using Unity.Netcode;
using UnityEngine;
using System.Text;

public class ScreenLogger : MonoBehaviour
{
    private StringBuilder logHistory = new StringBuilder();
    private Vector2 scrollPosition;

    void Awake() { DontDestroyOnLoad(gameObject); }
    void OnEnable() { Application.logMessageReceived += CaptureLog; }
    void OnDisable() { Application.logMessageReceived -= CaptureLog; }

    void CaptureLog(string condition, string stackTrace, LogType type)
    {
        logHistory.AppendLine($"[{System.DateTime.Now:HH:mm:ss}] {condition}");
        scrollPosition.y = float.MaxValue;
    }

    void OnGUI()
    {
        // Moved to center-right to stay clear of other UI
        GUI.Box(new Rect(Screen.width - 360, 270, 350, 200), "System Console");
        GUILayout.BeginArea(new Rect(Screen.width - 350, 300, 330, 150));

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.Label(logHistory.ToString());
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }
}