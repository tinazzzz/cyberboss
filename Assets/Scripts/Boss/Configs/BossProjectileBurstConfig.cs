using UnityEngine;

/// <summary>
/// Tunable parameters for the Boss Projectile Burst skill.
///
/// The boss fires a spread of projectiles toward the player.
/// Counters the player's Barrier by punishing passive, stationary play.
///
/// Create asset: CyberBoss > Boss > ProjectileBurstConfig
/// </summary>
[CreateAssetMenu(fileName = "BossSkillConfig_ProjectileBurst",
    menuName = "CyberBoss/Boss/ProjectileBurstConfig")]
public class BossProjectileBurstConfig : ScriptableObject
{
    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 8f;

    [Header("Projectiles")]
    [Range(3, 5)]
    [SerializeField] private int   _projectileCount    = 3;
    [SerializeField] private float _spreadAngleDegrees = 30f;
    [SerializeField] private float _projectileSpeed    = 14f;
    [SerializeField] private float _projectileRange    = 22f;
    [SerializeField] private float _projectileDamage   = 12f;

    [Header("Timing")]
    [SerializeField] private float _windupDuration = 1.0f;

    [Header("Impact Feedback")]
    [Tooltip("Shake and CA fire ONCE at volley release, not per-projectile — a light " +
             "quick 'staccato' pulse. Freeze is explicitly disabled: a freeze here " +
             "would undercut the 'rapid/unavoidable' read and feel choppy across " +
             "multiple shots. Red-orange muzzle-flash VFX fires once per shot fired.")]
    [SerializeField] private ImpactEffectProfile _impactProfile;

    public float Cooldown            => _cooldown;
    public int   ProjectileCount     => _projectileCount;
    public float SpreadAngleDegrees  => _spreadAngleDegrees;
    public float ProjectileSpeed     => _projectileSpeed;
    public float ProjectileRange     => _projectileRange;
    public float ProjectileDamage    => _projectileDamage;
    public float WindupDuration      => _windupDuration;

    public ImpactEffectProfile ImpactProfile => _impactProfile;
}
