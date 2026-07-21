using UnityEngine;

/// <summary>
/// Barrier skill implementation.
///
/// Activates a personal shield with a health pool and a maximum duration.
/// The barrier expires when either BarrierHealth reaches zero or the duration timer
/// elapses — whichever comes first.
///
/// DamageHandler contract:
///   1. Check PlayerSkills.HasBarrier before applying HP damage.
///   2. Call PlayerSkills.AbsorbBarrierDamage(amount) to drain the shield.
///   3. Apply only the overflow (return value) to the player's HP.
///
/// Three events allow callers to distinguish the shield's end-states:
///   OnDamageAbsorbed — shield absorbed a hit and is still up.
///   OnShieldBroken   — shield health depleted to 0 (a failure).
///   OnShieldExpired  — duration ran out, undamaged (a clean, successful use).
///
/// Boss counter: Projectile Burst — punishes passive players.
/// </summary>
public class BarrierSkill : ISkill
{
    private readonly BarrierSkillConfig _config;
    private SkillCooldown _cooldown;

    private Animator _animator;

    private float _barrierTimer;

    private static readonly int BarrierTriggerHash = Animator.StringToHash("BarrierTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  HasBarrier       { get; private set; }
    public float BarrierHealth    { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    /// <summary>Fired from AbsorbDamage() when the shield absorbs a hit and stays up.</summary>
    public event System.Action OnDamageAbsorbed;

    /// <summary>Fired from AbsorbDamage() when the absorbed hit depletes the shield to 0.</summary>
    public event System.Action OnShieldBroken;

    /// <summary>Fired from UpdateCooldown() when the duration timer elapses undamaged.</summary>
    public event System.Action OnShieldExpired;

    public BarrierSkill(BarrierSkillConfig config)
    {
        _config   = config;
        _cooldown = new SkillCooldown(config.Cooldown);
    }

    /// <param name="animator">Player Animator — to fire BarrierTrigger.</param>
    public void Init(Animator animator)
    {
        _animator = animator;
    }

    /// <summary>
    /// Activate the barrier. Blocked while cooldown is active or barrier already up.
    /// </summary>
    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || HasBarrier) return;

        _cooldown.Trigger();
        HasBarrier    = true;
        BarrierHealth = _config.BarrierHealth;
        _barrierTimer = _config.BarrierDuration;

        _animator.SetTrigger(BarrierTriggerHash);
    }

    /// <summary>
    /// Called by DamageHandler to absorb an incoming hit into the barrier.
    /// Returns the overflow damage that exceeded remaining barrier health (apply to player HP).
    /// </summary>
    public float AbsorbDamage(float incomingDamage)
    {
        if (!HasBarrier) return incomingDamage;

        float overflow = incomingDamage - BarrierHealth;
        BarrierHealth -= incomingDamage;

        if (BarrierHealth <= 0f)
        {
            BarrierHealth = 0f;
            HasBarrier    = false;
            OnShieldBroken?.Invoke();
        }
        else
        {
            OnDamageAbsorbed?.Invoke();
        }

        return Mathf.Max(0f, overflow);
    }

    public void Reset(Animator animator)
    {
        HasBarrier    = false;
        BarrierHealth = 0f;
        _barrierTimer = 0f;
        _cooldown.Reset();
        animator.ResetTrigger(BarrierTriggerHash);
    }

    /// <summary>
    /// Ticks the cooldown and the barrier duration timer.
    /// Called by PlayerSkills.Update() every frame.
    /// </summary>
    public void UpdateCooldown(float deltaTime)
    {
        _cooldown.Tick(deltaTime);

        if (!HasBarrier) return;

        _barrierTimer -= deltaTime;
        if (_barrierTimer <= 0f)
        {
            HasBarrier    = false;
            BarrierHealth = 0f;
            OnShieldExpired?.Invoke();
        }
    }
}
