using UnityEngine;

/// <summary>
/// Burst Strike skill implementation.
///
/// Deals damage to all IDamageable targets within a sphere radius around the player.
/// Unlike the other four skills, readiness is energy-based rather than time-based.
/// Energy accumulates from normal attacks, ranged blast, dashes, perfect dodges,
/// parries, and taking damage. Using the skill resets energy to zero.
///
/// Notify* methods are called by PlayerSkills when the corresponding action occurs.
/// Each routes its energy amount from BurstStrikeSkillConfig into EnergyCooldown.
///
/// Uses Physics.OverlapSphereNonAlloc with a pre-allocated buffer to avoid
/// per-Execute heap allocation (WebGL hot-path safe).
///
/// Boss counter: AoE Slam — punishes close aggression.
/// </summary>
public class BurstStrikeSkill : ISkill
{
    private readonly BurstStrikeSkillConfig _config;
    private EnergyCooldown _cooldown;

    private Transform _playerTransform;
    private Animator  _animator;

    // Pre-allocated buffer — zero allocation in Execute().
    private readonly Collider[] _hitBuffer = new Collider[16];

    // Ground-ring shockwave duration — a particle burst alone reads as
    // "dots", not a ring, so this is a separate expanding-disc technique.
    private const float GroundRingDuration = 0.25f;

    private static readonly int StrikeTriggerHash = Animator.StringToHash("StrikeTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public float CooldownProgress => _cooldown.NormalizedProgress;

    public BurstStrikeSkill(BurstStrikeSkillConfig config)
    {
        _config   = config;
        _cooldown = new EnergyCooldown(config.EnergyThreshold);
    }

    /// <param name="playerTransform">Centre of the overlap sphere.</param>
    /// <param name="animator">Player Animator — to fire StrikeTrigger.</param>
    public void Init(Transform playerTransform, Animator animator)
    {
        _playerTransform = playerTransform;
        _animator        = animator;

        if (_config.TargetLayerMask == 0)
            Debug.LogWarning("[BurstStrikeSkill] TargetLayerMask is Nothing (0). " +
                "Set it to your Boss/Enemy layer in SkillConfig_BurstStrike — " +
                "the skill will animate but hit nothing until this is fixed.");
    }

    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady) return;

        _cooldown.Trigger();
        _animator.SetTrigger(StrikeTriggerHash);

        // Fires unconditionally on cast (not gated on hit count) — Burst
        // Strike is the heaviest single-target player commitment and should always
        // read as impactful, regardless of whether it connects.
        ScreenShakeManager.Instance?.Shake(_config.ImpactProfile);
        HitFreezeManager.Instance?.Freeze(_config.ImpactProfile);
        CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);
        CombatVFXManager.Instance?.SpawnImpact(_playerTransform.position, "BurstStrike", _config.ImpactProfile);

        // Expanding/fading ground ring at BlastRadius — the actual
        // "radial shockwave ring" read a particle burst alone can't produce.
        if (_config.ImpactProfile != null)
            CombatVFXManager.Instance?.SpawnGroundRing(
                _playerTransform.position, _config.ImpactProfile.VfxColor, _config.BlastRadius, GroundRingDuration);

        int hitCount = Physics.OverlapSphereNonAlloc(
            _playerTransform.position,
            _config.BlastRadius,
            _hitBuffer,
            _config.TargetLayerMask);

        for (int i = 0; i < hitCount; i++)
        {
            if (_hitBuffer[i].gameObject == user) continue;
            if (_hitBuffer[i].transform.IsChildOf(user.transform)) continue;

            IDamageable target = _hitBuffer[i].GetComponent<IDamageable>()
                ?? _hitBuffer[i].GetComponentInParent<IDamageable>();
            var tc = target as Component;
            if (tc != null)
                target.TakeDamage(_config.BlastDamage);
        }
    }

    public void UpdateCooldown(float deltaTime) => _cooldown.Tick(deltaTime);

    public void Reset(Animator animator)
    {
        _cooldown.Reset();
        animator.ResetTrigger(StrikeTriggerHash);
    }

    // ------------------------------------------------------------------
    // Energy sources — called by PlayerSkills when each action occurs
    // ------------------------------------------------------------------

    public void NotifyNormalAttack() => _cooldown.AddEnergy(_config.EnergyOnNormalAttack);
    public void NotifyRangedBlast()  => _cooldown.AddEnergy(_config.EnergyOnRangedBlast);
    public void NotifyDash()         => _cooldown.AddEnergy(_config.EnergyOnDash);
    public void NotifyPerfectDodge() => _cooldown.AddEnergy(_config.EnergyOnPerfectDodge);
    public void NotifyParry()        => _cooldown.AddEnergy(_config.EnergyOnParry);
    public void NotifyTakeDamage()   => _cooldown.AddEnergy(_config.EnergyOnTakeDamage);
}
