using UnityEngine;

/// <summary>
/// Tunable parameters for the Ranged Blast skill.
/// Create asset: CyberBoss > Skills > RangedBlastConfig
/// Boss counter: Charge — closes distance.
/// </summary>
[CreateAssetMenu(fileName = "SkillConfig_RangedBlast", menuName = "CyberBoss/Skills/RangedBlastConfig")]
public class RangedBlastSkillConfig : ScriptableObject
{
    [Header("Projectile")]
    [SerializeField] private float     _projectileSpeed  = 18.0f;
    [SerializeField] private float     _projectileRange  = 20.0f;
    [SerializeField] private float     _projectileDamage = 15.0f;
    [SerializeField] private LayerMask _floorLayerMask;

    [Header("Charges")]
    [Tooltip("Maximum shots that can be stored at once.")]
    [SerializeField] private int   _maxCharges       = 3;
    [Tooltip("Seconds to restore one charge.")]
    [SerializeField] private float _rechargeDuration = 2.0f;

    [Header("Impact Feedback")]
    [Tooltip("Fired at hit-confirm (ProjectileBehavior's collision), NOT at launch — " +
             "PlayerSkills.OnSkillExecuted fires at launch, before projectile travel " +
             "time elapses, and this profile is tuned for the landing moment. Light " +
             "'recoil kick' shake (must feel cheap to spam, the highest-frequency " +
             "player skill), no freeze, amber impact spark in its own pool.")]
    [SerializeField] private ImpactEffectProfile _impactProfile;

    public float     ProjectileSpeed   => _projectileSpeed;
    public float     ProjectileRange   => _projectileRange;
    public float     ProjectileDamage  => _projectileDamage;
    public LayerMask FloorLayerMask    => _floorLayerMask;
    public int       MaxCharges        => _maxCharges;
    public float     RechargeDuration  => _rechargeDuration;

    public ImpactEffectProfile ImpactProfile => _impactProfile;
}
