using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Root HUD manager.
///
/// Deliberately avoids relying on serialized field wiring for core health bar
/// references, because wiring can be lost when the canvas is rebuilt or the
/// scene is reloaded without saving. Instead:
///
///   Fill images    — located at runtime by walking the canvas hierarchy
///                    (supports both current BarArea/Fill and legacy direct Fill paths)
///   HealthSystems  — found by FindObjectsByType filtered by tag/component type
///   CooldownUI     — GetComponentInChildren on this canvas root
///   GameOverScreen — GetComponentInChildren on this canvas root
///
/// Polling in Update() is the primary mechanism for health bar updates; event
/// subscription is kept as a fast-path supplement.
///
/// A DIAGNOSTICS log fires at Start() — check the Console for "[HUDController]
/// DIAGNOSTICS" to see the exact state of every reference. Any NULL entry means
/// that object is missing from the scene and the setup steps have not been run.
/// </summary>
public class HUDController : MonoBehaviour
{
    // Serialized refs — used if already wired by SetupHUD; overridden at runtime
    // by ResolveAllRefs() if null. Keeping [SerializeField] lets the Inspector show
    // what was found after runtime resolution.
    [Header("Health Bars (auto-resolved if null)")]
    [SerializeField] private Image _playerHealthFill;
    [SerializeField] private Image _bossHealthFill;

    [Header("UI Components (auto-resolved if null)")]
    [SerializeField] private CooldownUI     _cooldownUI;
    [SerializeField] private GameOverScreen _gameOverScreen;

    private HealthSystem   _playerHealthSystem;
    private HealthSystem   _bossHealthSystem;
    private BossController _bossController;
    private PlayerSkills   _playerSkills;

    // Polling baselines — initialised to -1 so the first poll always syncs the bar.
    private float _lastPlayerHealth = -1f;
    private float _lastBossHealth   = -1f;

    // Retry throttle — re-attempts component discovery once per second.
    private float _nextRetryTime;

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Start()
    {
        ResolveAllRefs();
        SubscribeToEvents();
        SetInitialHealthBars();

        if (_playerSkills != null && _cooldownUI != null)
            _cooldownUI.Initialize(_playerSkills.AllSkills);

        LogDiagnostics(); // Always fires — check Console to confirm setup is correct.
    }

    private void Update()
    {
        // Retry until all health systems AND PlayerSkills are found.
        if ((_playerHealthSystem == null || _bossHealthSystem == null || _playerSkills == null)
            && Time.time >= _nextRetryTime)
        {
            RetryMissingComponents();
            _nextRetryTime = Time.time + 1f;
        }

        PollHealthBars();
    }

    private void OnDestroy()
    {
        if (_playerHealthSystem != null)
        {
            _playerHealthSystem.OnHealthChanged -= OnPlayerHealthChanged;
            _playerHealthSystem.OnDied          -= OnPlayerDied;
        }

        if (_bossHealthSystem != null)
            _bossHealthSystem.OnHealthChanged -= OnBossHealthChanged;

        if (_bossController != null)
            _bossController.OnBossDied -= OnBossDefeated;
    }

    // ------------------------------------------------------------------
    // Reference resolution — does not require SetupHUD to have been run
    // ------------------------------------------------------------------

    private void ResolveAllRefs()
    {
        // Fill images: walk the canvas hierarchy this script is attached to.
        // Health bars use the RectTransform-anchor approach: anchorMax.x is driven
        // by health ratio. This works for any Image type (Simple or Filled) and is
        // immune to fillAmount being overridden by the Image type configuration.
        if (_playerHealthFill == null)
            _playerHealthFill = FindFillInBar("PlayerHealthBar");

        if (_bossHealthFill == null)
            _bossHealthFill = FindFillInBar("BossHealthBar");

        // CooldownUI and GameOverScreen are siblings on this canvas root.
        if (_cooldownUI == null)
            _cooldownUI = GetComponentInChildren<CooldownUI>(includeInactive: true);

        if (_gameOverScreen == null)
            _gameOverScreen = GetComponentInChildren<GameOverScreen>(includeInactive: true);

        // Health systems and controllers: scene-wide search filtered by tag/type.
        ResolvePlayerComponents();
        ResolveBossComponents();
    }

    /// <summary>
    /// Supports current layout (BarArea/Fill) and legacy layout (Fill as direct child).
    /// No Image type configuration needed — bar width is driven via anchorMax.x.
    /// </summary>
    private Image FindFillInBar(string barGroupName)
    {
        var newLayout = transform.Find($"{barGroupName}/BarArea/Fill");
        if (newLayout != null) return newLayout.GetComponent<Image>();

        var legacyLayout = transform.Find($"{barGroupName}/Fill");
        return legacyLayout != null ? legacyLayout.GetComponent<Image>() : null;
    }

    private void ResolvePlayerComponents()
    {
        var playerGo = GameObject.FindWithTag("Player");

        if (playerGo == null)
        {
            var skills = Object.FindAnyObjectByType<PlayerSkills>();
            if (skills != null) playerGo = skills.gameObject;
        }

        if (playerGo == null) return;

        _playerHealthSystem = playerGo.GetComponent<HealthSystem>()
                           ?? playerGo.GetComponentInChildren<HealthSystem>();

        _playerSkills = playerGo.GetComponent<PlayerSkills>()
                     ?? playerGo.GetComponentInChildren<PlayerSkills>();

        // Last resort: find any HealthSystem in the scene that is not tagged Boss.
        if (_playerHealthSystem == null)
        {
            foreach (var hs in Object.FindObjectsByType<HealthSystem>(FindObjectsSortMode.None))
            {
                if (!hs.CompareTag("Boss"))
                {
                    _playerHealthSystem = hs;
                    break;
                }
            }
        }
    }

    private void ResolveBossComponents()
    {
        var bossGo = GameObject.FindWithTag("Boss");

        if (bossGo == null)
        {
            var bc = Object.FindAnyObjectByType<BossController>();
            if (bc != null) bossGo = bc.gameObject;
        }

        if (bossGo == null) return;

        _bossHealthSystem = bossGo.GetComponent<HealthSystem>()
                         ?? bossGo.GetComponentInChildren<HealthSystem>();

        _bossController = bossGo.GetComponent<BossController>()
                       ?? bossGo.GetComponentInChildren<BossController>();
    }

    // ------------------------------------------------------------------
    // Periodic retry — handles actors spawned or added after Start()
    // ------------------------------------------------------------------

    private void RetryMissingComponents()
    {
        bool playerHsWasNull         = _playerHealthSystem == null;
        bool bossHsWasNull           = _bossHealthSystem   == null;
        bool bossControllerWasNull   = _bossController     == null;

        if (_playerHealthSystem == null || _playerSkills == null)
            ResolvePlayerComponents();

        if (_bossHealthSystem == null || _bossController == null)
            ResolveBossComponents();

        // Subscribe only newly-found components to avoid double-subscription.
        if (playerHsWasNull && _playerHealthSystem != null)
        {
            _playerHealthSystem.OnHealthChanged += OnPlayerHealthChanged;
            _playerHealthSystem.OnDied          += OnPlayerDied;
            _lastPlayerHealth = -1f;
        }

        if (bossHsWasNull && _bossHealthSystem != null)
        {
            _bossHealthSystem.OnHealthChanged += OnBossHealthChanged;
            _lastBossHealth = -1f;
        }

        // Use the controller-specific flag — not bossHsWasNull — so a late-found
        // BossController always gets subscribed even if HealthSystem was found earlier.
        if (bossControllerWasNull && _bossController != null)
            _bossController.OnBossDied += OnBossDefeated;

        if (_playerSkills != null && _cooldownUI != null)
            _cooldownUI.Initialize(_playerSkills.AllSkills); // No-op after first call (guarded in CooldownUI).
    }

    // ------------------------------------------------------------------
    // Event subscription / initial sync
    // ------------------------------------------------------------------

    private void SubscribeToEvents()
    {
        if (_playerHealthSystem != null)
        {
            _playerHealthSystem.OnHealthChanged += OnPlayerHealthChanged;
            _playerHealthSystem.OnDied          += OnPlayerDied;
        }

        if (_bossHealthSystem != null)
            _bossHealthSystem.OnHealthChanged += OnBossHealthChanged;

        if (_bossController != null)
            _bossController.OnBossDied += OnBossDefeated;
    }

    private void SetInitialHealthBars()
    {
        if (_playerHealthSystem != null)
        {
            SetHealthFill(_playerHealthFill,
                _playerHealthSystem.CurrentHealth, _playerHealthSystem.MaxHealth);
            _lastPlayerHealth = _playerHealthSystem.CurrentHealth;
        }

        if (_bossHealthSystem != null)
        {
            SetHealthFill(_bossHealthFill,
                _bossHealthSystem.CurrentHealth, _bossHealthSystem.MaxHealth);
            _lastBossHealth = _bossHealthSystem.CurrentHealth;
        }
    }

    // ------------------------------------------------------------------
    // Polling — guaranteed health bar sync every frame
    // ------------------------------------------------------------------

    private void PollHealthBars()
    {
        if (_playerHealthSystem != null)
        {
            float hp = _playerHealthSystem.CurrentHealth;
            if (!Mathf.Approximately(hp, _lastPlayerHealth))
            {
                SetHealthFill(_playerHealthFill, hp, _playerHealthSystem.MaxHealth);
                _lastPlayerHealth = hp;
            }
        }

        if (_bossHealthSystem != null)
        {
            float hp = _bossHealthSystem.CurrentHealth;
            if (!Mathf.Approximately(hp, _lastBossHealth))
            {
                SetHealthFill(_bossHealthFill, hp, _bossHealthSystem.MaxHealth);
                _lastBossHealth = hp;
            }
        }
    }

    // ------------------------------------------------------------------
    // Event handlers
    // ------------------------------------------------------------------

    private void OnPlayerHealthChanged(float current, float max)
    {
        SetHealthFill(_playerHealthFill, current, max);
        _lastPlayerHealth = current;
    }

    private void OnBossHealthChanged(float current, float max)
    {
        SetHealthFill(_bossHealthFill, current, max);
        _lastBossHealth = current;
    }

    private void OnPlayerDied()
    {
        if (_gameOverScreen == null)
        {
            Debug.LogError("[HUDController] GameOverScreen is null — death screen cannot show. " +
                "Run CyberBoss/Setup HUD and Ctrl+S.");
            return;
        }
        _gameOverScreen.ShowDeath();
    }

    private void OnBossDefeated()
    {
        if (_gameOverScreen == null)
        {
            Debug.LogError("[HUDController] GameOverScreen is null — victory screen cannot show. " +
                "Run CyberBoss/Setup HUD and Ctrl+S.");
            return;
        }
        _gameOverScreen.ShowVictory();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Drives the health bar by setting anchorMax.x on the Fill RectTransform.
    /// This physically resizes the Fill rectangle and works regardless of Image
    /// type, sprite configuration, or fillAmount state.
    ///
    ///   anchorMax.x = 1.0  → bar is 100% full (Fill covers all of BarArea)
    ///   anchorMax.x = 0.3  → bar is 30% full  (Fill covers left 30% of BarArea)
    ///   anchorMax.x = 0.0  → bar is empty     (Fill has zero width)
    /// </summary>
    private static void SetHealthFill(Image fill, float current, float max)
    {
        if (fill == null) return;
        float ratio     = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        var   rt        = fill.rectTransform;
        var   anchorMax = rt.anchorMax;
        anchorMax.x     = ratio;
        rt.anchorMax    = anchorMax;
    }

    /// <summary>
    /// Prints the full state of every resolved reference at Start().
    /// Any "NULL" entry identifies exactly what is missing from the scene.
    /// </summary>
    private void LogDiagnostics()
    {
        string Fmt(Object obj) => obj == null ? "NULL" : $"OK ({obj.name})";

        string FmtFill(Image img)
        {
            if (img == null) return "NULL";
            var rt = img.rectTransform;
            return $"OK ({img.name})  anchorMin={rt.anchorMin}  anchorMax={rt.anchorMax}" +
                   $"  offsetMin={rt.offsetMin}  offsetMax={rt.offsetMax}  color={img.color}";
        }

        string playerHs = _playerHealthSystem == null ? "NULL"
            : $"OK ({_playerHealthSystem.name})  hp={_playerHealthSystem.CurrentHealth}/{_playerHealthSystem.MaxHealth}";
        string bossHs = _bossHealthSystem == null ? "NULL"
            : $"OK ({_bossHealthSystem.name})  hp={_bossHealthSystem.CurrentHealth}/{_bossHealthSystem.MaxHealth}";

        Debug.Log(
            $"[HUDController] DIAGNOSTICS\n" +
            $"  _playerHealthFill  : {FmtFill(_playerHealthFill)}\n" +
            $"  _bossHealthFill    : {FmtFill(_bossHealthFill)}\n" +
            $"  _playerHealthSystem: {playerHs}\n" +
            $"  _bossHealthSystem  : {bossHs}\n" +
            $"  _playerSkills      : {Fmt(_playerSkills)}\n" +
            $"  _cooldownUI        : {Fmt(_cooldownUI)}\n" +
            $"  _gameOverScreen    : {Fmt(_gameOverScreen)}\n" +
            "  Health bars driven via anchorMax.x (0=empty, 1=full) — immune to Image type.\n" +
            "  Anything NULL = run Setup Boss + Setup Player Combat + Setup HUD, then Ctrl+S.");
    }
}
