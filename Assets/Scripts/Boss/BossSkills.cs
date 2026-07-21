using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns, initializes, and ticks all five boss skills.
///
/// Lifecycle:
///   Awake — creates skill instances from per-skill ScriptableObject configs.
///   Start  — finds the Player by tag, caches references, calls Init on each skill.
///            Start is used (not Awake) so all player Awake calls complete first.
///
/// Coroutine host: boss skill coroutines run on this MonoBehaviour so that
/// StopAllCoroutines() on BossController (stagger) does NOT kill them automatically.
/// BossController.StaggerRoutine() calls ResetActiveSkill() to stop them explicitly
/// at the correct moment with clean state restoration per skill.
///
/// Cross-system contracts:
///   AllSkills        — IReadOnlyList used by BossController and BossRLAgent.
///                      Index order: 0=Charge, 1=ProjectileBurst, 2=AoeSlam,
///                      3=ShieldPhase, 4=TeleportStrike. Do not reorder.
///   ResetAllSkills() — called by respawn logic before re-enabling the boss.
///   IsShielded           — BossController.TakeDamage() reads this to block damage.
///   IsAnySkillExecuting  — BossController.SkillSelectionLoop yields on this.
///   ResetActiveSkill()   — BossController.StaggerRoutine calls this after StopAllCoroutines().
/// </summary>
public class BossSkills : MonoBehaviour
{
    [Header("Skill Configs")]
    [SerializeField] private BossChargeConfig          _chargeConfig;
    [SerializeField] private BossProjectileBurstConfig _projectileBurstConfig;
    [SerializeField] private BossAoESlamConfig         _aoeSlamConfig;
    [SerializeField] private BossShieldPhaseConfig     _shieldPhaseConfig;
    [SerializeField] private BossTeleportStrikeConfig  _teleportStrikeConfig;

    [Header("Arena Bounds")]
    [Tooltip("Provides ArenaRadius and ArenaCenterPosition for boss skill bounds clamping. " +
             "Assign the same BehaviorTrackerConfig asset used by PlayerBehaviorTracker.")]
    [SerializeField] private BehaviorTrackerConfig _trackerConfig;

    [Header("Projectile")]
    [Tooltip("Red/orange capsule prefab with ProjectileBehavior and a Trigger Collider. " +
             "Run CyberBoss/Setup Boss Skills to create it automatically.")]
    [SerializeField] private GameObject _bossProjectilePrefab;

    private BossChargeSkill          _charge;
    private BossProjectileBurstSkill _projectileBurst;
    private BossAoESlamSkill         _aoeSlam;
    private BossShieldPhaseSkill     _shieldPhase;
    private BossTeleportStrikeSkill  _teleportStrike;

    private ISkill[] _allSkills;
    private ISkill[] _selectableSkills;
    private float[]  _selectableSkillCooldownDurations;

    // ------------------------------------------------------------------
    // AllSkills index order is locked — BossRLAgent maps action indices to it.
    // ------------------------------------------------------------------

    /// <summary>
    /// All five boss skills in stable index order. Includes ShieldPhase — needed
    /// here for cooldown ticking, IsAnySkillExecuting, and IsShielded.
    /// 0=Charge, 1=ProjectileBurst, 2=AoeSlam, 3=ShieldPhase, 4=TeleportStrike.
    /// Do not reorder.
    ///
    /// NOT used for turn-based skill selection — see SelectableSkills.
    /// </summary>
    public IReadOnlyList<ISkill> AllSkills => _allSkills;

    /// <summary>
    /// The subset of skills eligible for turn-based selection (scripted random
    /// or RL-driven). ShieldPhase is deliberately excluded — it is a reactive
    /// skill triggered by BossShieldReactiveTrigger on player attacks, never
    /// a deliberate "turn" pick.
    ///
    /// 0=Charge, 1=ProjectileBurst, 2=AoeSlam, 3=TeleportStrike.
    /// Do not reorder — BossRLAgent maps action indices to this order,
    /// and the trained ONNX model's 4-way output assumes it.
    /// </summary>
    public IReadOnlyList<ISkill> SelectableSkills => _selectableSkills;

    /// <summary>
    /// Max cooldown duration (seconds) for each entry in <see cref="SelectableSkills"/>,
    /// same index order. ISkill only exposes normalized 0-1 CooldownProgress, which
    /// isn't enough to compare raw remaining time across skills with different total
    /// cooldowns — BossRLAgent's cooldown-substitution needs raw seconds to match
    /// cyberboss_env.py's argmin(remaining_seconds) exactly. Values must stay in sync
    /// with SKILL_COOLDOWNS_SEC in cyberboss_env.py.
    /// </summary>
    public IReadOnlyList<float> SelectableSkillCooldownDurations => _selectableSkillCooldownDurations;

    /// <summary>
    /// The shield-phase skill instance, exposed directly for
    /// BossShieldReactiveTrigger — it is not part of SelectableSkills and needs
    /// its own accessor to check IsReady / call Execute() reactively.
    /// </summary>
    public ISkill ShieldPhase => _shieldPhase;

    // ------------------------------------------------------------------
    // Shield/execution state properties
    // ------------------------------------------------------------------

    /// <summary>
    /// True while the Shield Phase is active. BossController.TakeDamage() reads
    /// this and returns early to block all incoming damage.
    /// </summary>
    public bool IsShielded => _shieldPhase.IsShielded;

    /// <summary>
    /// True while any skill's execution coroutine is running.
    /// BossController.SkillSelectionLoop yields on this after calling Execute()
    /// so the next skill is not selected until the current one fully completes.
    /// </summary>
    public bool IsAnySkillExecuting =>
        _charge.IsExecuting          ||
        _projectileBurst.IsExecuting ||
        _aoeSlam.IsExecuting         ||
        _shieldPhase.IsExecuting     ||
        _teleportStrike.IsExecuting;

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        ValidateConfigs();

        _charge          = new BossChargeSkill(_chargeConfig);
        _projectileBurst = new BossProjectileBurstSkill(_projectileBurstConfig, _bossProjectilePrefab);
        _aoeSlam         = new BossAoESlamSkill(_aoeSlamConfig);
        _shieldPhase     = new BossShieldPhaseSkill(_shieldPhaseConfig);
        _teleportStrike  = new BossTeleportStrikeSkill(_teleportStrikeConfig);

        _allSkills = new ISkill[]
        {
            _charge, _projectileBurst, _aoeSlam, _shieldPhase, _teleportStrike
        };

        _selectableSkills = new ISkill[]
        {
            _charge, _projectileBurst, _aoeSlam, _teleportStrike
        };

        _selectableSkillCooldownDurations = new float[]
        {
            _chargeConfig.Cooldown, _projectileBurstConfig.Cooldown,
            _aoeSlamConfig.Cooldown, _teleportStrikeConfig.Cooldown
        };
    }

    private void Start()
    {
        // FindWithTag runs after all Awake() calls complete — player is guaranteed to exist.
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null)
        {
            Debug.LogError(
                "[BossSkills] No GameObject tagged 'Player' found. " +
                "Boss skills will not execute — tag the player 'Player' and re-enter Play mode.");
            return;
        }

        Transform             playerTransform     = playerGo.transform;
        DamageHandler         playerDamageHandler = playerGo.GetComponent<DamageHandler>();
        PlayerBehaviorTracker playerBehaviorTracker = playerGo.GetComponent<PlayerBehaviorTracker>();

        if (playerDamageHandler == null)
            Debug.LogWarning(
                $"[BossSkills] DamageHandler not found on '{playerGo.name}'. " +
                "Charge contact damage will not apply. " +
                "Run CyberBoss/Setup Player Combat to add DamageHandler.");

        if (playerBehaviorTracker == null)
            Debug.LogWarning(
                $"[BossSkills] PlayerBehaviorTracker not found on '{playerGo.name}'. " +
                "Teleport Strike will land directly behind the player instead of " +
                "predicting a dodge side. Run CyberBoss/Setup Behavior Tracker to add it.");

        Renderer[] bossRenderers = GetComponentsInChildren<Renderer>();

        var bossAnimator = GetComponent<Animator>();
        if (bossAnimator == null)
            Debug.LogWarning(
                "[BossSkills] Animator not found on boss — skill animations will not play. " +
                "Run CyberBoss/Setup Boss Animator to create and assign the controller.");

        // Extract arena parameters once and share across skills that need bounds clamping.
        // Zero radius disables clamping gracefully in each skill — no divide-by-zero risk.
        Vector3 arenaCenter = Vector3.zero;
        float   arenaRadius = 0f;
        if (_trackerConfig != null)
        {
            arenaCenter = _trackerConfig.ArenaCenterPosition;
            arenaRadius = _trackerConfig.ArenaRadius;
        }
        else
        {
            Debug.LogWarning(
                "[BossSkills] BehaviorTrackerConfig not assigned — " +
                "boss skills will not clamp to arena bounds. " +
                "Assign the same asset used by PlayerBehaviorTracker.");
        }

        _charge.Init(
            this, transform, playerTransform, playerDamageHandler,
            bossRenderers, bossAnimator, arenaCenter, arenaRadius);
        _projectileBurst.Init(this, transform, playerTransform, bossAnimator);
        _aoeSlam.Init(this, transform, playerTransform, bossAnimator);
        _shieldPhase.Init(this, transform, bossRenderers, bossAnimator);
        _teleportStrike.Init(
            this, transform, playerTransform, bossAnimator, arenaCenter, arenaRadius,
            playerBehaviorTracker);
    }

    private void Update()
    {
        // Index loop avoids IEnumerator boxing — called every frame.
        for (int i = 0; i < _allSkills.Length; i++)
            _allSkills[i].UpdateCooldown(Time.deltaTime);
    }

    private void OnDestroy()
    {
        // Charge, AoE Slam, and Teleport Strike allocate Material instances in Init().
        // UnityEngine.Object instances are not reclaimed by the C# GC — must destroy explicitly.
        _charge?.Cleanup();
        _projectileBurst?.Cleanup();
        _aoeSlam?.Cleanup();
        _teleportStrike?.Cleanup();
    }

    // ------------------------------------------------------------------
    // Stagger / death cleanup
    // ------------------------------------------------------------------

    /// <summary>
    /// Stop all in-flight skill coroutines and restore any dirty per-skill state
    /// (shield tint cleared, boss scale restored, slam indicator destroyed, etc.).
    ///
    /// Call from BossController immediately after StopAllCoroutines() on stagger or
    /// death. Does NOT call StopAllCoroutines() on BossSkills itself — only the
    /// active skill coroutines are stopped so BossSkills remains ready to host
    /// the next skill coroutine after stagger ends.
    /// </summary>
    public void ResetActiveSkill()
    {
        _charge.StopExecution();
        _projectileBurst.StopExecution();
        _aoeSlam.StopExecution();
        _shieldPhase.StopExecution();
        _teleportStrike.StopExecution();
    }

    // ------------------------------------------------------------------
    // Respawn contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Clear all in-flight cooldown and animation state.
    /// Call from respawn logic before re-enabling the boss.
    /// </summary>
    public void ResetAllSkills(Animator animator)
    {
        // Stop any running coroutines first so Reset() receives a clean state.
        ResetActiveSkill();

        for (int i = 0; i < _allSkills.Length; i++)
            _allSkills[i].Reset(animator);
    }

    // ------------------------------------------------------------------
    // Validation
    // ------------------------------------------------------------------

    private void ValidateConfigs()
    {
        if (_chargeConfig          == null) ThrowMissingConfig(nameof(_chargeConfig));
        if (_projectileBurstConfig == null) ThrowMissingConfig(nameof(_projectileBurstConfig));
        if (_aoeSlamConfig         == null) ThrowMissingConfig(nameof(_aoeSlamConfig));
        if (_shieldPhaseConfig     == null) ThrowMissingConfig(nameof(_shieldPhaseConfig));
        if (_teleportStrikeConfig  == null) ThrowMissingConfig(nameof(_teleportStrikeConfig));

        if (_bossProjectilePrefab == null)
            throw new System.InvalidOperationException(
                $"[BossSkills] _bossProjectilePrefab is not assigned on '{name}'. " +
                "Run CyberBoss/Setup Boss Skills to create and assign the orange boss projectile prefab.");
    }

    private void ThrowMissingConfig(string fieldName) =>
        throw new System.InvalidOperationException(
            $"[BossSkills] {fieldName} is not assigned on '{name}'. " +
            "Run CyberBoss/Setup Boss Skills to create and assign all per-skill configs.");
}
