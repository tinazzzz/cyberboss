using UnityEngine;

/// <summary>
/// Tunable parameters for the Burst Strike skill.
///
/// Burst Strike uses an energy-based cooldown instead of a fixed timer.
/// Energy accumulates from player actions; reaching EnergyThreshold makes the
/// skill available. Using it resets energy to zero.
///
/// Create asset: CyberBoss > Skills > BurstStrikeConfig
/// Boss counter: AoE Slam — punishes close aggression.
/// </summary>
[CreateAssetMenu(fileName = "SkillConfig_BurstStrike", menuName = "CyberBoss/Skills/BurstStrikeConfig")]
public class BurstStrikeSkillConfig : ScriptableObject
{
    [Header("Damage")]
    [SerializeField] private float     _blastRadius     = 3.0f;
    [SerializeField] private float     _blastDamage     = 25.0f;
    [SerializeField] private LayerMask _targetLayerMask;

    [Header("Energy")]
    [Tooltip("Total energy required to make Burst Strike available.")]
    [SerializeField] private float _energyThreshold    = 100f;

    [Tooltip("Energy gained each time the player presses the normal attack button.")]
    [SerializeField] private float _energyOnNormalAttack = 8f;

    [Tooltip("Energy gained when Ranged Blast is successfully fired.")]
    [SerializeField] private float _energyOnRangedBlast  = 12f;

    [Tooltip("Energy gained on any successful Dash (i-frames activated).")]
    [SerializeField] private float _energyOnDash          = 5f;

    [Tooltip("Bonus energy on a perfect dodge — a dash that absorbs an incoming hit via i-frames.")]
    [SerializeField] private float _energyOnPerfectDodge  = 20f;

    [Tooltip("Energy gained when a Parry successfully reflects boss damage.")]
    [SerializeField] private float _energyOnParry         = 15f;

    [Tooltip("Energy gained each time the player takes damage (rewards risk-taking).")]
    [SerializeField] private float _energyOnTakeDamage    = 10f;

    [Header("Impact Feedback")]
    [Tooltip("Fired unconditionally on cast — a dedicated Heavy 'gut punch' tier: " +
             "short, high-amplitude, a distinct character from Boss AoE Slam's " +
             "longer 'roll' even though both are nominally heavy. Own Heavy-equivalent " +
             "freeze instance (decoupled from Boss Charge's/AoE Slam's), medium-heavy " +
             "CA, and a new radial shockwave-ring burst VFX (hot pink/magenta).")]
    [SerializeField] private ImpactEffectProfile _impactProfile;

    public float     BlastRadius          => _blastRadius;
    public float     BlastDamage          => _blastDamage;
    public LayerMask TargetLayerMask      => _targetLayerMask;

    public float EnergyThreshold          => _energyThreshold;
    public float EnergyOnNormalAttack     => _energyOnNormalAttack;
    public float EnergyOnRangedBlast      => _energyOnRangedBlast;
    public float EnergyOnDash             => _energyOnDash;
    public float EnergyOnPerfectDodge     => _energyOnPerfectDodge;
    public float EnergyOnParry            => _energyOnParry;
    public float EnergyOnTakeDamage       => _energyOnTakeDamage;

    public ImpactEffectProfile ImpactProfile => _impactProfile;
}
