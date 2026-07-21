using UnityEngine;

/// <summary>
/// Tunable normalization config for PlayerBehaviorTracker.
/// All threshold and ceiling values live here so designers tune without touching code.
/// Create via: Assets > Create > CyberBoss > Behavior Tracker Config
/// Or run: CyberBoss / Setup Behavior Tracker (creates and assigns automatically).
/// </summary>
[CreateAssetMenu(menuName = "CyberBoss/Behavior Tracker Config", fileName = "BehaviorTrackerConfig")]
public class BehaviorTrackerConfig : ScriptableObject
{
    [SerializeField] private float   _closeRangeThreshold     = 4f;
    [SerializeField] private float   _maxArenaRange            = 20f;
    [SerializeField] private float   _arenaRadius              = 10f;
    [SerializeField] private float[] _maxUsesPerMinutePerSkill = { 10f, 6f, 8f, 6f, 2.5f };
    [SerializeField] private Vector3 _arenaCenterPosition      = Vector3.zero;
    [SerializeField] private float   _sampleInterval           = 0.1f;

    [Tooltip("Normalization ceiling for normal-attack rate, blended into AggressionScore " +
             "alongside proximity. No SkillCooldown gates the normal attack, so this ceiling " +
             "is set higher than the 5 tracked skills' ceilings. The Python training sim must use " +
             "the same value.")]
    [SerializeField] private float   _maxNormalAttacksPerMinute = 30f;

    [Tooltip("Dot-product threshold for classifying a dash as lateral (left/right) vs " +
             "forward/back. Dashes with |dot(dashDir, cameraRight)| < threshold are excluded " +
             "from DodgeDirectionBias. Default 0.3 ≈ cos(72°). " +
             "The Python training sim must use this same value.")]
    [SerializeField] private float   _dashLateralThreshold     = 0.3f;

    /// Distance from boss below which time counts toward AggressionScore.
    public float   CloseRangeThreshold      => _closeRangeThreshold;

    /// Normalization ceiling for AverageEngagementRange (max expected boss distance maps to 1.0).
    public float   MaxArenaRange            => _maxArenaRange;

    /// Arena half-width. PositionalBias reaches 0 when mean distance from center equals this.
    public float   ArenaRadius              => _arenaRadius;

    /// Per-skill uses/min ceiling. Index matches PlayerSkills.SkillIndex constants:
    /// [0]=Dash, [1]=Parry, [2]=RangedBlast, [3]=BurstStrike, [4]=Barrier
    public float[] MaxUsesPerMinutePerSkill => _maxUsesPerMinutePerSkill;

    /// World-space origin used to compute PositionalBias distance.
    public Vector3 ArenaCenterPosition      => _arenaCenterPosition;

    /// Seconds between range and position samples. Lower = more accurate, more CPU cost.
    public float   SampleInterval           => _sampleInterval;

    /// Dot-product threshold for lateral dash classification — see tooltip above.
    /// The Python training sim reads this value via the config to stay in sync.
    public float   DashLateralThreshold     => _dashLateralThreshold;

    /// Normalization ceiling for normal-attack rate — see tooltip above.
    public float   MaxNormalAttacksPerMinute => _maxNormalAttacksPerMinute;
}
