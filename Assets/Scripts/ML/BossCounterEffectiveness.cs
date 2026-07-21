using UnityEngine;

/// <summary>
/// Pure C# port of cyberboss_env.py's _compute_effectiveness() — used by
/// BossRLAgent.ApplyCooldownSubstitution() to rank cooldown-substitute skills
/// by genuine counter-play quality instead of raw remaining cooldown time.
///
/// Must be kept in exact sync with _compute_effectiveness() in
/// Assets/ML/Training/cyberboss_env.py. One deliberate divergence: the Python
/// warmup_ramp dampening (obs[1]-[5] terms scaled down for the first
/// WARMUP_STEPS of a training episode, to counteract early-episode statistical
/// noise from a near-zero elapsed-time denominator) is NOT replicated here —
/// it is a training-signal-stabilization device with no clean analog for a
/// real fight's pacing, and this formula is only used for the substitution
/// fallback (not the primary reward-shaping path), so a small early-fight
/// divergence here is an accepted, contained simplification rather than a
/// parity requirement.
/// </summary>
public static class BossCounterEffectiveness
{
    /// <summary>
    /// Returns counter-play effectiveness in [0, 1] for the given boss skill
    /// against a 9-float observation in BehaviorStatVector field order.
    /// Skill index matches BossSkills.SelectableSkills:
    /// 0=Charge, 1=ProjectileBurst, 2=AoESlam, 3=TeleportStrike.
    /// </summary>
    public static float Compute(int skill, float[] obs)
    {
        switch (skill)
        {
            case 0: // Charge — counters ranged play, plus burst-strike spam
            {
                float rangeEff = obs[7];
                float burstEff = obs[4];
                return Mathf.Clamp01(rangeEff * 0.7f + burstEff * 0.3f);
            }

            case 1: // ProjectileBurst — counters barrier/passive turtling and parry-spam
            {
                float barrierEff = obs[5];
                float parryEff   = obs[2];
                return Mathf.Clamp01((barrierEff + parryEff) * 0.5f);
            }

            case 2: // AoESlam — counters close-range aggression
                return Mathf.Clamp01(obs[6]);

            case 3: // TeleportStrike — counters dash-heavy play, plus general
                    // distance-keeping (deliberately overlaps with Charge's
                    // range driver — more than one valid counter per player
                    // tendency increases skill variety instead of strict 1:1).
            {
                float dashEff       = obs[1];
                float biasDeviation = Mathf.Abs(obs[0] - 0.5f) * 2f;
                float rangeEff      = obs[7];
                return Mathf.Clamp01(dashEff * 0.5f + biasDeviation * 0.3f + rangeEff * 0.2f);
            }

            default:
                return 0f;
        }
    }
}
