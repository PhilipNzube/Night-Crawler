using UnityEngine;
using TMPro;

public class MatchTimerUI : MonoBehaviour
{
    private TextMeshProUGUI _timerText;

    void Awake()
    {
        _timerText = GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        if (GameManager.Instance == null || _timerText == null) return;

        float timeRemaining = GameManager.Instance.matchTimer.Value;
        UpdateTimerDisplay(timeRemaining);
    }

    private void UpdateTimerDisplay(float time)
    {
        if (time < 0) time = 0;

        int minutes = Mathf.FloorToInt(time / 60);
        int seconds = Mathf.FloorToInt(time % 60);

        _timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        // Turn red when time is low (optional)
        if (time < 60f) _timerText.color = Color.red;
        else _timerText.color = Color.white;
    }
}
