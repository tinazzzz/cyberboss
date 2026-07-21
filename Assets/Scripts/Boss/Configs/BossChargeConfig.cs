using UnityEngine;

/// <summary>
/// Tunable parameters for the Boss Charge skill.
///
/// The boss dashes toward the player, dealing damage on contact.
/// Counters the player's Ranged Blast by closing distance aggressively.
///
/// Create asset: CyberBoss > Boss > ChargeConfig
/// </summary>
[CreateAssetMenu(fileName = "BossSkillConfig_Charge", menuName = "CyberBoss/Boss/ChargeConfig")]
public class BossChargeConfig : ScriptableObject
{
    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 7f;

    [Header("Movement")]
    [SerializeField] private float _chargeSpeed    = 20f;
    [SerializeField] private float _maxRange       = 8f;
    [SerializeField] private float _contactRadius  = 1.5f;

    [Header("Damage")]
    [SerializeField] private float _damage = 20f;

    [Header("Windup")]
    [SerializeField] private float _windupDuration = 1.2f;

    [Header("VFX — scale pulse during charge")]
    [SerializeField] private float _chargeScaleMultiplier = 1.15f;

    [Header("Impact Feedback")]
    [Tooltip("Fired on contact — a new short, high-frequency, fast-decay 'jolt' shake " +
             "(this skill previously had freeze+CA+FOV but no shake at all — the " +
             "clearest gap in the pre-Part-10 effects layer). Own Heavy-equivalent " +
             "freeze and CA/FOV instances. Orange impact burst at contact plus a " +
             "continuous dust trail during the run phase (same VfxColor, own local " +
             "emission-rate tuning — a continuous emitter isn't expressible in this " +
             "one-shot-burst profile schema).")]
    [SerializeField] private ImpactEffectProfile _impactProfile;

    public float Cooldown              => _cooldown;
    public float ChargeSpeed           => _chargeSpeed;
    public float MaxRange              => _maxRange;
    public float ContactRadius         => _contactRadius;
    public float Damage                => _damage;
    public float WindupDuration        => _windupDuration;
    public float ChargeScaleMultiplier => _chargeScaleMultiplier;

    public ImpactEffectProfile ImpactProfile => _impactProfile;
}
