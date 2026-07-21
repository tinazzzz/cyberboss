using UnityEngine;

/// <summary>
/// ScriptableObject holding all tunable values for the expanded combat effects:
/// impact sparks, chromatic aberration pulse, and camera FOV punch.
///
/// Created at Assets/ScriptableObjects/CombatEffectsConfig.asset by
/// CyberBoss/Setup Combat Effects.
///
/// Chromatic aberration is driven by animating the weight of ImpactEffectsVolume
/// (priority 10) rather than modifying the arena VolumeProfile — so the shared
/// disk asset is never touched at runtime.
/// </summary>
[CreateAssetMenu(fileName = "CombatEffectsConfig", menuName = "CyberBoss/Combat Effects Config")]
public class CombatEffectsConfig : ScriptableObject
{
    // ------------------------------------------------------------------
    // Chromatic Aberration — ImpactEffectsVolume weight targets
    // ------------------------------------------------------------------

    [Header("Chromatic Aberration")]
    [Tooltip("Volume weight when player attacks land on the boss. " +
             "Weight 0 = arena baseline (0.22 CA); weight 1 = impact profile max (1.0 CA).")]
    [SerializeField][Range(0f, 1f)] private float _lightCaWeight = 0.35f;

    [Tooltip("Volume weight on any boss attack landing on the player.")]
    [SerializeField][Range(0f, 1f)] private float _mediumCaWeight = 0.5f;

    [Tooltip("Volume weight for heavy boss attacks (Charge, AoE Slam). " +
             "Called directly from boss skill code and overrides the concurrent medium spike.")]
    [SerializeField][Range(0f, 1f)] private float _heavyCaWeight = 0.7f;

    [Tooltip("Volume weight for the boss death sequence. Held at 1.0 then decays slowly.")]
    [SerializeField][Range(0f, 1f)] private float _bossDeathCaWeight = 1.0f;

    [Tooltip("How quickly the volume weight decays back to 0 per second. " +
             "2.5 = back to baseline in ~0.28 s from a heavy spike.")]
    [SerializeField] private float _caWeightDecayRate = 2.5f;

    // ------------------------------------------------------------------
    // Camera FOV Punch
    // ------------------------------------------------------------------

    [Header("FOV Punch")]
    [Tooltip("FOV change in degrees. Negative zooms in (base 40 -> 36 at -4). " +
             "Fires only on heavy hits (Charge, AoE Slam) via direct boss skill calls.")]
    [SerializeField] private float _fovPunchDelta = -4f;

    [Tooltip("Seconds to complete the initial zoom (punch in). Keep short for snappiness.")]
    [SerializeField] private float _fovPunchInDuration = 0.05f;

    [Tooltip("Seconds to ease back to the original FOV (spring out).")]
    [SerializeField] private float _fovSpringOutDuration = 0.2f;

    // ------------------------------------------------------------------
    // Player Hit Sparks — fired when player attacks land on the boss
    // ------------------------------------------------------------------

    [Header("Player Hit Sparks (cyan, at boss contact point)")]
    [Tooltip("Particles emitted per burst.")]
    [SerializeField] private int _playerSparkCount = 20;

    [Tooltip("HDR color — values above 1 trigger bloom on the cyberpunk arena.")]
    [SerializeField][ColorUsage(false, true)] private Color _playerSparkColor
        = new Color(0f, 2.5f, 2.5f, 1f); // HDR cyan

    [SerializeField] private float _playerSparkMinSpeed = 4f;
    [SerializeField] private float _playerSparkMaxSpeed = 10f;
    [SerializeField] private float _playerSparkLifetime = 0.25f;
    [SerializeField] private float _playerSparkSize     = 0.08f;

    // ------------------------------------------------------------------
    // Boss Attack Impact — fired when any boss attack lands on the player
    // ------------------------------------------------------------------

    [Header("Boss Attack Impact (orange, at player position)")]
    [Tooltip("Particles emitted per burst. Slightly more than player sparks for dramatic weight.")]
    [SerializeField] private int _bossImpactCount = 30;

    [SerializeField][ColorUsage(false, true)] private Color _bossImpactColor
        = new Color(2.5f, 0.5f, 0f, 1f); // HDR orange

    [SerializeField] private float _bossImpactMinSpeed = 5f;
    [SerializeField] private float _bossImpactMaxSpeed = 12f;
    [SerializeField] private float _bossImpactLifetime = 0.2f;
    [SerializeField] private float _bossImpactSize     = 0.12f;

    // ------------------------------------------------------------------
    // Public read-only properties — follow BossConfig.cs pattern
    // ------------------------------------------------------------------

    public float LightCaWeight     => _lightCaWeight;
    public float MediumCaWeight    => _mediumCaWeight;
    public float HeavyCaWeight     => _heavyCaWeight;
    public float BossDeathCaWeight => _bossDeathCaWeight;
    public float CaWeightDecayRate => _caWeightDecayRate;

    public float FovPunchDelta        => _fovPunchDelta;
    public float FovPunchInDuration   => _fovPunchInDuration;
    public float FovSpringOutDuration => _fovSpringOutDuration;

    public int   PlayerSparkCount    => _playerSparkCount;
    public Color PlayerSparkColor    => _playerSparkColor;
    public float PlayerSparkMinSpeed => _playerSparkMinSpeed;
    public float PlayerSparkMaxSpeed => _playerSparkMaxSpeed;
    public float PlayerSparkLifetime => _playerSparkLifetime;
    public float PlayerSparkSize     => _playerSparkSize;

    public int   BossImpactCount    => _bossImpactCount;
    public Color BossImpactColor    => _bossImpactColor;
    public float BossImpactMinSpeed => _bossImpactMinSpeed;
    public float BossImpactMaxSpeed => _bossImpactMaxSpeed;
    public float BossImpactLifetime => _bossImpactLifetime;
    public float BossImpactSize     => _bossImpactSize;
}
