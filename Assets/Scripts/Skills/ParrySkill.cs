using UnityEngine;

/// <summary>
/// Parry skill implementation.
///
/// Opens a timed window during which incoming hits are reflected.
/// DamageHandler reads IsParryActive to determine whether to
/// reflect damage back to the attacker instead of applying it to the player.
///
/// PlayerController reads PlayerSkills.IsMovementLocked (which delegates to
/// IsParryActive) to suppress horizontal movement during the window.
///
/// Boss counter: Projectile Burst — a sudden multi-projectile volley is much
/// harder to react-parry than a single telegraphed attack. (Shield Phase is no
/// longer a deliberate counter-pick keyed on Parry frequency — see #5 rework in
/// CLAUDE.md; it is now purely reactive, triggered by the player's own attacks.)
/// </summary>
public class ParrySkill : ISkill
{
    private readonly ParrySkillConfig _config;
    private SkillCooldown _cooldown;

    private Animator _animator;

    private float _parryTimer;

    private static readonly int ParryTriggerHash = Animator.StringToHash("ParryTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  IsParryActive    { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    public ParrySkill(ParrySkillConfig config)
    {
        _config   = config;
        _cooldown = new SkillCooldown(config.Cooldown);
    }

    /// <param name="animator">Player Animator — to fire ParryTrigger.</param>
    public void Init(Animator animator)
    {
        _animator = animator;
    }

    /// <summary>
    /// Open the parry window. Blocked while cooldown is active or window already open.
    /// </summary>
    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || IsParryActive) return;

        _cooldown.Trigger();
        IsParryActive = true;
        _parryTimer   = _config.ParryWindowDuration;

        _animator.SetTrigger(ParryTriggerHash);

        // No shake — camera stays still to sell precision. Sharp freeze +
        // light CA tick + quick small FOV snap. The spark-ring VFX recolor lives in
        // PlayerCombatEffects (it's a LineRenderer effect, not a pooled particle burst).
        HitFreezeManager.Instance?.Freeze(_config.ImpactProfile);
        CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);
        CameraImpactEffects.Instance?.TriggerFOVPunch(_config.ImpactProfile);
    }

    /// <summary>Clears parry window state and pending animator trigger. Call on respawn.</summary>
    public void Reset(Animator animator)
    {
        IsParryActive = false;
        _parryTimer   = 0f;
        _cooldown.Reset();
        animator.ResetTrigger(ParryTriggerHash);
    }

    /// <summary>
    /// Ticks the cooldown and the parry window timer.
    /// Called by PlayerSkills.Update() every frame.
    /// </summary>
    public void UpdateCooldown(float deltaTime)
    {
        _cooldown.Tick(deltaTime);

        if (!IsParryActive) return;

        _parryTimer -= deltaTime;
        if (_parryTimer <= 0f)
            IsParryActive = false;
    }
}
