using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates all five player skills.
///
/// Receives skill input via PlayerInput SendMessages callbacks.
/// Exposes the state properties and events that other systems depend on:
///
///   Combat (DamageHandler): IsInvincible, IsParryActive, HasBarrier, BarrierHealth,
///                           AbsorbBarrierDamage()
///   HUD (CooldownUI):       AllSkills — IReadOnlyList for cooldown fill bars
///   Tracker:                OnSkillExecuted event (index 0–4, see SkillIndex constants)
///   PlayerController:       IsMovementLocked — suppresses movement during Parry window
///
/// Skill index constants are stable:
///   0 = Dash, 1 = Parry, 2 = RangedBlast, 3 = BurstStrike, 4 = Barrier
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerSkills : MonoBehaviour
{
    // Stable skill indices — used by PlayerBehaviorTracker and CooldownUI.
    public const int SkillIndexDash        = 0;
    public const int SkillIndexParry       = 1;
    public const int SkillIndexRangedBlast = 2;
    public const int SkillIndexBurstStrike = 3;
    public const int SkillIndexBarrier     = 4;

    [SerializeField] private DashSkillConfig        _dashConfig;
    [SerializeField] private ParrySkillConfig       _parryConfig;
    [SerializeField] private RangedBlastSkillConfig _rangedBlastConfig;
    [SerializeField] private BurstStrikeSkillConfig _burstStrikeConfig;
    [SerializeField] private BarrierSkillConfig     _barrierConfig;

    /// <summary>
    /// Placeholder cyan capsule prefab. Must have a ProjectileBehavior component
    /// and a Trigger Collider. Assign in the Inspector.
    /// </summary>
    [SerializeField] private GameObject _projectilePrefab;

    private DashSkill        _dash;
    private ParrySkill       _parry;
    private RangedBlastSkill _rangedBlast;
    private BurstStrikeSkill _burstStrike;
    private BarrierSkill     _barrier;

    private PlayerController _playerController;
    private Animator         _animator;
    private Vector2          _moveInput;
    private Camera           _mainCamera;
    private InputAction      _dashAction;

    // Stored delegates for clean unsubscription in OnDestroy.
    private System.Action       _onPerfectDodgeDelegate;
    private System.Action<float> _onParryReflectDelegate;
    private System.Action<float> _onDamageTakenDelegate;

    // Barrier end-state fan-out delegates.
    private System.Action _onBarrierDamageAbsorbedDelegate;
    private System.Action _onBarrierShieldBrokenDelegate;
    private System.Action _onBarrierShieldExpiredDelegate;

    private static readonly int AttackTriggerHash = Animator.StringToHash("AttackTrigger");

    private ISkill[] _allSkills;

    // ------------------------------------------------------------------
    // BehaviorTracker contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Fired every time the player successfully executes a skill.
    /// Argument is the SkillIndex constant (0–4).
    /// PlayerBehaviorTracker subscribes here to record usage frequency.
    /// </summary>
    public event System.Action<int> OnSkillExecuted;

    /// <summary>
    /// Fired when the player successfully executes a Dash, carrying the world-space
    /// dash direction. PlayerBehaviorTracker subscribes here to classify lateral dodges
    /// (left/right bias). Fires on the same frame as OnSkillExecuted(SkillIndexDash).
    /// </summary>
    public event System.Action<Vector3> OnDashExecuted;

    /// <summary>
    /// Fired every time the player performs a normal (basic) attack. This is
    /// distinct from the 5 tracked skills — no SkillIndex constant applies.
    /// PlayerBehaviorTracker subscribes here to blend attack frequency into
    /// AggressionScore, alongside proximity to the boss.
    /// </summary>
    public event System.Action OnNormalAttackExecuted;

    /// <summary>
    /// Fired when a fired Ranged Blast projectile actually lands on the boss —
    /// NOT when it's fired (OnSkillExecuted(SkillIndexRangedBlast) fires at launch,
    /// before projectile travel time elapses). BossShieldReactiveTrigger subscribes
    /// here instead of OnSkillExecuted for Ranged Blast specifically: rolling the
    /// reactive-shield check at launch time let a player's own shot trigger a
    /// Shield that then blocked that same shot on arrival.
    /// </summary>
    public event System.Action OnRangedBlastHitConfirmed;

    // ------------------------------------------------------------------
    // Barrier end-state fan-out (see BarrierSkill remarks)
    // ------------------------------------------------------------------

    /// <summary>Fired when the Barrier absorbs a hit and stays up. PlayerCombatEffects subscribes.</summary>
    public event System.Action OnBarrierDamageAbsorbed;

    /// <summary>Fired when the Barrier's health depletes to 0. PlayerCombatEffects subscribes.</summary>
    public event System.Action OnBarrierShieldBroken;

    /// <summary>Fired when the Barrier's duration elapses undamaged. PlayerCombatEffects subscribes.</summary>
    public event System.Action OnBarrierShieldExpired;

    // ------------------------------------------------------------------
    // Combat contract (DamageHandler reads these)
    // ------------------------------------------------------------------

    /// <summary>True while a Dash is in progress. DamageHandler skips damage when set.</summary>
    public bool IsInvincible => _dash.IsInvincible;

    /// <summary>True during the Parry window. DamageHandler reflects damage when set.</summary>
    public bool IsParryActive => _parry.IsParryActive;

    /// <summary>True while the Barrier is active. DamageHandler routes damage through it.</summary>
    public bool HasBarrier => _barrier.HasBarrier;

    /// <summary>Remaining barrier HP. Read by HUDController for barrier health display.</summary>
    public float BarrierHealth => _barrier.BarrierHealth;

    /// <summary>
    /// Called by DamageHandler before applying HP damage.
    /// Absorbs as much of incomingDamage into the barrier as possible.
    /// Returns the overflow that should still be applied to the player's HP.
    /// </summary>
    public float AbsorbBarrierDamage(float incomingDamage) =>
        _barrier.AbsorbDamage(incomingDamage);

    // ------------------------------------------------------------------
    // PlayerController contract
    // ------------------------------------------------------------------

    /// <summary>
    /// True during the Parry window. PlayerController checks this to suppress
    /// horizontal movement, enforcing the parry stance commitment.
    /// </summary>
    public bool IsMovementLocked => _parry.IsParryActive;

    // ------------------------------------------------------------------
    // HUD contract (CooldownUI reads these)
    // ------------------------------------------------------------------

    /// <summary>
    /// Ordered list of all skills. Index matches SkillIndex constants.
    /// CooldownUI reads ISkill.IsReady and ISkill.CooldownProgress on each skill.
    /// </summary>
    public IReadOnlyList<ISkill> AllSkills => _allSkills;

    // ------------------------------------------------------------------
    // Respawn contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Clears all in-flight skill state and pending animator triggers.
    /// Call from respawn logic before re-enabling the player.
    /// </summary>
    public void ResetAllSkills()
    {
        foreach (ISkill skill in _allSkills)
            skill.Reset(_animator);
        _animator.ResetTrigger(AttackTriggerHash);
    }

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void OnEnable()
    {
        // Direct subscription fires exactly once per press (not once for Started
        // + once for Performed like SendMessages does). Required for reliable dash.
        if (_dashAction != null)
            _dashAction.performed += OnDashPerformed;
    }

    private void OnDisable()
    {
        if (_dashAction != null)
            _dashAction.performed -= OnDashPerformed;
    }

    private void OnDestroy()
    {
        var dh = GetComponent<DamageHandler>();
        if (dh != null)
        {
            if (_onPerfectDodgeDelegate != null) dh.OnPerfectDodge  -= _onPerfectDodgeDelegate;
            if (_onParryReflectDelegate != null) dh.OnParryReflect  -= _onParryReflectDelegate;
        }

        var hs = GetComponent<HealthSystem>();
        if (hs != null && _onDamageTakenDelegate != null)
            hs.OnDamageTaken -= _onDamageTakenDelegate;

        if (_barrier != null)
        {
            if (_onBarrierDamageAbsorbedDelegate != null) _barrier.OnDamageAbsorbed -= _onBarrierDamageAbsorbedDelegate;
            if (_onBarrierShieldBrokenDelegate   != null) _barrier.OnShieldBroken   -= _onBarrierShieldBrokenDelegate;
            if (_onBarrierShieldExpiredDelegate  != null) _barrier.OnShieldExpired  -= _onBarrierShieldExpiredDelegate;
        }

        // Destroy Dash's runtime-built TrailRenderer GameObject + material.
        _dash?.Cleanup();
    }

    private void Awake()
    {
        ValidateSerializedFields();

        _playerController = GetComponent<PlayerController>();
        _animator         = GetComponent<Animator>();

        // Cache the Dash action for direct subscription (bypasses SendMessages timing).
        var playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
            _dashAction = playerInput.actions["Dash"];
        else
            Debug.LogWarning($"[PlayerSkills] No PlayerInput component on '{name}' — Dash will not respond to input.");

        var cc = GetComponent<CharacterController>();
        var animator      = _animator;
        _mainCamera       = Camera.main;

        if (_mainCamera == null)
            throw new System.InvalidOperationException(
                $"[PlayerSkills] Camera.main is null on '{name}'. " +
                "Ensure exactly one camera in the scene is tagged 'MainCamera'.");
        var cam = _mainCamera;

        _dash = new DashSkill(_dashConfig);
        _dash.Init(cc, transform, animator);

        _parry = new ParrySkill(_parryConfig);
        _parry.Init(animator);

        _rangedBlast = new RangedBlastSkill(_rangedBlastConfig, _projectilePrefab);
        _rangedBlast.Init(transform, cam);

        _burstStrike = new BurstStrikeSkill(_burstStrikeConfig);
        _burstStrike.Init(transform, animator);

        _barrier = new BarrierSkill(_barrierConfig);
        _barrier.Init(animator);

        _allSkills = new ISkill[]
        {
            _dash, _parry, _rangedBlast, _burstStrike, _barrier
        };

        SubscribeBurstEnergyEvents();
    }

    private void Update()
    {
        // Tick all skills each frame — handles cooldowns and ongoing skill state.
        foreach (ISkill skill in _allSkills)
            skill.UpdateCooldown(Time.deltaTime);
    }

    // ------------------------------------------------------------------
    // Input callbacks — PlayerInput SendMessages (method names must match action names).
    // ------------------------------------------------------------------

    // Track move input independently of PlayerController to avoid Update-order
    // dependency when computing the dash direction on the same frame as input.
    private void OnMove(InputValue value) => _moveInput = value.Get<Vector2>();

    private void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;
        _animator.SetTrigger(AttackTriggerHash);
        _burstStrike.NotifyNormalAttack();
        OnNormalAttackExecuted?.Invoke();
    }

    // Dash uses direct InputAction.performed subscription (wired in OnEnable/OnDisable)
    // to guarantee exactly one callback per press. SendMessages fires for both the
    // Started and Performed phases which caused every other press to be swallowed.
    private void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        Vector3 dashDirection = ComputeWorldMoveDirection();
        _dash.PrimeDirection(dashDirection);
        bool wasReady = _dash.IsReady;
        TryExecute(_dash, SkillIndexDash);
        if (wasReady && !_dash.IsReady)
        {
            OnDashExecuted?.Invoke(dashDirection);
            _burstStrike.NotifyDash();
        }
    }

    private void OnParry(InputValue value)
    {
        if (!value.isPressed) return;
        TryExecute(_parry, SkillIndexParry);
    }

    private void OnRangedBlast(InputValue value)
    {
        if (!value.isPressed) return;
        TryExecute(_rangedBlast, SkillIndexRangedBlast);
    }

    private void OnBurstStrike(InputValue value)
    {
        if (!value.isPressed) return;
        TryExecute(_burstStrike, SkillIndexBurstStrike);
    }

    private void OnBarrier(InputValue value)
    {
        if (!value.isPressed) return;
        TryExecute(_barrier, SkillIndexBarrier);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private Vector3 ComputeWorldMoveDirection()
    {
        if (_moveInput.sqrMagnitude < 0.01f) return transform.forward;
        Vector3 camForward = Vector3.ProjectOnPlane(
            _mainCamera.transform.forward, Vector3.up).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(
            _mainCamera.transform.right, Vector3.up).normalized;
        return (camForward * _moveInput.y + camRight * _moveInput.x).normalized;
    }

    /// <summary>
    /// Execute the skill and fire OnSkillExecuted only if the skill was actually
    /// consumed (was ready before the call and is now on cooldown after).
    /// </summary>
    private void TryExecute(ISkill skill, int skillIndex)
    {
        bool wasReady = skill.IsReady;
        skill.Execute(gameObject);

        if (wasReady && !skill.IsReady)
            OnSkillExecuted?.Invoke(skillIndex);
    }

    // ------------------------------------------------------------------
    // Burst Strike energy wiring
    // ------------------------------------------------------------------

    /// <summary>
    /// Single callback registered with RangedBlastSkill's projectile hit — fans out
    /// to both Burst Strike energy gain and the public OnRangedBlastHitConfirmed
    /// event. RangedBlastSkill.SetOnHitCallback only accepts one delegate, so this
    /// method is the fan-out point rather than each consumer subscribing directly.
    /// </summary>
    private void OnRangedBlastHit()
    {
        _burstStrike.NotifyRangedBlast();
        OnRangedBlastHitConfirmed?.Invoke();
    }

    private void SubscribeBurstEnergyEvents()
    {
        _rangedBlast.SetOnHitCallback(OnRangedBlastHit);

        // Perfect dodge also plays Dash's brighter re-trigger VFX + reward
        // freeze/CA, decoupled from Burst Strike's energy gain via a second delegate
        // on the same event rather than reaching into _burstStrike's internals.
        _onPerfectDodgeDelegate = () =>
        {
            _burstStrike.NotifyPerfectDodge();
            _dash.NotifyPerfectDodge();
        };
        _onParryReflectDelegate = _ => _burstStrike.NotifyParry();
        _onDamageTakenDelegate  = _ => _burstStrike.NotifyTakeDamage();

        var dh = GetComponent<DamageHandler>();
        if (dh != null)
        {
            dh.OnPerfectDodge += _onPerfectDodgeDelegate;
            dh.OnParryReflect += _onParryReflectDelegate;
        }

        var hs = GetComponent<HealthSystem>();
        if (hs != null)
            hs.OnDamageTaken += _onDamageTakenDelegate;

        // Barrier end-state fan-out — PlayerCombatEffects subscribes at the
        // PlayerSkills level instead of reaching into BarrierSkill directly.
        _onBarrierDamageAbsorbedDelegate = () => OnBarrierDamageAbsorbed?.Invoke();
        _onBarrierShieldBrokenDelegate   = () => OnBarrierShieldBroken?.Invoke();
        _onBarrierShieldExpiredDelegate  = () => OnBarrierShieldExpired?.Invoke();
        _barrier.OnDamageAbsorbed += _onBarrierDamageAbsorbedDelegate;
        _barrier.OnShieldBroken   += _onBarrierShieldBrokenDelegate;
        _barrier.OnShieldExpired  += _onBarrierShieldExpiredDelegate;
    }

    private void ValidateSerializedFields()
    {
        if (_dashConfig        == null) ThrowMissingField(nameof(_dashConfig));
        if (_parryConfig       == null) ThrowMissingField(nameof(_parryConfig));
        if (_rangedBlastConfig == null) ThrowMissingField(nameof(_rangedBlastConfig));
        if (_burstStrikeConfig == null) ThrowMissingField(nameof(_burstStrikeConfig));
        if (_barrierConfig     == null) ThrowMissingField(nameof(_barrierConfig));

        if (_projectilePrefab == null)
            throw new System.InvalidOperationException(
                $"[PlayerSkills] _projectilePrefab is not assigned on '{name}'. " +
                "Assign the cyan capsule prefab in the Inspector.");
    }

    private void ThrowMissingField(string fieldName) =>
        throw new System.InvalidOperationException(
            $"[PlayerSkills] {fieldName} is not assigned on '{name}'. " +
            "Assign the matching ScriptableObject in the Inspector.");
}
