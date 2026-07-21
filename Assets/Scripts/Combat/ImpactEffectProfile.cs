using System;
using UnityEngine;

/// <summary>
/// Per-skill combat feedback signature.
///
/// A plain serializable data record (not a ScriptableObject) embedded as a field
/// inside each skill's own Config asset (e.g. DashSkillConfig, BossChargeConfig).
/// Skills that need more than one distinct feedback moment (Dash's cast vs.
/// perfect-dodge reward, Barrier's absorb/broken/expired, Boss Teleport Strike's
/// arrival vs. strike) declare multiple named fields of this type on their config.
///
/// Every channel is independently enable-gated:
///   EnableShake  = false  → this skill never triggers screen shake.
///   EnableFreeze = false  → this skill never triggers a hit-freeze frame.
///   CaWeight     = 0      → this skill never spikes chromatic aberration.
/// A skill that "never shakes" (Dash, Boss Shield Phase) simply ships a profile
/// with EnableShake = false rather than omitting the shake fields — this keeps
/// the schema uniform across all 10 skills and lets ScreenShakeManager.Shake(),
/// HitFreezeManager.Freeze(), and CameraImpactEffects.ImpactCA()/ImpactCAGlitch()
/// take a single profile type regardless of which channels a given skill uses.
///
/// IMPORTANT — do not give any field a non-default C# field initializer (e.g.
/// "= 20"). SetupPart10Polish.SeedProfile() uses _vfxParticleCount == 0 as its
/// idempotency sentinel for "this profile has never been seeded or hand-tuned".
/// A non-zero initializer here materializes as that same non-zero value the
/// moment Unity's serializer first encounters this field on an existing Config
/// asset (i.e. before the seeder ever runs) — which made the sentinel see
/// "already seeded" on every asset from the very first run, silently seeding
/// nothing at all. Every field here must default to its natural zero/false value.
///
/// Usage example (from a plain-C# ISkill implementation, mirroring the existing
/// BossChargeSkill.DealContactDamage() pattern):
/// <code>
///   ScreenShakeManager.Instance?.Shake(_config.ImpactProfile);
///   HitFreezeManager.Instance?.Freeze(_config.ImpactProfile);
///   CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);
///   CombatVFXManager.Instance?.SpawnImpact(worldPosition, "BurstStrike", _config.ImpactProfile);
/// </code>
/// </summary>
[Serializable]
public class ImpactEffectProfile
{
    [Header("Screen Shake")]
    [SerializeField] private bool  _enableShake;
    [Tooltip("Cinemachine impulse amplitude. Reference strength 1 = the old Medium tier.")]
    [SerializeField] private float _shakeForce;
    [Tooltip("Cinemachine impulse duration in seconds.")]
    [SerializeField] private float _shakeDuration;
    [Tooltip("True = fast decay/high-frequency 'jolt' (Bump shape). " +
             "False = slower sustained 'roll' (Explosion shape) — e.g. AoE Slam.")]
    [SerializeField] private bool  _shakeIsSnap;

    [Header("Hit Freeze")]
    [SerializeField] private bool  _enableFreeze;
    [Tooltip("Time.timeScale multiplier while frozen. Lower = deeper freeze.")]
    [SerializeField] private float _freezeTimeScale;
    [Tooltip("Real-world seconds (WaitForSecondsRealtime), independent of timeScale.")]
    [SerializeField] private float _freezeRealSeconds;

    [Header("Chromatic Aberration")]
    [Tooltip("Target ImpactEffectsVolume weight. 0 = this skill never spikes CA.")]
    [SerializeField] private float _caWeight;
    [Tooltip("True = stepped/flicker decay (data-glitch read) instead of a smooth " +
             "fade. Boss Teleport Strike only.")]
    [SerializeField] private bool  _isGlitchCa;

    [Header("Camera FOV Punch")]
    [Tooltip("Signed FOV delta in degrees. Negative = zoom-in punch (hits). " +
             "Positive = zoom-out punch (Dash's 'speed' read). 0 = no FOV punch.")]
    [SerializeField] private float _fovPunchDelta;

    [Header("Impact VFX")]
    [Tooltip("HDR color — values above 1 feed URP bloom.")]
    [SerializeField][ColorUsage(false, true)] private Color _vfxColor;
    [SerializeField] private int   _vfxParticleCount;
    [SerializeField] private float _vfxMinSpeed;
    [SerializeField] private float _vfxMaxSpeed;
    [SerializeField] private float _vfxLifetime;
    [SerializeField] private float _vfxSize;

    public bool  EnableShake       => _enableShake;
    public float ShakeForce        => _shakeForce;
    public float ShakeDuration     => _shakeDuration;
    public bool  ShakeIsSnap       => _shakeIsSnap;

    public bool  EnableFreeze      => _enableFreeze;
    public float FreezeTimeScale   => _freezeTimeScale;
    public float FreezeRealSeconds => _freezeRealSeconds;

    public float CaWeight          => _caWeight;
    public bool  IsGlitchCa        => _isGlitchCa;

    public float FovPunchDelta     => _fovPunchDelta;

    public Color VfxColor          => _vfxColor;
    public int   VfxParticleCount  => _vfxParticleCount;
    public float VfxMinSpeed       => _vfxMinSpeed;
    public float VfxMaxSpeed       => _vfxMaxSpeed;
    public float VfxLifetime       => _vfxLifetime;
    public float VfxSize           => _vfxSize;
}
