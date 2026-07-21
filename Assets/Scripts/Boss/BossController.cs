using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss state machine and IDamageable entry point.
///
/// This component must be the first IDamageable on the boss GameObject so that
/// GetComponent&lt;IDamageable&gt;() finds it before HealthSystem. SetupBoss.cs adds
/// BossController first in the component list. Shield-phase logic inside TakeDamage()
/// runs before the call to _healthSystem.TakeDamage().
///
/// State transitions:
///   Idle → SelectSkill     — on Start()
///   SelectSkill → ExecutingSkill — when a ready skill is selected
///   ExecutingSkill → SelectSkill — immediately after Execute() returns
///   Any → Stagger          — OnDamageTaken when hit exceeds StaggerThreshold
///   Stagger → SelectSkill  — after StaggerDuration elapses
///   Any → Dead             — OnDied from HealthSystem
///
/// Cross-system contract:
///   IDamageable.TakeDamage(float) — player skills call this to deal damage
///   CurrentState                  — HUDController reads Dead to trigger the win screen
///   OnBossDied                    — HUDController subscribes for win-screen transition
/// </summary>
public class BossController : MonoBehaviour, IDamageable
{
    /// <summary>Boss state machine states.</summary>
    public enum BossState { Idle, SelectSkill, ExecutingSkill, Stagger, Dead }

    [SerializeField] private BossConfig            _config;
    [SerializeField] private BehaviorTrackerConfig _trackerConfig;

    // Assign BossRLAgent in the Inspector to enable RL skill selection.
    // When assigned and BossRLAgent._useRLPolicy is true, SkillSelectionLoop
    // calls _bossRLAgent.SelectSkillIndex() instead of SelectRandomReadySkill().
    [SerializeField] private BossRLAgent _bossRLAgent;

    private HealthSystem    _healthSystem;
    private BossSkills      _bossSkills;
    private DamageHandler   _playerDamageHandler;
    private HealthSystem    _playerHealthSystem;
    private Transform       _playerTransform;
    private BossState       _currentState;
    private Animator        _animator;

    // Pre-allocated — avoids re-allocation on re-enable.
    private System.Action _onPlayerDiedDelegate;

    // Cached yield object — avoids per-iteration heap allocation in StaggerRoutine.
    private WaitForSeconds _staggerWait;

    // Animator state names — must match SetupBossAnimator state names exactly.
    private const string IdleStateName = "ChargeWindup";
    private const string WalkStateName = "ChargeRun";

    // Animator trigger hashes — wired to BossAnimator.controller by SetupBossAnimator.
    private static readonly int StaggerAnimHash = Animator.StringToHash("StaggerTrigger");
    private static readonly int DeathAnimHash   = Animator.StringToHash("DeathTrigger");

    // ------------------------------------------------------------------
    // HUD / GameLoop contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Current state. HUDController reads Dead to transition to the win screen.
    /// </summary>
    public BossState CurrentState => _currentState;

    /// <summary>
    /// Fired exactly once when the boss dies.
    /// HUDController subscribes here to trigger the win screen.
    /// </summary>
    public event System.Action OnBossDied;

    /// <summary>
    /// Fired from TakeDamage() when incoming damage is blocked by an active Shield
    /// Phase. Argument is the boss's position (hit point), used by
    /// CombatVFXManager to spawn a deflect-spark cue. Previously this path was a
    /// bare Debug.Log with zero player-facing feedback that the hit did nothing.
    /// BossController deliberately does not call into VFX systems directly here —
    /// CombatVFXManager subscribes to this event instead, keeping the low-coupling
    /// rule (communicate via events, not direct references into VFX code).
    /// </summary>
    public event System.Action<Vector3> OnShieldBlockedDamage;

    /// <summary>
    /// Clears episode-scoped accumulators at the start of a new fight.
    /// Called by GameOverScreen before scene reload so the PlayerBehaviorTracker
    /// does not carry stale stats into the next episode.
    /// </summary>
    public void NotifyFightStart()
    {
        if (_bossRLAgent != null)
            _bossRLAgent.OnEpisodeBegin();
    }

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        _healthSystem = GetComponent<HealthSystem>();
        _bossSkills   = GetComponent<BossSkills>();
        _animator     = GetComponent<Animator>(); // null until Setup Boss Animator is run

        if (_config == null)
            throw new System.InvalidOperationException(
                $"[BossController] BossConfig not assigned on '{name}'. " +
                "Run CyberBoss/Setup Boss to create and assign it.");

        if (_healthSystem == null)
            throw new System.InvalidOperationException(
                $"[BossController] HealthSystem missing on '{name}'. " +
                "Run CyberBoss/Setup Boss to add it.");

        if (_bossSkills == null)
            throw new System.InvalidOperationException(
                $"[BossController] BossSkills missing on '{name}'. " +
                "Run CyberBoss/Setup Boss to add it.");

        _staggerWait = new WaitForSeconds(_config.StaggerDuration);
    }

    private void OnEnable()
    {
        // _healthSystem is null only if OnEnable fires before Awake (not normal Unity flow).
        if (_healthSystem == null) return;

        _healthSystem.OnDamageTaken += OnHealthDamageTaken;
        _healthSystem.OnDied        += OnHealthDied;

        // Re-subscribe player events after toggle; Start() wires them on first activation.
        if (_playerDamageHandler != null)
            _playerDamageHandler.OnParryReflect += TakeDamage;
        if (_playerHealthSystem != null && _onPlayerDiedDelegate != null)
            _playerHealthSystem.OnDied += _onPlayerDiedDelegate;
    }

    private void OnDisable()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDamageTaken -= OnHealthDamageTaken;
            _healthSystem.OnDied        -= OnHealthDied;
        }

        if (_playerDamageHandler != null)
            _playerDamageHandler.OnParryReflect -= TakeDamage;

        if (_playerHealthSystem != null && _onPlayerDiedDelegate != null)
            _playerHealthSystem.OnDied -= _onPlayerDiedDelegate;
    }

    private void Start()
    {
        _healthSystem.Initialize(_config.MaxHealth);
        SubscribeToParryReflect();
        CachePlayerTransform();

        _currentState = BossState.Idle;
        StartCoroutine(SkillSelectionLoop());
    }

    // ------------------------------------------------------------------
    // IDamageable
    // ------------------------------------------------------------------

    /// <summary>
    /// Route incoming damage to HealthSystem.
    /// Shield phase blocks all damage while BossSkills.IsShielded is true.
    /// Always call this — never call HealthSystem.TakeDamage() directly on the boss.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (_currentState == BossState.Dead || damage <= 0f) return;

        // Shield-phase blocks all incoming damage for its duration.
        if (_bossSkills.IsShielded)
        {
            Debug.Log($"[Combat] Boss blocked {damage:F0} (shield phase)");
            OnShieldBlockedDamage?.Invoke(transform.position);
            return;
        }

        _healthSystem.TakeDamage(damage);
    }

    // ------------------------------------------------------------------
    // HealthSystem callbacks
    // ------------------------------------------------------------------

    private void OnHealthDamageTaken(float damage)
    {
        if (_currentState == BossState.Dead) return;
        // Stagger immunity while already staggered — prevents infinite stunlock.
        if (_currentState == BossState.Stagger) return;
        if (damage < _config.StaggerThreshold) return;

        StopAllCoroutines();
        StartCoroutine(StaggerRoutine());
    }

    private void OnHealthDied()
    {
        StopAllCoroutines();
        // Skill coroutines run on BossSkills — stop them and restore any dirty state
        // (shield tint, scale, slam indicator) so the death pose is clean.
        _bossSkills.ResetActiveSkill();
        _currentState = BossState.Dead;
        if (_animator != null) _animator.SetTrigger(DeathAnimHash);
        OnBossDied?.Invoke();
        Debug.Log("[BossController] Boss died.");
    }

    // ------------------------------------------------------------------
    // Coroutines
    // ------------------------------------------------------------------

    private IEnumerator SkillSelectionLoop()
    {
        _currentState = BossState.SelectSkill;

        while (true)
        {
            yield return StartCoroutine(ApproachAndWait(_config.SkillInterval));

            // BossShieldReactiveTrigger can start Shield mid-approach (it's not
            // gated by this loop's timing). Wait for it to clear before selecting
            // and executing a new turn-based skill, so the two never run
            // concurrently — Shield's own IsReady/state guards keep this bounded.
            while (_bossSkills.IsAnySkillExecuting)
                yield return null;

            ISkill skill;
            if (_bossRLAgent != null && _bossRLAgent.IsRLActive)
            {
                int skillIndex = _bossRLAgent.SelectSkillIndex();
                ISkill candidate = _bossSkills.SelectableSkills[skillIndex];
                // Guard: ApplyCooldownSubstitution returns the best-countering ready
                // skill, but if all four selectable skills are still cooling down it
                // falls back to the closest-to-ready one (which may not truly be ready
                // yet) — we skip this tick in that case, same behaviour as the scripted
                // path returning null when readyCount == 0.
                skill = candidate.IsReady ? candidate : null;
            }
            else
            {
                skill = SelectRandomReadySkill();
            }
            if (skill != null)
            {
                _currentState = BossState.ExecutingSkill;
                skill.Execute(gameObject);

                // Wait for the skill coroutine (running on BossSkills) to finish
                // before selecting the next skill.
                // Timeout prevents permanent deadlock if a coroutine throws mid-execution
                // and never clears its IsExecuting flag.
                float skillTimeout = 15f;
                while (_bossSkills.IsAnySkillExecuting && skillTimeout > 0f)
                {
                    skillTimeout -= Time.deltaTime;
                    yield return null;
                }
                if (skillTimeout <= 0f)
                {
                    Debug.LogWarning("[BossController] Skill execution timed out. " +
                        "Check boss skill coroutines for unhandled exceptions.");
                    // Force-clear IsExecuting on all skills so a stale zombie coroutine
                    // cannot be selected again and run concurrently with a fresh execution.
                    _bossSkills.ResetActiveSkill();
                }

                _currentState = BossState.SelectSkill;
            }
        }
    }

    private IEnumerator StaggerRoutine()
    {
        _currentState = BossState.Stagger;

        // Skill coroutines run on BossSkills — StopAllCoroutines() above did not
        // stop them. Reset now to restore any dirty state (scale, shield, indicator).
        _bossSkills.ResetActiveSkill();
        if (_animator != null) _animator.SetTrigger(StaggerAnimHash);

        Debug.Log("[BossController] Stagger.");
        yield return _staggerWait;

        if (_currentState != BossState.Dead)
            StartCoroutine(SkillSelectionLoop());
    }

    // ------------------------------------------------------------------
    // Skill selection — no heap allocation
    // ------------------------------------------------------------------

    // ShieldPhase is intentionally excluded (SelectableSkills, not AllSkills) —
    // as of the #5 rework it only fires reactively via BossShieldReactiveTrigger,
    // never as a deliberate scripted or RL "turn" pick.
    private ISkill SelectRandomReadySkill()
    {
        IReadOnlyList<ISkill> skills = _bossSkills.SelectableSkills;

        int readyCount = 0;
        for (int i = 0; i < skills.Count; i++)
        {
            if (skills[i].IsReady) readyCount++;
        }

        if (readyCount == 0) return null;

        int pick    = Random.Range(0, readyCount);
        int current = 0;
        for (int i = 0; i < skills.Count; i++)
        {
            if (!skills[i].IsReady) continue;
            if (current == pick) return skills[i];
            current++;
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Arena bounds — safety net for any skill that moves the boss
    // ------------------------------------------------------------------

    /// Clamps the boss to the circular arena after all skill movement has run.
    /// The boss has no CharacterController, so direct position assignment is safe.
    private void LateUpdate()
    {
        ClampToArenaBounds();
    }

    private void ClampToArenaBounds()
    {
        if (_trackerConfig == null) return;

        Vector3 center     = _trackerConfig.ArenaCenterPosition;
        float   halfExtent = _trackerConfig.ArenaRadius;
        Vector3 pos        = transform.position;

        // Square clamp matches the actual rectangular arena walls.
        pos.x = Mathf.Clamp(pos.x, center.x - halfExtent, center.x + halfExtent);
        pos.z = Mathf.Clamp(pos.z, center.z - halfExtent, center.z + halfExtent);
        transform.position = pos;
    }

    // ------------------------------------------------------------------
    // Approach movement — runs during the skill interval between executions
    // ------------------------------------------------------------------

    /// <summary>
    /// Walks the boss toward the player for <paramref name="duration"/> seconds.
    /// Stops and idles when within ApproachStopDistance. CrossFades the animator
    /// between walk and idle states only on state change to avoid per-frame calls.
    /// StopAllCoroutines() on stagger naturally interrupts this coroutine.
    /// </summary>
    private IEnumerator ApproachAndWait(float duration)
    {
        float elapsed   = 0f;
        bool  isWalking = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            if (_playerTransform != null)
            {
                Vector3 toPlayer = _playerTransform.position - transform.position;
                toPlayer.y       = 0f;
                float dist       = toPlayer.magnitude;
                bool  shouldWalk = dist > _config.ApproachStopDistance;

                if (shouldWalk)
                {
                    Vector3 dir        = toPlayer / dist;
                    transform.position += dir * _config.ApproachSpeed * Time.deltaTime;
                    transform.rotation  = Quaternion.LookRotation(dir, Vector3.up);

                    if (!isWalking)
                    {
                        if (_animator != null) _animator.CrossFade(WalkStateName, 0.2f, 0);
                        isWalking = true;
                    }
                }
                else if (isWalking)
                {
                    if (_animator != null) _animator.CrossFade(IdleStateName, 0.2f, 0);
                    isWalking = false;
                }
            }

            yield return null;
        }

        // Ensure we return to idle if the interval ended while still walking.
        if (isWalking && _animator != null)
            _animator.CrossFade(IdleStateName, 0.15f, 0);
    }

    // ------------------------------------------------------------------
    // Parry reflect wiring
    // ------------------------------------------------------------------

    private void CachePlayerTransform()
    {
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo != null)
            _playerTransform = playerGo.transform;
    }

    private void SubscribeToParryReflect()
    {
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null)
        {
            Debug.LogWarning("[BossController] No GameObject tagged 'Player' found. " +
                "Parry reflect disabled — tag the player 'Player' and re-enter play mode.");
            return;
        }

        _playerDamageHandler = playerGo.GetComponent<DamageHandler>();
        if (_playerDamageHandler == null)
        {
            Debug.LogWarning($"[BossController] DamageHandler not found on '{playerGo.name}'. " +
                "Run CyberBoss/Setup Player Combat to add it. Parry reflect disabled.");
        }
        else
        {
            // Parry reflect routes through TakeDamage so shield logic applies.
            _playerDamageHandler.OnParryReflect += TakeDamage;
        }

        _playerHealthSystem = playerGo.GetComponent<HealthSystem>();
        if (_playerHealthSystem != null)
        {
            _onPlayerDiedDelegate = OnPlayerDied;
            _playerHealthSystem.OnDied += _onPlayerDiedDelegate;
        }
        else
        {
            Debug.LogWarning($"[BossController] HealthSystem not found on '{playerGo.name}'. " +
                "Boss will not stop on player death.");
        }
    }

    private void OnPlayerDied()
    {
        // Stop the skill-selection loop and any in-progress skill coroutines.
        // BossSkills coroutines run on BossSkills, not BossController, so
        // StopAllCoroutines() here only kills the loop/stagger — ResetActiveSkill
        // handles the skill side.
        StopAllCoroutines();
        _bossSkills.ResetActiveSkill();
        _currentState = BossState.Idle;
        if (_animator != null)
            _animator.CrossFade(IdleStateName, 0.2f, 0);
        Debug.Log("[BossController] Player died — boss halted.");
    }
}
