using UnityEngine;

/// <summary>
/// Tunable weights for the reactive Shield Phase trigger.
///
/// Create asset: CyberBoss > Boss > ShieldReactiveTriggerConfig
/// </summary>
[CreateAssetMenu(fileName = "BossSkillConfig_ShieldReactiveTrigger",
    menuName = "CyberBoss/Boss/ShieldReactiveTriggerConfig")]
public class BossShieldReactiveTriggerConfig : ScriptableObject
{
    [Tooltip("Weight on RangedBlast frequency (BehaviorStatVector.SkillUsageFrequency2) " +
             "in the reactive-shield trigger probability roll. Ranged Blast is treated as " +
             "the 'high damage' source worth shielding against.")]
    [SerializeField] private float _rangedBlastFrequencyWeight = 0.6f;

    [Tooltip("Weight on AggressionScore in the reactive-shield trigger probability roll — " +
             "sustained close-range pressure also warrants shielding periodically.")]
    [SerializeField] private float _aggressionWeight = 0.4f;

    public float RangedBlastFrequencyWeight => _rangedBlastFrequencyWeight;
    public float AggressionWeight            => _aggressionWeight;
}
