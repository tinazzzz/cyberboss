using UnityEngine;

/// <summary>
/// Tunable parameters for the Barrier skill.
/// Create asset: CyberBoss > Skills > BarrierConfig
/// Boss counter: Projectile Burst — punishes passive players.
/// </summary>
[CreateAssetMenu(fileName = "SkillConfig_Barrier", menuName = "CyberBoss/Skills/BarrierConfig")]
public class BarrierSkillConfig : ScriptableObject
{
    [SerializeField] private float _cooldown        = 8.0f;
    [SerializeField] private float _barrierHealth   = 50.0f;
    [SerializeField] private float _barrierDuration = 5.0f;

    [Header("Impact Feedback (3 distinct end-states)")]
    [Tooltip("Fired from BarrierSkill.OnDamageAbsorbed — shield still up. Small " +
             "localized deflect spark + light CA tick; no shake.")]
    [SerializeField] private ImpactEffectProfile _absorbProfile;

    [Tooltip("Fired from BarrierSkill.OnShieldBroken — health depleted to 0. Light " +
             "shake + a wider 'shatter' burst; reads as a failure.")]
    [SerializeField] private ImpactEffectProfile _brokenProfile;

    [Tooltip("Fired from BarrierSkill.OnShieldExpired — duration ran out undamaged. " +
             "No shake, no flash, no burst; reads as a clean, successful use.")]
    [SerializeField] private ImpactEffectProfile _expiredProfile;

    public float Cooldown        => _cooldown;
    public float BarrierHealth   => _barrierHealth;
    public float BarrierDuration => _barrierDuration;

    public ImpactEffectProfile AbsorbProfile  => _absorbProfile;
    public ImpactEffectProfile BrokenProfile  => _brokenProfile;
    public ImpactEffectProfile ExpiredProfile => _expiredProfile;
}
