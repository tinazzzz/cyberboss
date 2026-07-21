using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss AoE Slam skill.
///
/// The boss pauses for WindupDuration while a red sphere indicator shows the blast
/// radius, then calls Physics.OverlapSphereNonAlloc to find and damage targets.
///
/// Damage routing: each hit target is queried for DamageHandler first (not the
/// generic IDamageable) so the player's full defense chain — Dash invincibility,
/// Parry reflect, Barrier absorption — is always applied. Objects without
/// DamageHandler in the hit results are silently skipped.
///
/// TargetLayerMask: set to the Player layer in BossSkillConfig_AoeSlam so the
/// overlap only queries player colliders. If left at Nothing (0), a warning fires
/// and Everything is used as a fallback (boss self-colliders are excluded via
/// IsChildOf).
///
/// VFX: a red sphere primitive is spawned at the boss position during the windup
/// and destroyed when the slam detonates. A particle ground-ring shockwave
/// expands outward when the slam lands.
///
/// _hitBuffer and _hitHandlers are pre-allocated — no heap allocation in
/// DamagePlayersInRange(). _hitHandlers deduplicates by DamageHandler so a player
/// with multiple colliders is only hit once per activation.
///
/// BossSkills.ResetActiveSkill() calls StopExecution() on stagger, which destroys
/// the indicator if the boss is interrupted during the windup.
///
/// Counters the player's Burst Strike — punishes close-range aggression.
/// </summary>
public class BossAoESlamSkill : ISkill
{
    private readonly BossAoESlamConfig _config;
    private SkillCooldown _cooldown;

    private MonoBehaviour _runner;
    private Transform     _bossTransform;
    private Transform     _playerTransform;
    private PlayerSkills  _playerSkills;
    private Coroutine     _activeCoroutine;
    private GameObject    _activeIndicator;
    private Animator      _animator;

    // Pre-allocated overlap buffer — zero allocation in DamagePlayersInRange().
    private readonly Collider[] _hitBuffer = new Collider[16];

    // Allocated once in Init() with a URP Unlit shader so the indicator
    // does not render as pink (Built-in RP Standard is missing in URP/WebGL).
    private Material _slamIndicatorMaterial;

    // Pre-cached yields allocated in Init(), not per slam.
    private WaitForSeconds _windupWait;
    private WaitForSeconds _impactDelayWait;

    // Pre-allocated to avoid heap allocation per activation.
    private readonly HashSet<DamageHandler> _hitHandlers = new HashSet<DamageHandler>();

    // Ground-ring shockwave duration — a particle burst alone reads as
    // "dots", not a ring, so this is a separate expanding-disc technique.
    private const float GroundRingDuration = 0.25f;

    // Wire "SlamTrigger" in the boss Animator to a ground-slam animation.
    private static readonly int SlamAnimHash = Animator.StringToHash("SlamTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  IsExecuting      { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    public BossAoESlamSkill(BossAoESlamConfig config)
    {
        _config   = config;
        _cooldown = new SkillCooldown(config.Cooldown);
    }

    /// <param name="runner">MonoBehaviour coroutine host — BossSkills.</param>
    /// <param name="bossTransform">AoE is centred on this transform each slam.</param>
    /// <param name="playerTransform">Used to range-gate Execute() — skill is skipped when player is beyond MinEngagementRange.</param>
    public void Init(MonoBehaviour runner, Transform bossTransform, Transform playerTransform, Animator animator)
    {
        _runner          = runner;
        _bossTransform   = bossTransform;
        _playerTransform = playerTransform;
        _windupWait      = new WaitForSeconds(_config.WindupDuration);
        _impactDelayWait = new WaitForSeconds(_config.SlamImpactDelay);
        _animator        = animator;
        // One-time GetComponent in Init() — not in Update, so allocation is fine.
        _playerSkills    = playerTransform?.GetComponent<PlayerSkills>();

        // CreatePrimitive assigns the Built-in RP Standard shader (pink in URP/WebGL).
        // Pre-allocate a URP Unlit material instance and set ALL properties required
        // for GPU alpha blending — setting _Surface alone is not sufficient to change
        // the actual blend equations; _SrcBlend/_DstBlend/_ZWrite are also required.
        var urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        _slamIndicatorMaterial = new Material(
            urpUnlit != null ? urpUnlit : Shader.Find("Sprites/Default"));

        _slamIndicatorMaterial.SetFloat("_Surface", 1f);   // 0=Opaque, 1=Transparent
        _slamIndicatorMaterial.SetFloat("_Blend",   0f);   // 0=Alpha blend
        _slamIndicatorMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _slamIndicatorMaterial.SetOverrideTag("RenderType", "Transparent");
        _slamIndicatorMaterial.renderQueue =
            (int)UnityEngine.Rendering.RenderQueue.Transparent;
        // These three set the actual GPU blend equations.
        _slamIndicatorMaterial.SetInt("_SrcBlend",
            (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _slamIndicatorMaterial.SetInt("_DstBlend",
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _slamIndicatorMaterial.SetInt("_ZWrite", 0);
        // Color baked directly into the material — no MaterialPropertyBlock needed.
        _slamIndicatorMaterial.SetColor("_BaseColor", new Color(1f, 0.15f, 0f, 0.45f));
    }

    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || _runner == null) return;

        if (_playerTransform != null)
        {
            Vector3 bossXZ   = new Vector3(_bossTransform.position.x, 0f, _bossTransform.position.z);
            Vector3 playerXZ = new Vector3(_playerTransform.position.x, 0f, _playerTransform.position.z);
            if (Vector3.Distance(bossXZ, playerXZ) > _config.MinEngagementRange)
                return;
        }

        _cooldown.Trigger();
        IsExecuting      = true;
        _activeCoroutine = _runner.StartCoroutine(SlamRoutine());
    }

    public void UpdateCooldown(float deltaTime) => _cooldown.Tick(deltaTime);

    public void Reset(Animator animator)
    {
        StopExecution();
        _cooldown.Reset();
        animator?.ResetTrigger(SlamAnimHash);
    }

    /// <summary>
    /// Stop the in-flight coroutine and destroy the slam indicator if the boss
    /// is interrupted during the windup phase.
    /// </summary>
    public void StopExecution()
    {
        if (_activeCoroutine != null && _runner != null)
        {
            _runner.StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        if (_animator != null) _animator.ResetTrigger(SlamAnimHash);
        DestroyIndicator();
        IsExecuting = false;
    }

    // ------------------------------------------------------------------
    // Coroutine
    // ------------------------------------------------------------------

    private IEnumerator SlamRoutine()
    {
        _activeIndicator = CreateSlamIndicator();

        yield return _windupWait;

        // Indicator disappears: player's cue to dodge.
        DestroyIndicator();
        // Animation fires now — plays its windup before the impact frame.
        if (_animator != null) _animator.SetTrigger(SlamAnimHash);

        // Wait for the animation to reach its impact frame before applying damage.
        // SlamImpactDelay in the config should be tuned to match the animation clip.
        yield return _impactDelayWait;

        DamagePlayersInRange();

        _activeCoroutine = null;
        IsExecuting      = false;
    }

    // ------------------------------------------------------------------
    // Damage
    // ------------------------------------------------------------------

    private void DamagePlayersInRange()
    {
        // The ground-crack shockwave fires at the real impact frame, regardless of whether the
        // player was actually in range to be hit (unlike shake/freeze/CA below,
        // which stay gated on a confirmed hit as before). The particle burst
        // alone reads as "dots", not a crack — the expanding/fading ground ring
        // is the actual "shockwave" read.
        CombatVFXManager.Instance?.SpawnImpact(_bossTransform.position, "BossAoESlam", _config.ImpactProfile);
        if (_config.ImpactProfile != null)
            CombatVFXManager.Instance?.SpawnGroundRing(
                _bossTransform.position, _config.ImpactProfile.VfxColor, _config.BlastRadius, GroundRingDuration);

        LayerMask mask = _config.TargetLayerMask;
        int hitCount;

        if (mask == 0)
        {
            Debug.LogWarning(
                "[BossAoESlamSkill] TargetLayerMask is Nothing (0). " +
                "Set it to your Player layer in BossSkillConfig_AoeSlam — " +
                "using Everything as fallback.");
            hitCount = Physics.OverlapSphereNonAlloc(
                _bossTransform.position, _config.BlastRadius, _hitBuffer);
        }
        else
        {
            hitCount = Physics.OverlapSphereNonAlloc(
                _bossTransform.position, _config.BlastRadius, _hitBuffer, mask);
        }

        // Clear before each activation — field is pre-allocated, no heap work here.
        _hitHandlers.Clear();

        // Capture defense state before the damage loop — these flags can change as a
        // side effect of TakeDamage() (barrier absorption clears HasBarrier). A dodge
        // (Dash i-frames) or a successful parry means zero damage was ever applied —
        // barrier-only used to be checked here, which let a clean dodge or parry play
        // the full slam shake/freeze/CA/FOV block as if the hit had actually landed.
        bool hadActiveDefense = _playerSkills != null &&
            (_playerSkills.HasBarrier || _playerSkills.IsInvincible || _playerSkills.IsParryActive);

        for (int i = 0; i < hitCount; i++)
        {
            // Skip the boss's own collider hierarchy.
            if (_hitBuffer[i].transform.IsChildOf(_bossTransform)) continue;

            // Explicitly target DamageHandler so the player's full defense chain applies.
            // HealthSystem also implements IDamageable but must not be called directly.
            DamageHandler handler = _hitBuffer[i].GetComponent<DamageHandler>()
                ?? _hitBuffer[i].GetComponentInParent<DamageHandler>();

            // Deduplicate: OverlapSphereNonAlloc returns one entry per collider.
            // A player with multiple colliders would receive one hit per collider
            // without this guard — exactly the -15 × 3 bug seen in testing.
            if (handler == null || _hitHandlers.Contains(handler)) continue;

            _hitHandlers.Add(handler);
            handler.TakeDamage(_config.Damage, "Boss AoE Slam");
        }

        // Slam shake + freeze when at least one player was confirmed hit
        // AND no active defense (barrier/dodge/parry) absorbed or blocked it. If the
        // shield absorbed the slam, PlayerCombatEffects handles the visual feedback
        // via its shield break flash — no camera effects needed here either way.
        if (_hitHandlers.Count > 0 && !hadActiveDefense)
        {
            ScreenShakeManager.Instance?.Shake(_config.ImpactProfile);
            HitFreezeManager.Instance?.Freeze(_config.ImpactProfile);
            // Own-instance Heavy-equivalent CA overrides the concurrent medium spike
            // from OnDamageTaken via CameraImpactEffects' Mathf.Max.
            CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);
            CameraImpactEffects.Instance?.TriggerFOVPunch(_config.ImpactProfile);
        }
    }

    // ------------------------------------------------------------------
    // VFX — red indicator sphere
    // ------------------------------------------------------------------

    private GameObject CreateSlamIndicator()
    {
        // Flat Quad on the floor instead of a sphere — cannot block the boss view
        // regardless of transparency and communicates "danger zone on the ground"
        // exactly as AoE indicators do in standard game design.
        var indicator = GameObject.CreatePrimitive(PrimitiveType.Quad);
        indicator.name = "SlamIndicator";

        var col = indicator.GetComponent<Collider>();
        if (col != null)
            Object.Destroy(col);

        // Rotate 90° around X so the quad faces upward (lies flat on the floor).
        // Small Y offset prevents Z-fighting with the arena floor mesh.
        indicator.transform.position   = _bossTransform.position + Vector3.up * 0.05f;
        indicator.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        indicator.transform.localScale = Vector3.one * (_config.BlastRadius * 2f);

        // Color is baked into _slamIndicatorMaterial — no per-indicator MPB needed.
        indicator.GetComponent<Renderer>().sharedMaterial = _slamIndicatorMaterial;

        return indicator;
    }

    private void DestroyIndicator()
    {
        if (_activeIndicator != null)
        {
            Object.Destroy(_activeIndicator);
            _activeIndicator = null;
        }
    }

    /// <summary>
    /// Destroy the indicator material instance — called from BossSkills.OnDestroy()
    /// to prevent a material leak on scene reload. UnityEngine.Object instances are
    /// not reclaimed by the C# GC and must be explicitly destroyed.
    /// </summary>
    public void Cleanup()
    {
        if (_slamIndicatorMaterial != null)
        {
            Object.Destroy(_slamIndicatorMaterial);
            _slamIndicatorMaterial = null;
        }
    }
}
