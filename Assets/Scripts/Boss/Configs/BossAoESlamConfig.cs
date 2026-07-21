using UnityEngine;

/// <summary>
/// Tunable parameters for the Boss AoE Slam skill.
///
/// The boss winds up and slams the ground, dealing AoE damage within blast radius.
/// Counters the player's Burst Strike by punishing close-range aggression.
///
/// Set TargetLayerMask to the Player layer so the overlap finds only the player's
/// colliders. Leave at Nothing (0) only for testing — a warning will fire and
/// Everything will be used as a fallback.
///
/// Create asset: CyberBoss > Boss > AoESlamConfig
/// </summary>
[CreateAssetMenu(fileName = "BossSkillConfig_AoeSlam", menuName = "CyberBoss/Boss/AoESlamConfig")]
public class BossAoESlamConfig : ScriptableObject
{
    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 9f;

    [Header("AoE")]
    [SerializeField] private float     _blastRadius        = 4f;
    [SerializeField] private float     _minEngagementRange = 6f;
    [SerializeField] private float     _damage             = 25f;
    [SerializeField] private LayerMask _targetLayerMask    = ~0; // Everything — set to Player layer for best performance

    [Header("Timing")]
    [SerializeField] private float _windupDuration = 1.5f;
    [Tooltip("Seconds between the animation trigger and the actual damage call. " +
             "Set this to match the animation's impact frame so visuals and damage land together. " +
             "Also gives the player a reaction window to dash after the red indicator disappears.")]
    [SerializeField] private float _slamImpactDelay = 0.3f;

    [Header("Impact Feedback")]
    [Tooltip("Fired at actual ground impact (DamagePlayersInRange()) — previously " +
             "the telegraph disc was destroyed before impact, leaving zero particles " +
             "at the real payoff moment. Dedicated longer 'roll' shake tier (distinct " +
             "from Boss Charge's short jolt, though migrated to the same profile-based " +
             "API), own Heavy-equivalent freeze/CA/FOV instance, and an expanding " +
             "ground-crack shockwave ring VFX (red-orange).")]
    [SerializeField] private ImpactEffectProfile _impactProfile;

    public float     Cooldown             => _cooldown;
    public float     BlastRadius          => _blastRadius;
    public float     MinEngagementRange   => _minEngagementRange;
    public float     Damage               => _damage;
    public LayerMask TargetLayerMask      => _targetLayerMask;
    public float     WindupDuration       => _windupDuration;
    public float     SlamImpactDelay      => _slamImpactDelay;

    public ImpactEffectProfile ImpactProfile => _impactProfile;
}
