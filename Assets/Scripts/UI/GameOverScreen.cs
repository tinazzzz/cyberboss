using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Full-screen death or victory overlay.
///
/// The panel's GameObject starts inactive in the scene (SetupHUD.cs sets it inactive).
/// HUDController calls ShowDeath() or ShowVictory() to activate it.
///
/// TMP serialized field note: Unity deserializes all [SerializeField] values for every
/// object in the scene at load time, including inactive GameObjects. _messageText,
/// _subtitleText, _hintText, and _config are valid references before Awake() runs.
/// Show() can therefore safely write to TMP text fields before calling SetActive(true).
///
/// Time.timeScale note: Time.timeScale is set to 0 when shown and restored to 1 before
/// scene reload. The New Input System polls at the system clock rate — not game time —
/// so Keyboard.current.tKey.wasPressedThisFrame remains responsive at timeScale = 0.
/// </summary>
public class GameOverScreen : MonoBehaviour
{
    [SerializeField] private TMP_Text      _messageText;
    [SerializeField] private TMP_Text      _subtitleText;
    [SerializeField] private TMP_Text      _hintText;
    [SerializeField] private HUDConfig     _config;
    [SerializeField] private BossController _bossController;

    private bool _isShown;

    // ------------------------------------------------------------------
    // Public API — called by HUDController
    // ------------------------------------------------------------------

    /// <summary>Show the death panel ("YOU DIED") and pause the game.</summary>
    public void ShowDeath() =>
        Show(_config.DeathMessage, _config.DeathMessageColor, _config.SubtitleDeathText);

    /// <summary>Show the victory panel ("BOSS DEFEATED") and pause the game.</summary>
    public void ShowVictory() =>
        Show(_config.VictoryMessage, _config.VictoryMessageColor, _config.SubtitleVictoryText);

    private void OnDestroy()
    {
        // Time.timeScale survives scene loads — always restore if this object is destroyed
        // while frozen (e.g. external reload, editor stop) so the next scene is not frozen.
        if (_isShown) Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Update()
    {
        if (!_isShown) return;
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            Restart();
    }

    // ------------------------------------------------------------------
    // Private
    // ------------------------------------------------------------------

    private void Show(string message, Color messageColor, string subtitle)
    {
        if (_isShown) return; // Guard: first caller wins (e.g. simultaneous player + boss death).

        if (_config == null)
        {
            Debug.LogError("[GameOverScreen] HUDConfig is not assigned. " +
                "Run CyberBoss/Setup HUD and Ctrl+S to wire it.");
            return;
        }

        if (_messageText != null)  { _messageText.text = message; _messageText.color = messageColor; }
        if (_subtitleText != null)   _subtitleText.text = subtitle;
        if (_hintText != null)       _hintText.text     = _config.RestartHintText;

        gameObject.SetActive(true);
        _isShown       = true;
        Time.timeScale = 0f;
    }

    private void Restart()
    {
        // Clear behavior tracker accumulators before reload so the new episode
        // starts from a clean observation vector, not one contaminated by this fight.
        if (_bossController != null)
            _bossController.NotifyFightStart();

        _isShown       = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
