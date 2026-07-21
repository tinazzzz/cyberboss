using UnityEngine;

/// <summary>
/// Tunable parameters for the Boss Teleport Strike skill.
///
/// The boss teleports behind the player, then strikes after a brief delay.
/// Counters the player's Dash by appearing behind them before the cooldown resets.
///
/// Set TargetLayerMask to the Player layer so the strike overlap finds only the
/// player's colliders. Leave at Nothing (0) only for testing — a warning will fire
/// and Everything will be used as a fallback.
///
/// Create asset: CyberBoss > Boss > TeleportStrikeConfig
/// </summary>
[CreateAssetMenu(fileName = "BossSkillConfig_TeleportStrike",
    menuName = "CyberBoss/Boss/TeleportStrikeConfig")]
public class BossTeleportStrikeConfig : ScriptableObject
{
    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 8f;

    [Header("Teleport")]
    [SerializeField] private float _teleportBehindOffset = 1.5f;

    [Header("Strike")]
    [SerializeField] private float     _strikeRadius    = 2f;
    [SerializeField] private float     _strikeDamage    = 22f;
    [SerializeField] private float     _strikeDelay     = 1.0f;
    [SerializeField] private LayerMask _targetLayerMask = ~0; // Everything — set to Player layer for best performance

    [Header("VFX — scale pulse on arrival")]
    [SerializeField] private float _scalePulseMagnitude = 1.2f;
    [SerializeField] private float _scalePulseDuration  = 0.15f;

    [Header("Windup Warning")]
    [Tooltip("Seconds the red warning disc is shown at the teleport destination " +
             "before the boss actually moves. Gives the player time to dodge.")]
    [SerializeField] private float _preTeleportWindup = 1.0f;

    [Header("Dodge Prediction")]
    [Tooltip("Max lateral shift (world units, along camera-right) applied to the " +
             "teleport destination based on PlayerBehaviorTracker.DodgeDirectionBias. " +
             "Scales with how predictable the player's dodge direction is: 0 shift at " +
             "neutral bias (0.5), full shift at a fully one-sided bias (0 or 1). " +
             "Set to 0 to disable prediction and teleport directly behind the player.")]
    [SerializeField] private float _predictiveLateralOffset = 2.0f;

    [Tooltip("Lateral dashes required (left+right, PlayerBehaviorTracker.LateralDashSampleCount) " +
             "before DodgeDirectionBias fully drives the predictive offset. Below this count the " +
             "offset scales down linearly toward 0 — a single early dash reads as a fully " +
             "committed bias (0.0 or 1.0) despite proving nothing statistically. " +
             "Set to 0 to disable confidence scaling and trust the raw bias immediately.")]
    [SerializeField] private int _dodgeBiasConfidenceSamples = 4;

    [Header("Impact Feedback")]
    [Tooltip("Fired on arrival (materialize) — VFX + a light CA tick only, no shake " +
             "or freeze. Uses the glitch/flicker CA variant for a 'data glitch' read.")]
    [SerializeField] private ImpactEffectProfile _arrivalProfile;

    [Tooltip("Fired at the strike AoE — a short, very high-amplitude single 'snap' " +
             "shake, a new short/sharp freeze, and the glitch CA variant. Purple " +
             "impact burst VFX.")]
    [SerializeField] private ImpactEffectProfile _strikeProfile;

    public float     Cooldown                   => _cooldown;
    public float     TeleportBehindOffset       => _teleportBehindOffset;
    public float     StrikeRadius               => _strikeRadius;
    public float     StrikeDamage               => _strikeDamage;
    public float     StrikeDelay                => _strikeDelay;
    public LayerMask TargetLayerMask            => _targetLayerMask;
    public float     ScalePulseMagnitude        => _scalePulseMagnitude;
    public float     ScalePulseDuration         => _scalePulseDuration;
    public float     PreTeleportWindup          => _preTeleportWindup;
    public float     PredictiveLateralOffset    => _predictiveLateralOffset;
    public int       DodgeBiasConfidenceSamples => _dodgeBiasConfidenceSamples;

    public ImpactEffectProfile ArrivalProfile => _arrivalProfile;
    public ImpactEffectProfile StrikeProfile  => _strikeProfile;
}
