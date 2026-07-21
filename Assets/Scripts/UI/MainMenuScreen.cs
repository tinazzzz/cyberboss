using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Full-screen opening / pause menu.
///
/// Styled to match GameOverScreen (same full-screen dark backdrop, same
/// bold-title/subtitle/hint text layout) so the death/victory/start/pause
/// screens read as one consistent visual family.
///
/// Unlike GameOverScreen this panel is reopenable: it shows automatically at
/// the true start of a play session, and after the player dismisses it with
/// Enter, pressing M at any time reopens it as a pause menu (Enter resumes).
/// Because of that, the GameObject itself is never deactivated — a disabled
/// GameObject stops receiving Update() calls, which would make the M key
/// listener dead after the first dismissal. Visibility is instead toggled
/// via CanvasGroup (alpha/interactable/blocksRaycasts), so this script keeps
/// polling regardless of shown/hidden state.
///
/// s_hasStartedThisSession is deliberately static, not an instance field:
/// GameOverScreen.Restart() reloads the scene, which destroys and recreates
/// every GameObject (instance fields reset to their defaults), but a normal
/// scene reload does not reload the C# domain, so static fields survive it.
/// That is what lets a death/victory restart drop the player straight back
/// into the fight instead of re-showing the full instructions screen on
/// every single restart — the opening screen is a one-time-per-session
/// thing, not a one-time-per-scene-load thing. It resets naturally the next
/// time Play Mode actually starts (domain reload on Editor stop/play, or a
/// fresh process in a build).
///
/// Time.timeScale is frozen at 0 whenever the panel is visible (opening
/// screen or paused), exactly like GameOverScreen's pause behavior, so the
/// boss's skill-selection coroutine and player timers do not advance while
/// the menu is up. The New Input System polls at the system clock rate — not
/// game time — so wasPressedThisFrame remains responsive at timeScale = 0.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class MainMenuScreen : MonoBehaviour
{
    [SerializeField] private TMP_Text       _titleText;
    [SerializeField] private TMP_Text       _subtitleText;
    [SerializeField] private TMP_Text       _controlKeysText;
    [SerializeField] private TMP_Text       _controlActionsText;
    [SerializeField] private TMP_Text       _hintText;
    [SerializeField] private HUDConfig      _config;
    [SerializeField] private BossController _bossController;
    [SerializeField] private GameOverScreen _gameOverScreen;

    private static bool s_hasStartedThisSession;

    private CanvasGroup _canvasGroup;
    private bool _isPaused; // true whenever this panel currently owns the Time.timeScale = 0 freeze

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();

        if (_config == null)
        {
            Debug.LogError("[MainMenuScreen] HUDConfig is not assigned. " +
                "Run CyberBoss/Setup Main Menu and Ctrl+S.");
            return;
        }

        if (_titleText != null)    { _titleText.text = _config.MenuTitleText; _titleText.color = _config.MenuTitleColor; }
        if (_subtitleText != null) { _subtitleText.text = _config.MenuSubtitleText; _subtitleText.color = _config.MenuSubtitleColor; }
        if (_controlKeysText != null)    { _controlKeysText.text = _config.MenuControlKeysText; _controlKeysText.color = _config.MenuControlKeyColor; }
        if (_controlActionsText != null)  _controlActionsText.text = _config.MenuControlActionsText;
        if (_hintText != null)            _hintText.text = _config.MenuStartHintText;

        if (s_hasStartedThisSession)
        {
            // A death/victory restart reload, not the true first start of this
            // play session — drop straight back into the fight. GameOverScreen
            // .Restart() already called NotifyFightStart() itself before
            // reloading, so there's nothing left to do here.
            _isPaused      = false;
            Time.timeScale = 1f;
            SetVisible(false);
            return;
        }

        _isPaused = true;
        Time.timeScale = 0f;
        SetVisible(true);
    }

    private void Update()
    {
        bool enterPressed = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
        bool mPressed     = Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame;

        if (_isPaused)
        {
            if (enterPressed) Resume();
            return;
        }

        // Don't let M reopen the menu over the death/victory screen — that
        // screen owns its own Time.timeScale = 0 freeze and expects T to
        // reload, not Enter to resume a fight that has already ended.
        if (mPressed && !IsGameOverShown()) Pause();
    }

    private void OnDestroy()
    {
        // Time.timeScale survives scene loads — always restore if this object is
        // destroyed while frozen (e.g. external reload, editor stop) so play isn't stuck paused.
        if (_isPaused) Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------
    // Private
    // ------------------------------------------------------------------

    private bool IsGameOverShown() => _gameOverScreen != null && _gameOverScreen.gameObject.activeSelf;

    private void Pause()
    {
        _isPaused      = true;
        Time.timeScale = 0f;
        if (_hintText != null && _config != null) _hintText.text = _config.MenuResumeHintText;
        SetVisible(true);
    }

    private void Resume()
    {
        _isPaused      = false;
        Time.timeScale = 1f;
        SetVisible(false);

        if (!s_hasStartedThisSession)
        {
            s_hasStartedThisSession = true;

            // Marks a clean episode start for the RL policy at the moment gameplay
            // actually begins, rather than at scene load while still frozen behind the menu.
            if (_bossController != null)
                _bossController.NotifyFightStart();
        }
    }

    private void SetVisible(bool visible)
    {
        if (_canvasGroup == null) return;
        _canvasGroup.alpha          = visible ? 1f : 0f;
        _canvasGroup.interactable   = visible;
        _canvasGroup.blocksRaycasts = visible;
    }
}
