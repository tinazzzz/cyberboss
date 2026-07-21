using UnityEngine;

/// <summary>
/// Records and normalizes the player's combat behavior into a BehaviorStatVector.
///
/// Attach to the Player GameObject alongside PlayerSkills.
/// GetCurrentVector() returns the live stat vector — called by BossRLAgent before each inference pass.
/// ResetStats() clears all counters — call on player death/respawn.
///
/// Event subscriptions (wired in Start, released in OnDestroy):
///   PlayerSkills.OnSkillExecuted — increments per-skill use counters
///   PlayerSkills.OnDashExecuted  — classifies each dash as left/right for direction bias
///
/// Sampling strategy:
///   Aggression: per-frame delta-time (time accuracy required — spec §AggressionScore)
///   Range/position: every sampleInterval seconds (spec explicitly permits interval sampling)
/// </summary>
[RequireComponent(typeof(PlayerSkills))]
public class PlayerBehaviorTracker : MonoBehaviour
{
    [SerializeField] private BehaviorTrackerConfig _config;

    private PlayerSkills _playerSkills;
    private Transform    _bossTransform;
    private Camera       _mainCamera;

    // Dash direction counts — forward/back dashes are excluded from the bias computation.
    private int _leftDashCount;
    private int _rightDashCount;

    // Usage counts indexed by PlayerSkills.SkillIndex constants (0–4).
    private readonly int[] _skillUseCounts = new int[5];

    // Normal (basic) attack count — distinct from the 5 tracked skills, blended
    // into AggressionScore alongside proximity.
    private int _normalAttackCount;

    private float _elapsedTime;
    private float _timeInCloseRange;

    // Engagement range rolling mean — sum+count avoids List<T> allocation in hot path.
    private float _rangeSampleSum;
    private int   _rangeSampleCount;

    // Positional bias rolling mean.
    private float _positionSampleSum;
    private int   _positionSampleCount;

    private float _sampleTimer;

    // Lateral threshold is now read from _config.DashLateralThreshold (not a const)
    // so the Python training sim can read the authoritative value from the same config asset.

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (_config == null)
            throw new System.InvalidOperationException(
                $"[PlayerBehaviorTracker] BehaviorTrackerConfig not assigned on '{name}'. " +
                "Run CyberBoss/Setup Behavior Tracker to create and assign it.");

        _playerSkills = GetComponent<PlayerSkills>();
        _mainCamera   = Camera.main;

        ValidateConfig();
    }

    private void Start()
    {
        var bossGo = GameObject.FindWithTag("Boss");
        if (bossGo != null)
            _bossTransform = bossGo.transform;
        else
            Debug.LogWarning(
                "[PlayerBehaviorTracker] No GameObject tagged 'Boss' found. " +
                "AggressionScore and AverageEngagementRange will remain at their defaults. " +
                "Run CyberBoss/Setup Boss or tag the boss 'Boss' before entering Play mode.");
    }

    // OnEnable/OnDisable rather than Start/OnDestroy so event subscriptions are
    // correctly re-established when the ML-Agents training env toggles the agent
    // GameObject between episodes.
    private void OnEnable()
    {
        if (_playerSkills != null)
        {
            _playerSkills.OnSkillExecuted        += OnSkillExecuted;
            _playerSkills.OnDashExecuted          += OnDashExecuted;
            _playerSkills.OnNormalAttackExecuted  += OnNormalAttackExecuted;
        }
    }

    private void OnDisable()
    {
        if (_playerSkills != null)
        {
            _playerSkills.OnSkillExecuted        -= OnSkillExecuted;
            _playerSkills.OnDashExecuted          -= OnDashExecuted;
            _playerSkills.OnNormalAttackExecuted  -= OnNormalAttackExecuted;
        }
    }

    // ------------------------------------------------------------------
    // Update — aggression per frame; range/position on timed interval
    // ------------------------------------------------------------------

    private void Update()
    {
        _elapsedTime += Time.deltaTime;

        if (_bossTransform != null)
            TrackAggression();

        _sampleTimer += Time.deltaTime;
        if (_sampleTimer >= _config.SampleInterval)
        {
            _sampleTimer -= _config.SampleInterval;
            TakeSamples();
        }
    }

    private void TrackAggression()
    {
        float dist = Vector3.Distance(transform.position, _bossTransform.position);
        if (dist < _config.CloseRangeThreshold)
            _timeInCloseRange += Time.deltaTime;
    }

    private void TakeSamples()
    {
        if (_bossTransform != null)
        {
            float dist = Vector3.Distance(transform.position, _bossTransform.position);
            _rangeSampleSum += dist;
            _rangeSampleCount++;
        }

        float distFromCenter = Vector3.Distance(
            transform.position, _config.ArenaCenterPosition);
        _positionSampleSum  += distFromCenter;
        _positionSampleCount++;
    }

    // ------------------------------------------------------------------
    // Event callbacks
    // ------------------------------------------------------------------

    private void OnSkillExecuted(int skillIndex)
    {
        if (skillIndex >= 0 && skillIndex < _skillUseCounts.Length)
            _skillUseCounts[skillIndex]++;
    }

    private void OnNormalAttackExecuted()
    {
        _normalAttackCount++;
    }

    private void OnDashExecuted(Vector3 dashDirection)
    {
        // dashDirection is camera-relative world space (from PlayerSkills.ComputeWorldMoveDirection).
        // Classify using the camera's right axis so the same physical dodge (e.g., camera-left)
        // always maps to the same side regardless of which way the character is currently facing.
        if (_mainCamera == null)
            Debug.LogWarning("[PlayerBehaviorTracker] Camera.main is null — dash classified " +
                "using world right. DodgeDirectionBias will not reflect screen-space direction.");

        Vector3 cameraRight = _mainCamera != null
            ? Vector3.ProjectOnPlane(_mainCamera.transform.right, Vector3.up).normalized
            : Vector3.right;

        float threshold = _config.DashLateralThreshold;
        float rightDot  = Vector3.Dot(dashDirection, cameraRight);
        if (rightDot > threshold)
            _rightDashCount++;
        else if (rightDot < -threshold)
            _leftDashCount++;
        // Dashes near the forward/back camera axis are positional repositions, not lateral
        // dodges — excluding them keeps DodgeDirectionBias meaningful to the RL policy.
    }

    // ------------------------------------------------------------------
    // Public API — BossRLAgent contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the current normalized stat vector as a value-type struct (no allocation).
    /// Called by BossRLAgent before each skill-selection inference pass.
    /// </summary>
    public BehaviorStatVector GetCurrentVector()
    {
        float elapsedMinutes = _elapsedTime / 60f;

        return new BehaviorStatVector
        {
            DodgeDirectionBias     = ComputeDodgeDirectionBias(),
            SkillUsageFrequency0   = ComputeSkillFrequency(0, elapsedMinutes),
            SkillUsageFrequency1   = ComputeSkillFrequency(1, elapsedMinutes),
            SkillUsageFrequency2   = ComputeSkillFrequency(2, elapsedMinutes),
            SkillUsageFrequency3   = ComputeSkillFrequency(3, elapsedMinutes),
            SkillUsageFrequency4   = ComputeSkillFrequency(4, elapsedMinutes),
            AggressionScore        = ComputeAggressionScore(),
            AverageEngagementRange = ComputeAverageEngagementRange(),
            PositionalBias         = ComputePositionalBias(),
        };
    }

    /// <summary>
    /// Number of lateral dashes recorded so far (left + right; forward/back dashes
    /// are excluded, matching DodgeDirectionBias). Not part of the locked
    /// BehaviorStatVector schema — this is a side channel for consumers that need
    /// to judge how reliable the bias ratio is, e.g. BossTeleportStrikeSkill scales
    /// its predictive offset down when this count is low, since a single early
    /// dash reads as bias=0.0 or 1.0 despite proving nothing statistically.
    /// </summary>
    public int LateralDashSampleCount => _leftDashCount + _rightDashCount;

    /// <summary>
    /// Overrides the boss transform reference. Call this from the ML-Agents training
    /// environment's OnEpisodeBegin() when the boss is instantiated per-episode rather
    /// than pre-placed in the scene, since Start() only searches for it once.
    /// </summary>
    public void SetBossTransform(Transform bossTransform)
    {
        _bossTransform = bossTransform;
    }

    /// <summary>
    /// Clears all tracked state. Call on player death/respawn before the next fight begins.
    /// In ML-Agents training: call from OnEpisodeBegin() before each new episode.
    /// </summary>
    public void ResetStats()
    {
        _leftDashCount  = 0;
        _rightDashCount = 0;
        _normalAttackCount = 0;

        for (int i = 0; i < _skillUseCounts.Length; i++)
            _skillUseCounts[i] = 0;

        _elapsedTime         = 0f;
        _timeInCloseRange    = 0f;
        _rangeSampleSum      = 0f;
        _rangeSampleCount    = 0;
        _positionSampleSum   = 0f;
        _positionSampleCount = 0;
        _sampleTimer         = 0f;
    }

    // ------------------------------------------------------------------
    // Normalization helpers
    // ------------------------------------------------------------------

    private float ComputeDodgeDirectionBias()
    {
        int total = _leftDashCount + _rightDashCount;
        return total > 0
            ? (float)_rightDashCount / total
            : 0.5f; // Neutral before any lateral dashes are recorded.
    }

    private float ComputeSkillFrequency(int skillIndex, float elapsedMinutes)
    {
        if (elapsedMinutes <= 0f) return 0f;

        float[] ceilings = _config.MaxUsesPerMinutePerSkill;
        if (ceilings == null || skillIndex >= ceilings.Length) return 0f;

        float ceiling = ceilings[skillIndex];
        if (ceiling <= 0f) return 0f;

        float usesPerMinute = _skillUseCounts[skillIndex] / elapsedMinutes;
        return Mathf.Clamp01(usesPerMinute / ceiling);
    }

    /// <summary>
    /// Blends proximity (time spent within CloseRangeThreshold) with normal-attack
    /// rate — a player mashing melee attacks is a stronger pressure signal than
    /// proximity alone, since standing close without ever swinging isn't aggression.
    /// Proximity weighted higher (0.6) as the more direct "are you in the fight" signal.
    /// </summary>
    private float ComputeAggressionScore()
    {
        if (_elapsedTime <= 0f) return 0f;

        float proximityFraction = Mathf.Clamp01(_timeInCloseRange / _elapsedTime);

        float elapsedMinutes = _elapsedTime / 60f;
        float attackRateNormalized = _config.MaxNormalAttacksPerMinute > 0f
            ? Mathf.Clamp01((_normalAttackCount / elapsedMinutes) / _config.MaxNormalAttacksPerMinute)
            : 0f;

        return Mathf.Clamp01(proximityFraction * 0.6f + attackRateNormalized * 0.4f);
    }

    private float ComputeAverageEngagementRange()
    {
        if (_rangeSampleCount == 0 || _config.MaxArenaRange <= 0f)
            return 0.5f; // Neutral default before boss is tracked.

        float meanDist = _rangeSampleSum / _rangeSampleCount;
        return Mathf.Clamp01(meanDist / _config.MaxArenaRange);
    }

    private float ComputePositionalBias()
    {
        if (_positionSampleCount == 0 || _config.ArenaRadius <= 0f)
            return 0.5f; // Neutral default before position data accumulates.

        float meanDistFromCenter = _positionSampleSum / _positionSampleCount;
        return Mathf.Clamp01(1f - (meanDistFromCenter / _config.ArenaRadius));
    }

    private void ValidateConfig()
    {
        if (_config.SampleInterval <= 0f)
            throw new System.InvalidOperationException(
                $"[PlayerBehaviorTracker] BehaviorTrackerConfig.SampleInterval is {_config.SampleInterval} " +
                "on '{name}'. Must be > 0. TakeSamples() would run every frame and _sampleTimer " +
                "would grow without bound.");

        float[] ceilings = _config.MaxUsesPerMinutePerSkill;
        if (ceilings == null || ceilings.Length < 5)
            throw new System.InvalidOperationException(
                $"[PlayerBehaviorTracker] MaxUsesPerMinutePerSkill has " +
                $"{(ceilings == null ? 0 : ceilings.Length)} entries but exactly 5 are required " +
                "(Dash/Parry/RangedBlast/BurstStrike/Barrier). " +
                "Set the array length to 5 in BehaviorTrackerConfig.");
    }
}
