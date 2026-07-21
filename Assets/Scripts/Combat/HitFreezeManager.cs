using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton manager for hit freeze frames.
///
/// Briefly dips Time.timeScale on impactful hits to give the player a tactile
/// pause that confirms the hit landed. Uses WaitForSecondsRealtime inside the
/// freeze coroutine so the duration is measured in real wall-clock time and is
/// not affected by the modified timeScale.
///
/// Every one of the 10 combat skills that wants a freeze frame carries its own
/// ImpactEffectProfile (on its Config asset) and calls Freeze(profile) directly
/// from its own ISkill implementation — there is no shared "Light"/"Heavy" enum
/// tier anymore. A magnitude-based priority comparison (deeper freeze wins)
/// replaces the old fixed 2-case enum so any number of profiles — Parry's short
/// "Sharp" freeze, Boss Teleport Strike's short/sharp freeze, Burst Strike's and
/// Boss Charge's and Boss AoE Slam's independently-tunable Heavy-equivalent
/// freezes — can coexist without adding a new enum case per skill.
///
/// Priority rule: depth = 1 - FreezeTimeScale (lower timescale = deeper freeze =
/// higher priority). A new Freeze() call only interrupts an in-progress freeze
/// if it is strictly deeper; an equal-or-shallower call while a freeze is
/// already running is a no-op so overlapping hits cannot re-trigger or extend it.
///
/// Setup: Run CyberBoss/Setup Polish — adds this component to the scene
/// and assigns the shared ScreenShakeConfig.
///
/// Usage example (from a plain-C# ISkill implementation):
/// <code>
///   HitFreezeManager.Instance?.Freeze(_config.ImpactProfile);
/// </code>
/// </summary>
public class HitFreezeManager : MonoBehaviour
{
    /// <summary>
    /// Scene-scoped singleton. Null-safe — use ?.Freeze() from skill code so a
    /// missing manager never causes a NullReferenceException.
    /// </summary>
    public static HitFreezeManager Instance { get; private set; }

    [SerializeField] private ScreenShakeConfig _config;

    // -1 means no freeze currently active. Otherwise holds the depth priority
    // (1 - FreezeTimeScale) of the freeze currently running.
    private const float NoFreezePriority = -1f;
    private float     _currentPriority = NoFreezePriority;
    private Coroutine _freezeCoroutine;

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Fires a hit-freeze frame using the given skill's ImpactEffectProfile.
    /// No-op if the profile is null, has freeze disabled (EnableFreeze = false —
    /// the normal case for e.g. Boss Projectile Burst, which explicitly never
    /// freezes so multi-shot volleys don't feel choppy), or a strictly deeper
    /// freeze is already running.
    /// </summary>
    public void Freeze(ImpactEffectProfile profile)
    {
        if (profile == null || !profile.EnableFreeze || _config == null) return;

        float priority = ComputePriority(profile.FreezeTimeScale);

        // An equal-or-deeper freeze is already running — never downgrade or
        // restart it from a shallower or simultaneous call.
        if (_freezeCoroutine != null && priority <= _currentPriority) return;

        if (_freezeCoroutine != null)
            StopCoroutine(_freezeCoroutine);

        _currentPriority = priority;
        _freezeCoroutine = StartCoroutine(FreezeCoroutine(profile.FreezeTimeScale, profile.FreezeRealSeconds));
    }

    private static float ComputePriority(float freezeTimeScale) => 1f - freezeTimeScale;

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (_config == null)
            Debug.LogError("[HitFreezeManager] ScreenShakeConfig not assigned on " +
                $"'{name}'. Run CyberBoss/Setup Polish to create and assign it.");
    }

    private void OnDestroy()
    {
        // Guarantee Time.timeScale is restored if this object is destroyed mid-freeze.
        if (_currentPriority != NoFreezePriority)
            Time.timeScale = 1f;

        if (Instance == this)
            Instance = null;
    }

    // ------------------------------------------------------------------
    // Freeze execution
    // ------------------------------------------------------------------

    /// <summary>
    /// Drops Time.timeScale to targetTimeScale for realDuration real-world seconds,
    /// then restores to 1. WaitForSecondsRealtime is allocated per-call (infrequent,
    /// not a hot path) to avoid the WaitForSecondsRealtime re-use timer-state issue.
    /// </summary>
    private IEnumerator FreezeCoroutine(float targetTimeScale, float realDuration)
    {
        Time.timeScale = targetTimeScale;
        yield return new WaitForSecondsRealtime(realDuration);
        Time.timeScale   = 1f;
        _currentPriority = NoFreezePriority;
        _freezeCoroutine = null;
    }
}
