using UnityEngine;

/// <summary>
/// Tunable parameters for screen shake and hit freeze.
///
/// All values are exposed in the Inspector for live tuning without recompilation.
/// Create via: Assets > Create > CyberBoss > Screen Shake Config
/// Or run: CyberBoss / Setup Polish (creates and assigns automatically).
///
/// Shake tiers: Light (normal attacks) → Medium (skills, boss projectiles) →
/// Heavy (boss Charge) → HeavySlam (boss AoE Slam, longer duration) →
/// BossDeath (maximum dramatic rumble).
///
/// Freeze tiers: Light (~1 frame, normal attacks) → Heavy (~3 frames, Burst Strike,
/// boss Charge, boss AoE Slam). WaitForSecondsRealtime is used so freeze duration
/// is independent of the modified Time.timeScale.
/// </summary>
[CreateAssetMenu(fileName = "ScreenShakeConfig", menuName = "CyberBoss/Screen Shake Config")]
public class ScreenShakeConfig : ScriptableObject
{
    [Header("Impulse Forces (1 = reference strength)")]
    [Tooltip("Force on light hits — normal attacks, minor contact.")]
    [SerializeField] private float _lightImpulseForce = 0.25f;

    [Tooltip("Force on medium hits — player skills, boss projectile landing on player.")]
    [SerializeField] private float _mediumImpulseForce = 0.6f;

    [Tooltip("Force on heavy hits — Boss Charge contact.")]
    [SerializeField] private float _heavyImpulseForce = 1.2f;

    [Tooltip("Force on boss death — maximum dramatic impact.")]
    [SerializeField] private float _bossDeathImpulseForce = 2.0f;

    [Header("Impulse Durations (seconds)")]
    [SerializeField] private float _lightDuration  = 0.2f;
    [SerializeField] private float _mediumDuration = 0.3f;

    [Tooltip("Charge impact — sharp snap feel.")]
    [SerializeField] private float _heavyDuration  = 0.4f;

    [Tooltip("AoE Slam detonation — longer than Charge to convey sustained ground shudder.")]
    [SerializeField] private float _slamDuration   = 0.65f;

    [Tooltip("Boss death rumble — slow-decay for maximum dramatic effect.")]
    [SerializeField] private float _bossDeathDuration = 0.85f;

    [Header("Freeze Frame — Light (~1 frame at 30 fps)")]
    [Tooltip("Time.timeScale multiplier during the light freeze. 0.15 = 15% speed.")]
    [SerializeField] private float _lightFreezeTimeScale   = 0.15f;

    [Tooltip("Real-time duration (WaitForSecondsRealtime). 0.033 s ≈ 1 frame at 30 fps.")]
    [SerializeField] private float _lightFreezeRealSeconds = 0.033f;

    [Header("Freeze Frame — Heavy (~3 frames at 30 fps)")]
    [Tooltip("Time.timeScale multiplier during the heavy freeze. 0.05 = near-pause.")]
    [SerializeField] private float _heavyFreezeTimeScale   = 0.05f;

    [Tooltip("Real-time duration (WaitForSecondsRealtime). 0.1 s ≈ 3 frames at 30 fps.")]
    [SerializeField] private float _heavyFreezeRealSeconds = 0.1f;

    // ------------------------------------------------------------------
    // Public properties — read by ScreenShakeManager and HitFreezeManager
    // ------------------------------------------------------------------

    /// <summary>Impulse force for light hits (normal attacks, minor impacts).</summary>
    public float LightImpulseForce     => _lightImpulseForce;

    /// <summary>Impulse force for medium hits (player skills, projectile hits on player).</summary>
    public float MediumImpulseForce    => _mediumImpulseForce;

    /// <summary>Impulse force for heavy hits (Boss Charge contact).</summary>
    public float HeavyImpulseForce     => _heavyImpulseForce;

    /// <summary>Impulse force for boss death — maximum dramatic value.</summary>
    public float BossDeathImpulseForce => _bossDeathImpulseForce;

    /// <summary>Impulse duration for light shakes.</summary>
    public float LightDuration     => _lightDuration;

    /// <summary>Impulse duration for medium shakes.</summary>
    public float MediumDuration    => _mediumDuration;

    /// <summary>Impulse duration for heavy shakes (Boss Charge snap).</summary>
    public float HeavyDuration     => _heavyDuration;

    /// <summary>
    /// Impulse duration for Boss AoE Slam — longer than standard heavy to convey
    /// a sustained ground shudder distinct from the snap of Charge.
    /// </summary>
    public float SlamDuration      => _slamDuration;

    /// <summary>Impulse duration for boss death — slow-decay dramatic rumble.</summary>
    public float BossDeathDuration => _bossDeathDuration;

    /// <summary>Time.timeScale during a light freeze frame.</summary>
    public float LightFreezeTimeScale   => _lightFreezeTimeScale;

    /// <summary>Real-time seconds for a light freeze (survives timescale changes).</summary>
    public float LightFreezeRealSeconds => _lightFreezeRealSeconds;

    /// <summary>Time.timeScale during a heavy freeze frame.</summary>
    public float HeavyFreezeTimeScale   => _heavyFreezeTimeScale;

    /// <summary>Real-time seconds for a heavy freeze (survives timescale changes).</summary>
    public float HeavyFreezeRealSeconds => _heavyFreezeRealSeconds;
}
