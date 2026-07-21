using UnityEngine;

/// <summary>
/// Collects boss observations, drives skill selection, and manages episode
/// boundaries for the RL-trained boss policy.
///
/// Attach to the same GameObject as BossController and BossSkills.
/// BossController reads <see cref="SelectSkillIndex"/> to replace
/// its random selection with the policy output.
///
/// When <c>_useRLPolicy</c> is false: SelectSkillIndex() returns a random ready skill.
/// When <c>_useRLPolicy</c> is true: wire <c>_sentisManager</c> (SentisInferenceManager)
/// in the Inspector to enable ONNX inference.
///
/// ONNX observation layout — LOCKED, matches BehaviorStatVector field order:
///   float[0]  DodgeDirectionBias       0=pure left, 0.5=neutral, 1=pure right
///   float[1]  SkillUsageFrequency0     Dash uses/min ÷ 10
///   float[2]  SkillUsageFrequency1     Parry uses/min ÷ 6
///   float[3]  SkillUsageFrequency2     RangedBlast uses/min ÷ 8
///   float[4]  SkillUsageFrequency3     BurstStrike uses/min ÷ 6
///   float[5]  SkillUsageFrequency4     Barrier uses/min ÷ 4
///   float[6]  AggressionScore          0.6×(timeInCloseRange/elapsedTime) + 0.4×(normalAttackRate/ceiling)
///   float[7]  AverageEngagementRange   meanDistToBoss / maxArenaRange
///   float[8]  PositionalBias           1 − (meanDistFromCenter / arenaRadius)
///
/// ONNX action mapping (4 actions — ShieldPhase removed as of the #5 rework,
/// now a reactive skill triggered by BossShieldReactiveTrigger, never RL-picked):
///   0 = Charge            counters ranged play + burst-strike spam
///   1 = ProjectileBurst   counters barrier/passive turtling + parry-spam
///   2 = AoESlam           counters close-range aggression
///   3 = TeleportStrike    counters dash-heavy play + general distance-keeping
///
/// These indices must match BossSkills.SelectableSkills index order.
/// </summary>
[RequireComponent(typeof(BossSkills))]
public class BossRLAgent : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------

    [Tooltip("Enable the Sentis RL policy for skill selection. " +
             "False = random fallback. " +
             "Set true after wiring SentisInferenceManager.")]
    [SerializeField] private bool _useRLPolicy = false;

    [SerializeField] private SentisInferenceManager _sentisManager;

    [Tooltip("Log every RL skill decision to the Console with the live observation " +
             "vector. Enable while play-testing to confirm the boss reacts to your " +
             "play style in real time.")]
    [SerializeField] private bool _logDecisions = false;

    // ------------------------------------------------------------------
    // Private fields
    // ------------------------------------------------------------------

    private BossSkills            _bossSkills;
    private PlayerBehaviorTracker _behaviorTracker;

    // Pre-allocated observation buffer — reused every SelectSkillIndex call
    // to avoid per-decision heap allocation.
    private readonly float[] _obsBuffer = new float[9];

    // ShieldPhase excluded — as of the #5 rework it's a reactive skill
    // (BossShieldReactiveTrigger), never an RL-selectable action.
    private static readonly string[] _skillNames =
        { "Charge", "ProjectileBurst", "AoESlam", "TeleportStrike" };

    // ------------------------------------------------------------------
    // RL policy contract
    // ------------------------------------------------------------------

    /// <summary>
    /// True when the Sentis RL policy is active.
    /// BossController reads this to decide whether to call SelectSkillIndex()
    /// or fall back to scripted random selection.
    /// </summary>
    public bool IsRLActive => _useRLPolicy && _sentisManager != null;

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        _bossSkills = GetComponent<BossSkills>();

        if (_bossSkills == null)
            throw new System.InvalidOperationException(
                $"[BossRLAgent] BossSkills not found on '{name}'. " +
                "Attach BossRLAgent to the same GameObject as BossSkills.");

        CachePlayerBehaviorTracker();
    }

    private void Start()
    {
        // Awake() may run before the Player GameObject is active (e.g. when the
        // player spawns in Start or via a prefab instantiation that completes after
        // the boss Awake). Retry once here so the first fight has a valid tracker.
        if (_behaviorTracker == null)
            CachePlayerBehaviorTracker();

        // Silent-fallback guard (devil's-advocate finding): if _useRLPolicy is
        // ticked but _sentisManager was never wired, SelectSkillIndex() falls
        // straight to the random picker with zero indication anywhere in the
        // Console — a boss that looks "reactive" purely because the random
        // picker draws from the same 4-skill pool the RL policy would. Log
        // this loudly and once, since it otherwise cannot be told apart from
        // genuine RL behaviour by watching the boss play.
        if (_useRLPolicy && _sentisManager == null)
            Debug.LogError(
                $"[BossRLAgent] 'Use RL Policy' is enabled on '{name}' but " +
                "_sentisManager is not assigned — every skill decision this fight " +
                "will silently use the random picker instead of the trained " +
                "policy. Run CyberBoss/Setup RL Boss or assign SentisInferenceManager " +
                "in the Inspector.");
    }

    // ------------------------------------------------------------------
    // Episode boundary
    // ------------------------------------------------------------------

    /// <summary>
    /// Call this at the start of each fight episode (boss respawn / new round).
    ///
    /// IMPORTANT: <see cref="PlayerBehaviorTracker.ResetStats"/> MUST be called
    /// here. Stat accumulators (elapsed time, skill counts, distance sums)
    /// persist across episodes if not cleared. Without this call, the second
    /// episode's observation vector will be contaminated by the first episode's
    /// data, and the policy will receive incorrect inputs.
    ///
    /// Call from respawn/episode setup:
    ///   _bossRLAgent.OnEpisodeBegin();
    /// </summary>
    public void OnEpisodeBegin()
    {
        // CRITICAL: Clear all PlayerBehaviorTracker accumulators before the
        // new episode begins. Stats from the previous fight must not bleed
        // into the observation vector for the next episode.
        if (_behaviorTracker != null)
            _behaviorTracker.ResetStats();
    }

    // ------------------------------------------------------------------
    // Skill selection — called by BossController
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the skill index (0–3) the boss should execute next.
    ///
    /// Cooldown contract: the training env silently substitutes the best
    /// genuine ready counter whenever the policy picks a skill on cooldown.
    /// This method mirrors that contract via <see cref="ApplyCooldownSubstitution"/>.
    /// Do NOT skip this — calling Execute() on a cooling-down skill
    /// is a silent no-op that wastes the boss turn.
    ///
    /// Random path (when _useRLPolicy is false): returns a random ready skill index.
    ///
    /// RL path (when _useRLPolicy is true, _sentisManager assigned):
    ///   1. float[] obs = CollectObservations();
    ///   2. int policyChoice = _sentisManager.InferSkillIndex(obs);
    ///      // InferSkillIndex signature: int InferSkillIndex(float[] obs)
    ///      // Internally: argmax(model.forward(obs_0 tensor))
    ///   3. return ApplyCooldownSubstitution(policyChoice, obs);
    /// </summary>
    public int SelectSkillIndex()
    {
        if (_useRLPolicy && _sentisManager != null)
        {
            float[] obs          = CollectObservations();
            int     policyChoice = _sentisManager.InferSkillIndex(obs);
            int     finalChoice  = ApplyCooldownSubstitution(policyChoice, obs);

            if (_logDecisions)
                LogDecision(obs, policyChoice, finalChoice);

            return finalChoice;
        }

        return SelectRandomReadySkillIndex();
    }

    /// <summary>
    /// Prints the observation vector and resulting skill choice so a play-tester
    /// can watch the boss's reaction to their behaviour in real time. Enabled via
    /// the <see cref="_logDecisions"/> Inspector toggle — no-op otherwise.
    /// </summary>
    private void LogDecision(float[] obs, int policyChoice, int finalChoice)
    {
        string substitutionNote = finalChoice != policyChoice
            ? $" (subbed from {_skillNames[policyChoice]}, on cooldown)"
            : string.Empty;

        Debug.Log(
            $"[BossRLAgent] dodge:{obs[0]:F2} dash:{obs[1]:F2} parry:{obs[2]:F2} " +
            $"ranged:{obs[3]:F2} burst:{obs[4]:F2} barrier:{obs[5]:F2} " +
            $"aggro:{obs[6]:F2} range:{obs[7]:F2} posBias:{obs[8]:F2} " +
            $"-> {_skillNames[finalChoice]}{substitutionNote}");
    }

    /// <summary>
    /// Softmax temperature for cooldown-substitution sampling — must match
    /// SUBSTITUTION_SOFTMAX_TEMPERATURE in cyberboss_env.py exactly. Lower =
    /// sharper (closer to always picking the single best counter); higher =
    /// closer to uniform random among ready skills.
    /// </summary>
    private const float SubstitutionSoftmaxTemperature = 0.2f;

    // Reused every ApplyCooldownSubstitution call to avoid a per-decision
    // heap allocation — sized to SelectableSkills.Count (always 4).
    private readonly float[] _substitutionWeights = new float[4];

    /// <summary>
    /// Mirrors cyberboss_env.py's step()/_best_ready_skill() cooldown substitution
    /// exactly: if <paramref name="policyChoice"/> is on cooldown, samples among
    /// the ready skills with probability weighted by counter-play effectiveness
    /// against <paramref name="obs"/> — a softmax over
    /// <see cref="BossCounterEffectiveness.Compute"/> (a C# port of
    /// _compute_effectiveness() in cyberboss_env.py), not a strict argmax, and not
    /// raw remaining cooldown time.
    ///
    /// A strict argmax was tried first and made substitution fully deterministic
    /// for any player whose behaviour stats are roughly stable within a fight —
    /// most real fights, since these stats are slow-moving running averages. The
    /// boss locked onto its raw top pick plus one fixed "runner-up" substitute for
    /// the whole fight, with the 3rd/4th skill only firing if both of those
    /// happened to be on cooldown at once — reported directly by playtesting.
    /// Softmax sampling keeps better counters more likely without making them a
    /// guarantee, so substitution still varies across a fight even against a
    /// completely steady playstyle.
    ///
    /// Falls back to argmin(remaining_seconds) only if no skill is currently
    /// ready (rare — requires all 4 simultaneously mid-cooldown), matching
    /// _best_ready_skill()'s own fallback branch, so a skill still always
    /// fires this decision — the policy was trained assuming a skill fires
    /// every step, never skip execution.
    /// </summary>
    public int ApplyCooldownSubstitution(int policyChoice, float[] obs)
    {
        var skills    = _bossSkills.SelectableSkills;
        var durations = _bossSkills.SelectableSkillCooldownDurations;

        if (policyChoice >= 0 && policyChoice < skills.Count &&
            skills[policyChoice].IsReady)
            return policyChoice;

        float weightSum = 0f;
        for (int i = 0; i < skills.Count; i++)
        {
            if (!skills[i].IsReady)
            {
                _substitutionWeights[i] = 0f;
                continue;
            }
            float eff    = BossCounterEffectiveness.Compute(i, obs);
            float weight = Mathf.Exp(eff / SubstitutionSoftmaxTemperature);
            _substitutionWeights[i] = weight;
            weightSum += weight;
        }

        if (weightSum > 0f)
        {
            float roll  = Random.value * weightSum;
            float accum = 0f;
            for (int i = 0; i < skills.Count; i++)
            {
                if (_substitutionWeights[i] <= 0f) continue;
                accum += _substitutionWeights[i];
                if (roll <= accum)
                    return i;
            }
            // Floating-point fallback — should be unreachable, but guarantees a
            // valid return if rounding leaves `roll` fractionally past `weightSum`.
            for (int i = skills.Count - 1; i >= 0; i--)
                if (_substitutionWeights[i] > 0f) return i;
        }

        // Fallback: no skill is currently ready. Pick whichever has the least
        // remaining time so a skill still fires this decision.
        int   bestIndex     = 0;
        float bestRemaining = float.MaxValue;
        for (int i = 0; i < skills.Count; i++)
        {
            float remaining = (1f - skills[i].CooldownProgress) * durations[i];
            if (remaining < bestRemaining)
            {
                bestRemaining = remaining;
                bestIndex     = i;
            }
        }
        return bestIndex;
    }

    // ------------------------------------------------------------------
    // Observation collection — ONNX model input contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Fills and returns the pre-allocated 9-float observation buffer in
    /// the exact order expected by the ONNX model input tensor "obs_0".
    ///
    /// The returned array is the shared <c>_obsBuffer</c> — do not cache
    /// the reference across frames. Copy it if you need a snapshot.
    ///
    /// Called by SelectSkillIndex() before each inference pass.
    /// </summary>
    public float[] CollectObservations()
    {
        BehaviorStatVector stats = _behaviorTracker != null
            ? _behaviorTracker.GetCurrentVector()
            : DefaultStats();

        _obsBuffer[0] = stats.DodgeDirectionBias;
        _obsBuffer[1] = stats.SkillUsageFrequency0;
        _obsBuffer[2] = stats.SkillUsageFrequency1;
        _obsBuffer[3] = stats.SkillUsageFrequency2;
        _obsBuffer[4] = stats.SkillUsageFrequency3;
        _obsBuffer[5] = stats.SkillUsageFrequency4;
        _obsBuffer[6] = stats.AggressionScore;
        _obsBuffer[7] = stats.AverageEngagementRange;
        _obsBuffer[8] = stats.PositionalBias;

        return _obsBuffer;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a random ready skill index. Used as the random fallback
    /// when <c>_useRLPolicy</c> is false.
    /// No allocation — iterates the preallocated AllSkills array.
    /// </summary>
    private int SelectRandomReadySkillIndex()
    {
        var skills = _bossSkills.SelectableSkills;

        int readyCount = 0;
        for (int i = 0; i < skills.Count; i++)
        {
            if (skills[i].IsReady) readyCount++;
        }

        if (readyCount == 0) return 0; // all on cooldown — default to Charge

        int pick    = Random.Range(0, readyCount);
        int current = 0;
        for (int i = 0; i < skills.Count; i++)
        {
            if (!skills[i].IsReady) continue;
            if (current == pick) return i;
            current++;
        }

        return 0;
    }

    private void CachePlayerBehaviorTracker()
    {
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null)
        {
            Debug.LogWarning(
                "[BossRLAgent] No GameObject tagged 'Player' found. " +
                "CollectObservations() will return neutral defaults until the " +
                "player is present. Tag the player 'Player' before entering Play mode.");
            return;
        }

        _behaviorTracker = playerGo.GetComponent<PlayerBehaviorTracker>();
        if (_behaviorTracker == null)
            Debug.LogWarning(
                $"[BossRLAgent] PlayerBehaviorTracker not found on '{playerGo.name}'. " +
                "CollectObservations() will return neutral defaults. " +
                "Run CyberBoss/Setup Behavior Tracker to add the component.");
    }

    /// <summary>
    /// Returns neutral observation defaults matching PlayerBehaviorTracker
    /// initial values. Used when the tracker reference is unavailable.
    ///
    /// DodgeDirectionBias=0.5 (neutral), frequencies=0,
    /// AggressionScore=0, EngagementRange=0.5, PositionalBias=0.5.
    /// </summary>
    private static BehaviorStatVector DefaultStats() => new BehaviorStatVector
    {
        DodgeDirectionBias     = 0.5f,
        SkillUsageFrequency0   = 0f,
        SkillUsageFrequency1   = 0f,
        SkillUsageFrequency2   = 0f,
        SkillUsageFrequency3   = 0f,
        SkillUsageFrequency4   = 0f,
        AggressionScore        = 0f,
        AverageEngagementRange = 0.5f,
        PositionalBias         = 0.5f,
    };
}
