using UnityEngine;

/// <summary>
/// Reactive Shield Phase trigger.
///
/// ShieldPhase is no longer a turn-based skill the boss deliberately selects —
/// BossSkills.SelectableSkills excludes it, and neither the scripted-random
/// picker nor the RL policy can choose it. Instead, this component rolls a
/// probability check every time the player lands an attack that could deal
/// meaningful damage (normal attack, Burst Strike, or Ranged Blast) and
/// reactively triggers Shield if the roll succeeds and it's off cooldown.
///
/// Trigger probability = clamp01(RangedBlastFrequencyWeight * rangedBlastFreq
///                              + AggressionWeight * aggressionScore)
/// — players who lean on Ranged Blast (high burst damage) or sustain
/// close-range pressure get shielded against more often. This replaces the
/// old design where Shield fired on raw parry frequency regardless of
/// whether the player was actually threatening the boss at that moment,
/// which produced a stalemate: the boss would shield indefinitely against a
/// player who was parrying without ever attacking.
///
/// Guards (added after devil's-advocate review found this could reproduce the
/// same class of bug it was meant to fix, just relocated):
///   - Never fires during Stagger or after Dead — otherwise a player
///     continuing to attack during their earned stagger window could get
///     reactively shielded out of the damage they just earned.
///   - Never fires while another skill is already executing
///     (BossSkills.IsAnySkillExecuting) — prevents concurrent Shield +
///     turn-based-skill execution and the resulting Animator trigger collision.
///   - Ranged Blast is checked via OnRangedBlastHitConfirmed (fires on
///     projectile impact), not OnSkillExecuted (fires at launch) — otherwise a
///     player's own shot could trigger the Shield that then blocks that same
///     shot before it lands.
/// </summary>
[RequireComponent(typeof(BossSkills))]
public class BossShieldReactiveTrigger : MonoBehaviour
{
    [SerializeField] private BossShieldReactiveTriggerConfig _config;

    private BossSkills            _bossSkills;
    private BossController        _bossController;
    private PlayerSkills          _playerSkills;
    private PlayerBehaviorTracker _behaviorTracker;

    private void Awake()
    {
        _bossSkills     = GetComponent<BossSkills>();
        _bossController = GetComponent<BossController>();

        if (_config == null)
            throw new System.InvalidOperationException(
                $"[BossShieldReactiveTrigger] BossShieldReactiveTriggerConfig not assigned " +
                $"on '{name}'. Run CyberBoss/Setup Boss Skills to create and assign it.");

        float weightSum = _config.RangedBlastFrequencyWeight + _config.AggressionWeight;
        if (_config.RangedBlastFrequencyWeight < 0f || _config.AggressionWeight < 0f || weightSum > 1f)
            Debug.LogWarning(
                $"[BossShieldReactiveTrigger] Weights on '{_config.name}' are " +
                $"RangedBlastFrequencyWeight={_config.RangedBlastFrequencyWeight}, " +
                $"AggressionWeight={_config.AggressionWeight} (sum={weightSum}). " +
                "Negative weights or a sum > 1 get silently absorbed by the probability's " +
                "Clamp01 — the trigger will saturate to 100% across a wider input range " +
                "than intended, with no other signal that the extra weight is doing nothing.");
    }

    // Retry in both Start() and OnEnable() — Awake/OnEnable may run before the
    // Player GameObject exists (mirrors BossRLAgent's CachePlayerBehaviorTracker
    // retry pattern). CachePlayer() only assigns fields; each caller is
    // responsible for calling Subscribe() itself so a single enable cycle
    // never subscribes twice.
    private void Start()
    {
        if (_playerSkills == null)
        {
            CachePlayer();
            if (_playerSkills != null) Subscribe();
        }
    }

    // OnEnable/OnDisable rather than Start/OnDestroy — matches
    // PlayerBehaviorTracker's pattern so subscriptions are correctly
    // re-established if this GameObject is ever toggled between episodes
    // instead of scene-reloaded.
    private void OnEnable()
    {
        if (_playerSkills == null) CachePlayer();
        if (_playerSkills != null) Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void CachePlayer()
    {
        if (_playerSkills != null) return; // already cached

        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null)
        {
            Debug.LogWarning("[BossShieldReactiveTrigger] No GameObject tagged 'Player' found. " +
                "Reactive Shield will never trigger — tag the player 'Player' and re-enter Play mode.");
            return;
        }

        _playerSkills    = playerGo.GetComponent<PlayerSkills>();
        _behaviorTracker = playerGo.GetComponent<PlayerBehaviorTracker>();

        if (_playerSkills == null)
        {
            Debug.LogWarning($"[BossShieldReactiveTrigger] PlayerSkills not found on " +
                $"'{playerGo.name}'. Reactive Shield will never trigger.");
            return;
        }

        if (_behaviorTracker == null)
            Debug.LogWarning($"[BossShieldReactiveTrigger] PlayerBehaviorTracker not found on " +
                $"'{playerGo.name}'. Trigger probability will use neutral defaults (0 frequency, " +
                "0 aggression) until it's added — Shield will effectively never fire reactively.");
    }

    private void Subscribe()
    {
        if (_playerSkills == null) return;
        _playerSkills.OnNormalAttackExecuted   += OnQualifyingAttack;
        _playerSkills.OnSkillExecuted          += OnPlayerSkillExecuted;
        _playerSkills.OnRangedBlastHitConfirmed += OnQualifyingAttack;
    }

    private void Unsubscribe()
    {
        if (_playerSkills == null) return;
        _playerSkills.OnNormalAttackExecuted   -= OnQualifyingAttack;
        _playerSkills.OnSkillExecuted          -= OnPlayerSkillExecuted;
        _playerSkills.OnRangedBlastHitConfirmed -= OnQualifyingAttack;
    }

    /// <summary>
    /// Only Burst Strike counts here — it's a same-frame melee overlap check
    /// (no travel time), so launch-time and hit-time are the same instant.
    /// Ranged Blast is handled via OnRangedBlastHitConfirmed instead (see
    /// class remarks) because its projectile has real travel time.
    /// </summary>
    private void OnPlayerSkillExecuted(int skillIndex)
    {
        if (skillIndex == PlayerSkills.SkillIndexBurstStrike)
            OnQualifyingAttack();
    }

    private void OnQualifyingAttack()
    {
        if (_bossController != null &&
            (_bossController.CurrentState == BossController.BossState.Stagger ||
             _bossController.CurrentState == BossController.BossState.Dead))
            return;

        // Don't overlap with an already-executing turn-based skill (or a
        // still-running Shield from a previous reactive trigger).
        if (_bossSkills.IsAnySkillExecuting) return;

        if (!_bossSkills.ShieldPhase.IsReady) return;

        BehaviorStatVector stats = _behaviorTracker != null
            ? _behaviorTracker.GetCurrentVector()
            : default;

        float rangedBlastFreq = stats.SkillUsageFrequency2;
        float aggression      = stats.AggressionScore;

        float probability = Mathf.Clamp01(
            _config.RangedBlastFrequencyWeight * rangedBlastFreq +
            _config.AggressionWeight * aggression);

        if (Random.value < probability)
            _bossSkills.ShieldPhase.Execute(gameObject);
    }
}
