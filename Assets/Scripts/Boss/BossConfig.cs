using UnityEngine;

/// <summary>
/// Boss-level tunable parameters.
///
/// Used only by BossController (health, stagger thresholds, skill loop timing).
/// Per-skill cooldowns and damage values live in individual BossSkill*Config
/// ScriptableObjects — assign those to BossSkills, not here.
///
/// RL policy parameters belong in the ML-Agents config and SentisInferenceManager,
/// not in this runtime config.
/// </summary>
[CreateAssetMenu(fileName = "BossConfig", menuName = "CyberBoss/BossConfig")]
public class BossConfig : ScriptableObject
{
    [Header("Health")]
    [SerializeField] private float _maxHealth = 500f;

    [Header("Stagger")]
    [SerializeField] private float _staggerThreshold = 20f;
    [SerializeField] private float _staggerDuration   = 1.0f;

    [Header("Skill Loop")]
    [SerializeField] private float _skillInterval = 3.0f;

    [Header("Approach Movement")]
    [Tooltip("Walk speed used between skills to close distance to the player.")]
    [SerializeField] private float _approachSpeed = 4f;
    [Tooltip("Boss stops walking when it is within this world-unit distance of the player.")]
    [SerializeField] private float _approachStopDistance = 5f;

    // ------------------------------------------------------------------
    // Public properties
    // ------------------------------------------------------------------

    /// <summary>Full health at scene start. Passed to HealthSystem.Initialize().</summary>
    public float MaxHealth => _maxHealth;

    /// <summary>Minimum single-hit damage required to trigger stagger.</summary>
    public float StaggerThreshold => _staggerThreshold;

    /// <summary>Duration the boss is locked in stagger state.</summary>
    public float StaggerDuration => _staggerDuration;

    /// <summary>Delay between the end of one skill and selection of the next.</summary>
    public float SkillInterval => _skillInterval;

    /// <summary>World-units per second the boss walks toward the player between skills.</summary>
    public float ApproachSpeed => _approachSpeed;

    /// <summary>Boss stops walking and idles when closer than this to the player.</summary>
    public float ApproachStopDistance => _approachStopDistance;
}
