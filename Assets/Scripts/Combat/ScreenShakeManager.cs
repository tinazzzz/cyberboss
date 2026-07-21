using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Singleton manager for Cinemachine Impulse-based screen shake.
///
/// Two kinds of shake source:
///   1. Generic, non-skill-specific reactions — still driven by subscriptions here:
///        Light  — boss HealthSystem.OnDamageTaken (any player hit landing on the boss)
///        Medium — player HealthSystem.OnDied (player death)
///        BossDeath — BossController.OnBossDied
///   2. Per-skill signatures — data-driven. Each of the 10 combat
///      skills (5 player, 5 boss) carries its own ImpactEffectProfile on its Config
///      asset and calls Shake(profile) directly from its own ISkill implementation
///      (mirroring the pre-existing BossChargeSkill.DealContactDamage() pattern),
///      rather than this manager guessing which skill fired from a shared event.
///      This also structurally avoids the double-fire hazard Ranged Blast would
///      otherwise hit (PlayerSkills.OnSkillExecuted fires at launch; Ranged Blast's
///      profile is tuned for the hit-confirm moment) — there is no generic
///      OnSkillExecuted-based shake dispatch left for this to collide with.
///
/// The CinemachineImpulseListener on CM_IsometricCamera uses Additive mode, so a
/// per-skill impulse naturally stacks with a concurrent generic Light shake from
/// the boss HealthSystem event.
///
/// Setup: Run CyberBoss/Setup Polish — adds this component, a
/// CinemachineImpulseSource on the same GameObject, and wires the config.
///
/// Usage example (generic reaction, subscribed internally):
/// <code>
///   ScreenShakeManager.Instance?.TriggerLightShake();
/// </code>
/// Usage example (per-skill signature, called directly from an ISkill):
/// <code>
///   ScreenShakeManager.Instance?.Shake(_config.ImpactProfile);
/// </code>
/// </summary>
public class ScreenShakeManager : MonoBehaviour
{
    /// <summary>
    /// Scene-scoped singleton. Null-safe — use ?.TriggerXxx()/?.Shake() from skill
    /// code so a missing manager never causes a NullReferenceException.
    /// </summary>
    public static ScreenShakeManager Instance { get; private set; }

    [SerializeField] private ScreenShakeConfig _config;

    private CinemachineImpulseSource _impulseSource;

    // References retained for unsubscription in OnDestroy.
    private HealthSystem   _playerHealthSystem;
    private HealthSystem   _bossHealthSystem;
    private BossController _bossController;

    // Pre-allocated delegates — same instance used for subscribe and unsubscribe
    // so no heap allocation on cleanup and no dangling subscriptions on destroy.
    private System.Action        _onPlayerDiedDelegate;
    private System.Action<float> _onBossDamageTakenDelegate;
    private System.Action        _onBossDiedDelegate;

    // ------------------------------------------------------------------
    // Public API — generic reactions
    // ------------------------------------------------------------------

    /// <summary>
    /// Light shake: baseline whenever the boss HealthSystem records a damage event
    /// (any player attack — normal attack or skill — landing on the boss).
    /// </summary>
    public void TriggerLightShake() =>
        GenerateShake(_config.LightImpulseForce, _config.LightDuration, snap: true);

    /// <summary>Medium shake: player death.</summary>
    public void TriggerMediumShake() =>
        GenerateShake(_config.MediumImpulseForce, _config.MediumDuration, snap: true);

    /// <summary>Maximum-force slow-decay rumble fired on boss death.</summary>
    public void TriggerBossDeathShake() =>
        GenerateShake(_config.BossDeathImpulseForce, _config.BossDeathDuration, snap: false);

    // ------------------------------------------------------------------
    // Public API — per-skill signature
    // ------------------------------------------------------------------

    /// <summary>
    /// Fires a shake using the given skill's ImpactEffectProfile. No-op if the
    /// profile is null or has shake disabled (EnableShake = false) — this is the
    /// normal, expected case for skills like Dash and Boss Shield Phase that never
    /// shake the camera. ShakeIsSnap selects a short high-frequency "jolt" (Bump)
    /// vs. a longer sustained "roll" (Explosion, e.g. AoE Slam).
    /// </summary>
    public void Shake(ImpactEffectProfile profile)
    {
        if (profile == null || !profile.EnableShake) return;
        GenerateShake(profile.ShakeForce, profile.ShakeDuration, profile.ShakeIsSnap);
    }

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _impulseSource = GetComponent<CinemachineImpulseSource>();
        if (_impulseSource == null)
            Debug.LogError("[ScreenShakeManager] CinemachineImpulseSource missing on " +
                $"'{name}'. Run CyberBoss/Setup Polish to add it.");
    }

    private void Start()
    {
        if (_config == null)
        {
            Debug.LogError("[ScreenShakeManager] ScreenShakeConfig not assigned on " +
                $"'{name}'. Run CyberBoss/Setup Polish to create and assign it.");
            return;
        }

        SubscribeToGameEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromGameEvents();
        if (Instance == this)
            Instance = null;
    }

    // ------------------------------------------------------------------
    // Event wiring — generic reactions only; per-skill shakes are direct calls
    // ------------------------------------------------------------------

    private void SubscribeToGameEvents()
    {
        _onPlayerDiedDelegate      = TriggerMediumShake;
        _onBossDamageTakenDelegate = _ => TriggerLightShake();
        _onBossDiedDelegate        = TriggerBossDeathShake;

        SubscribePlayer();
        SubscribeBoss();
    }

    private void SubscribePlayer()
    {
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null)
        {
            Debug.LogWarning("[ScreenShakeManager] No GameObject tagged 'Player' found. " +
                "Player death shake will not trigger.");
            return;
        }

        _playerHealthSystem = playerGo.GetComponent<HealthSystem>();
        if (_playerHealthSystem != null)
            _playerHealthSystem.OnDied += _onPlayerDiedDelegate;
        else
            Debug.LogWarning("[ScreenShakeManager] HealthSystem not found on " +
                $"'{playerGo.name}'. Player death shake will not trigger.");
    }

    private void SubscribeBoss()
    {
        var bossGo = GameObject.FindWithTag("Boss");
        if (bossGo == null)
        {
            Debug.LogWarning("[ScreenShakeManager] No GameObject tagged 'Boss' found. " +
                "Boss hit and death shakes will not trigger.");
            return;
        }

        _bossHealthSystem = bossGo.GetComponent<HealthSystem>();
        if (_bossHealthSystem != null)
            _bossHealthSystem.OnDamageTaken += _onBossDamageTakenDelegate;
        else
            Debug.LogWarning("[ScreenShakeManager] HealthSystem not found on " +
                $"'{bossGo.name}'. Boss damage shakes will not trigger.");

        _bossController = bossGo.GetComponent<BossController>();
        if (_bossController != null)
            _bossController.OnBossDied += _onBossDiedDelegate;
        else
            Debug.LogWarning("[ScreenShakeManager] BossController not found on " +
                $"'{bossGo.name}'. Boss death shake will not trigger.");
    }

    private void UnsubscribeFromGameEvents()
    {
        if (_playerHealthSystem != null && _onPlayerDiedDelegate != null)
            _playerHealthSystem.OnDied -= _onPlayerDiedDelegate;

        if (_bossHealthSystem != null && _onBossDamageTakenDelegate != null)
            _bossHealthSystem.OnDamageTaken -= _onBossDamageTakenDelegate;

        if (_bossController != null && _onBossDiedDelegate != null)
            _bossController.OnBossDied -= _onBossDiedDelegate;
    }

    // ------------------------------------------------------------------
    // Core impulse generation — zero heap allocation per call
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes shape/duration onto the shared ImpulseDefinition then fires.
    /// Single-threaded gameplay guarantees no concurrent calls — mutating
    /// ImpulseDefinition before Generate is safe.
    /// </summary>
    private void GenerateShake(float force, float duration, bool snap)
    {
        if (_impulseSource == null || _config == null) return;
        _impulseSource.ImpulseDefinition.ImpulseShape    = snap
            ? CinemachineImpulseDefinition.ImpulseShapes.Bump
            : CinemachineImpulseDefinition.ImpulseShapes.Explosion;
        _impulseSource.ImpulseDefinition.ImpulseDuration = duration;
        _impulseSource.GenerateImpulseWithForce(force);
    }
}
