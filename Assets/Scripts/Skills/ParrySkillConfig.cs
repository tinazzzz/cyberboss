using UnityEngine;

/// <summary>
/// Tunable parameters for the Parry skill.
/// Create asset: CyberBoss > Skills > ParryConfig
/// Boss counter: Projectile Burst — a sudden multi-projectile volley is much
/// harder to react-parry than a single telegraphed attack. (Shield Phase is no
/// longer a deliberate counter-pick keyed on Parry frequency — see #5 rework in
/// CLAUDE.md; it is now purely reactive, triggered by the player's own attacks.)
/// </summary>
[CreateAssetMenu(fileName = "SkillConfig_Parry", menuName = "CyberBoss/Skills/ParryConfig")]
public class ParrySkillConfig : ScriptableObject
{
    [SerializeField] private float _cooldown            = 3.0f;
    [SerializeField] private float _parryWindowDuration = 0.4f;

    [Header("Impact Feedback")]
    [Tooltip("No shake (camera stays still to sell precision). A 'Sharp' freeze " +
             "tier — shorter/snappier than Heavy — plus a light CA tick and quick " +
             "small FOV snap. Also recolors the spark-ring VFX (white-gold).")]
    [SerializeField] private ImpactEffectProfile _impactProfile;

    public float Cooldown            => _cooldown;
    public float ParryWindowDuration => _parryWindowDuration;

    public ImpactEffectProfile ImpactProfile => _impactProfile;
}
