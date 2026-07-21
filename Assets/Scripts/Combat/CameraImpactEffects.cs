using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Singleton manager for camera and post-process impact effects.
///
/// Drives two transient effects on major combat hits:
///
///   ChromaticAberration weight spike:
///     Animates the weight of the ImpactEffectsVolume (priority 10, global).
///     The volume's CA intensity is fixed at 1.0; only Volume.weight changes.
///     At weight 0 the arena's baseline CA (0.22) shows through unaffected.
///     At weight 1 the CA clips to 1.0. Weight decays in Update (unscaled time).
///     This approach never modifies the arena VolumeProfile asset at runtime.
///
///   Camera FOV punch:
///     Snaps CinemachineCamera Lens.FieldOfView toward (baseFov + delta) over
///     FovPunchInDuration, then eases back over FovSpringOutDuration. A negative
///     delta reads as a zoom-in "hit" punch; a positive delta (Dash) reads as a
///     zoom-out "speed" punch — the same Lerp naturally mirrors for either sign.
///     LensSettings is a struct — must be read, modified, and re-assigned.
///     Uses Time.unscaledDeltaTime throughout so the animation is not frozen
///     during HitFreezeManager's Time.timeScale dip.
///
/// Two kinds of CA source, matching ScreenShakeManager's split:
///   1. Generic, non-skill-specific reactions — still driven by subscriptions here:
///        Light     — player attacks on boss   (bossHealthSystem.OnDamageTaken)
///        Medium    — any boss attack on player (playerHealthSystem.OnDamageTaken)
///        BossDeath — boss dies                 (BossController.OnBossDied)
///   2. Per-skill signatures — each of the 10 combat skills calls ImpactCA(profile)
///      (or ImpactCAGlitch(profile) for Boss Teleport Strike's "data glitch" read)
///      and, where relevant, TriggerFOVPunch(profile) directly from its own
///      ISkill implementation, using its own ImpactEffectProfile.
///
/// Setup: Run CyberBoss/Setup Combat Effects — creates ImpactEffectsVolume,
/// assigns _impactVolume and _vcam to this component via SerializedObject.
///
/// Usage example (per-skill signature, called directly from an ISkill):
/// <code>
///   CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);
///   CameraImpactEffects.Instance?.TriggerFOVPunch(_config.ImpactProfile);
/// </code>
/// </summary>
public class CameraImpactEffects : MonoBehaviour
{
    public static CameraImpactEffects Instance { get; private set; }

    [SerializeField] private CombatEffectsConfig _config;

    /// <summary>
    /// ImpactEffectsVolume (priority 10, global). Assigned by the setup script.
    /// Its VolumeProfile contains ChromaticAberration at intensity 1.0; only
    /// Volume.weight is modified at runtime.
    /// </summary>
    [SerializeField] private Volume _impactVolume;

    /// <summary>
    /// Reference to the isometric virtual camera for FOV punch.
    /// Assigned by setup script; auto-located by name in Start() as fallback.
    /// </summary>
    [SerializeField] private CinemachineCamera _vcam;

    // Stored at Start() when _vcam is confirmed valid.
    private float _baseFov;
    private Coroutine _fovCoroutine;
    private Coroutine _glitchCoroutine;

    // Stepped/flicker decay uses discrete hold-steps rather than a smooth Lerp —
    // tuned for a "data corruption" read, unique to Boss Teleport Strike.
    private const int   GlitchStepCount    = 5;
    private const float GlitchStepDuration = 0.035f;

    // References retained for clean unsubscription in OnDestroy.
    private HealthSystem   _playerHealthSystem;
    private HealthSystem   _bossHealthSystem;
    private BossController _bossController;

    private System.Action<float> _onPlayerDamageTakenDelegate;
    private System.Action<float> _onBossDamageTakenDelegate;
    private System.Action        _onBossDiedDelegate;

    // ------------------------------------------------------------------
    // Public API — generic reactions
    // ------------------------------------------------------------------

    /// <summary>Light CA spike: player attacks landing on the boss.</summary>
    public void TriggerLightCA()
    {
        if (_impactVolume == null || _config == null) return;
        _impactVolume.weight = Mathf.Max(_impactVolume.weight, _config.LightCaWeight);
    }

    /// <summary>Medium CA spike: any boss attack landing on the player.</summary>
    public void TriggerMediumCA()
    {
        if (_impactVolume == null || _config == null) return;
        _impactVolume.weight = Mathf.Max(_impactVolume.weight, _config.MediumCaWeight);
    }

    /// <summary>Maximum CA spike, slow decay — fired on boss death.</summary>
    public void TriggerBossDeathCA()
    {
        if (_impactVolume == null || _config == null) return;
        _impactVolume.weight = Mathf.Max(_impactVolume.weight, _config.BossDeathCaWeight);
    }

    // ------------------------------------------------------------------
    // Public API — per-skill signature
    // ------------------------------------------------------------------

    /// <summary>
    /// Smooth CA spike using the given skill's ImpactEffectProfile. No-op if the
    /// profile is null or CaWeight is 0 (the normal case for skills that never
    /// spike CA). Mathf.Max means a concurrent generic-reaction spike never gets
    /// downgraded by a smaller per-skill spike, and vice versa.
    /// </summary>
    public void ImpactCA(ImpactEffectProfile profile)
    {
        if (profile == null || profile.CaWeight <= 0f || _impactVolume == null || _config == null) return;
        _impactVolume.weight = Mathf.Max(_impactVolume.weight, profile.CaWeight);
    }

    /// <summary>
    /// Stepped/flicker CA variant — Boss Teleport Strike only. Drives the same
    /// ImpactEffectsVolume.weight as ImpactCA(), but decays in discrete holds
    /// instead of a smooth fade for a "data corruption" glitch read. Falls back
    /// to a plain ImpactCA() call if the profile doesn't request the glitch style
    /// (IsGlitchCa = false), so callers can always use this method uniformly.
    /// </summary>
    public void ImpactCAGlitch(ImpactEffectProfile profile)
    {
        if (profile == null || profile.CaWeight <= 0f || _impactVolume == null || _config == null) return;

        if (!profile.IsGlitchCa)
        {
            ImpactCA(profile);
            return;
        }

        if (_glitchCoroutine != null)
            StopCoroutine(_glitchCoroutine);
        _glitchCoroutine = StartCoroutine(GlitchCACoroutine(profile.CaWeight));
    }

    /// <summary>
    /// Brief FOV zoom then spring-back using the given skill's ImpactEffectProfile.
    /// No-op if the profile is null or FovPunchDelta is 0 (the normal case for
    /// skills that don't use an FOV punch). Interrupts any in-progress FOV punch
    /// before starting a new one.
    /// </summary>
    public void TriggerFOVPunch(ImpactEffectProfile profile)
    {
        if (profile == null || profile.FovPunchDelta == 0f || _vcam == null || _config == null) return;

        if (_fovCoroutine != null)
        {
            StopCoroutine(_fovCoroutine);
            SetFov(_baseFov);
        }

        _fovCoroutine = StartCoroutine(FOVPunchCoroutine(profile.FovPunchDelta));
    }

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
        {
            Debug.LogError("[CameraImpactEffects] CombatEffectsConfig not assigned on " +
                $"'{name}'. Run CyberBoss/Setup Combat Effects.");
            return;
        }

        // Locate vcam if setup script did not assign it (fallback)
        if (_vcam == null)
        {
            var vcamGo = GameObject.Find("CM_IsometricCamera");
            if (vcamGo != null)
                _vcam = vcamGo.GetComponent<CinemachineCamera>();

            if (_vcam == null)
                Debug.LogWarning("[CameraImpactEffects] CinemachineCamera 'CM_IsometricCamera' " +
                    "not found. FOV punch will be disabled.");
        }

        if (_vcam != null)
            _baseFov = _vcam.Lens.FieldOfView;

        // Impact volume starts dormant — arena volume provides baseline CA.
        if (_impactVolume != null)
            _impactVolume.weight = 0f;

        SubscribeToGameEvents();
    }

    private void Update()
    {
        if (_impactVolume == null || _config == null) return;
        // Glitch decay drives its own stepped weight — skip the smooth decay while it runs.
        if (_glitchCoroutine != null) return;

        float w = _impactVolume.weight;
        if (w <= 0.001f)
        {
            if (w > 0f) _impactVolume.weight = 0f;
            return;
        }

        // Decay uses unscaled time so the CA fades normally during hit freeze.
        _impactVolume.weight = Mathf.Max(
            0f, w - _config.CaWeightDecayRate * Time.unscaledDeltaTime);
    }

    private void OnDestroy()
    {
        UnsubscribeFromGameEvents();

        // Restore FOV if destroyed mid-punch (e.g., scene reload)
        if (_vcam != null)
            SetFov(_baseFov);

        // Ensure the impact volume is deactivated on cleanup
        if (_impactVolume != null)
            _impactVolume.weight = 0f;

        if (Instance == this)
            Instance = null;
    }

    // ------------------------------------------------------------------
    // FOV punch coroutine — entire animation uses unscaled time
    // ------------------------------------------------------------------

    private IEnumerator FOVPunchCoroutine(float delta)
    {
        float targetFov = _baseFov + delta;
        float elapsed   = 0f;

        // Punch in: snap toward targetFov
        while (elapsed < _config.FovPunchInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _config.FovPunchInDuration);
            SetFov(Mathf.Lerp(_baseFov, targetFov, t));
            yield return null;
        }

        SetFov(targetFov);
        elapsed = 0f;

        // Spring out: ease back to baseFov
        while (elapsed < _config.FovSpringOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _config.FovSpringOutDuration);
            SetFov(Mathf.Lerp(targetFov, _baseFov, t));
            yield return null;
        }

        SetFov(_baseFov);
        _fovCoroutine = null;
    }

    /// <summary>
    /// LensSettings is a struct in Cinemachine 3.x — must copy, modify, then
    /// reassign; direct field mutation on the returned struct has no effect.
    /// </summary>
    private void SetFov(float fov)
    {
        var lens = _vcam.Lens;
        lens.FieldOfView = fov;
        _vcam.Lens = lens;
    }

    // ------------------------------------------------------------------
    // Glitch CA coroutine — stepped/flicker decay (Boss Teleport Strike only)
    // ------------------------------------------------------------------

    private IEnumerator GlitchCACoroutine(float peakWeight)
    {
        _impactVolume.weight = Mathf.Max(_impactVolume.weight, peakWeight);

        for (int step = GlitchStepCount; step >= 0; step--)
        {
            _impactVolume.weight = peakWeight * (step / (float)GlitchStepCount);
            yield return new WaitForSecondsRealtime(GlitchStepDuration);
        }

        _impactVolume.weight = 0f;
        _glitchCoroutine = null;
    }

    // ------------------------------------------------------------------
    // Event wiring — generic reactions only; per-skill CA/FOV are direct calls
    // ------------------------------------------------------------------

    private void SubscribeToGameEvents()
    {
        _onPlayerDamageTakenDelegate = _ => TriggerMediumCA();
        _onBossDamageTakenDelegate   = _ => TriggerLightCA();
        _onBossDiedDelegate          = TriggerBossDeathCA;

        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo != null)
        {
            _playerHealthSystem = playerGo.GetComponent<HealthSystem>();
            if (_playerHealthSystem != null)
                _playerHealthSystem.OnDamageTaken += _onPlayerDamageTakenDelegate;
            else
                Debug.LogWarning("[CameraImpactEffects] HealthSystem not found on player. " +
                    "Medium CA spikes will not trigger.");
        }
        else
        {
            Debug.LogWarning("[CameraImpactEffects] No GameObject tagged 'Player'. " +
                "Player-related CA spikes will not trigger.");
        }

        var bossGo = GameObject.FindWithTag("Boss");
        if (bossGo != null)
        {
            _bossHealthSystem = bossGo.GetComponent<HealthSystem>();
            if (_bossHealthSystem != null)
                _bossHealthSystem.OnDamageTaken += _onBossDamageTakenDelegate;

            _bossController = bossGo.GetComponent<BossController>();
            if (_bossController != null)
                _bossController.OnBossDied += _onBossDiedDelegate;
            else
                Debug.LogWarning("[CameraImpactEffects] BossController not found. " +
                    "Boss death CA spike will not trigger.");
        }
        else
        {
            Debug.LogWarning("[CameraImpactEffects] No GameObject tagged 'Boss'. " +
                "Boss-related CA spikes will not trigger.");
        }
    }

    private void UnsubscribeFromGameEvents()
    {
        if (_playerHealthSystem != null && _onPlayerDamageTakenDelegate != null)
            _playerHealthSystem.OnDamageTaken -= _onPlayerDamageTakenDelegate;

        if (_bossHealthSystem != null && _onBossDamageTakenDelegate != null)
            _bossHealthSystem.OnDamageTaken -= _onBossDamageTakenDelegate;

        if (_bossController != null && _onBossDiedDelegate != null)
            _bossController.OnBossDied -= _onBossDiedDelegate;
    }
}
